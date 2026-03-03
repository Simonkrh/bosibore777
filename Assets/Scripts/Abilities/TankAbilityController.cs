using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

public class TankAbilityController : NetworkBehaviour
{
    [SerializeField] private Transform modelVisualMount;
    [FormerlySerializedAs("defaultTankVisual")]
    [SerializeField] private GameObject playerBodyVisual;
    [SerializeField] private bool hideDefaultTankWhenAbilityActive = true;
    [Tooltip("Optional: set explicit renderers to hide when a model override is active. If empty, renderers under PlayerBodyVisual are used.")]
    [SerializeField] private SpriteRenderer[] defaultBodyRenderers;
    [Tooltip("Optional: set colliders that represent the default tank hitbox and should be disabled when a model override is active.")]
    [SerializeField] private Collider2D[] defaultBodyHitboxColliders;
    [Tooltip("Legacy behavior. Keep disabled if PlayerBody also contains gameplay transforms (for example rotationChild).")]
    [SerializeField] private bool deactivatePlayerBodyObjectWhenAbilityActive;

    private readonly NetworkVariable<FixedString64Bytes> equippedAbilityId = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<int> activeAbilityUsageCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<FixedString128Bytes> overrideSpriteResourcePath = new NetworkVariable<FixedString128Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<FixedString128Bytes> overrideModelPrefabResourcePath = new NetworkVariable<FixedString128Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private GameObject runtimeModelOverride;
    private SpriteRenderer[] runtimeModelOverrideRenderers;
    private bool initializedCachedDefaults;

    public bool HasAbility => !string.IsNullOrWhiteSpace(equippedAbilityId.Value.ToString());
    public bool IsAbilityUsageActive => activeAbilityUsageCount.Value > 0;

    public override void OnNetworkSpawn()
    {
        equippedAbilityId.OnValueChanged += HandleEquippedAbilityChanged;
        overrideSpriteResourcePath.OnValueChanged += HandleOverrideSpritePathChanged;
        overrideModelPrefabResourcePath.OnValueChanged += HandleOverrideModelPrefabPathChanged;
        ApplyAbilityVisual(equippedAbilityId.Value.ToString());
    }

    private void OnDestroy()
    {
        equippedAbilityId.OnValueChanged -= HandleEquippedAbilityChanged;
        overrideSpriteResourcePath.OnValueChanged -= HandleOverrideSpritePathChanged;
        overrideModelPrefabResourcePath.OnValueChanged -= HandleOverrideModelPrefabPathChanged;
        ClearRuntimeModelOverride();
    }

    public bool TryAssignAbilityServer(AbilityDefinition definition)
    {
        if (!IsServer || definition == null || string.IsNullOrWhiteSpace(definition.Id))
        {
            return false;
        }

        overrideSpriteResourcePath.Value = default;
        overrideModelPrefabResourcePath.Value = default;
        equippedAbilityId.Value = definition.Id.Trim();
        return true;
    }

    public bool TryUseEquippedAbility(TankController owner, int shotSequence)
    {
        if (!IsServer || owner == null)
        {
            return false;
        }

        string currentAbilityId = equippedAbilityId.Value.ToString();
        if (string.IsNullOrWhiteSpace(currentAbilityId))
        {
            return false;
        }

        if (!AbilityRuntimeDatabase.TryGetById(currentAbilityId, out AbilityDefinition definition) ||
            definition == null ||
            definition.Behavior == null)
        {
            Debug.LogWarning($"[TankAbilityController] Unknown or invalid ability id '{currentAbilityId}'.");
            equippedAbilityId.Value = default;
            return false;
        }

        AbilityActivationResult activationResult = definition.Behavior.TryActivateServer(owner, shotSequence);
        if (activationResult == AbilityActivationResult.NotActivated)
        {
            return false;
        }

        if (activationResult == AbilityActivationResult.ActivatedConsume)
        {
            equippedAbilityId.Value = default;
        }

        return true;
    }

    public void NotifyAbilityInputReleasedServer(TankController owner)
    {
        if (!IsServer || owner == null)
        {
            return;
        }

        string currentAbilityId = equippedAbilityId.Value.ToString();
        if (string.IsNullOrWhiteSpace(currentAbilityId))
        {
            return;
        }

        if (!AbilityRuntimeDatabase.TryGetById(currentAbilityId, out AbilityDefinition definition) ||
            definition == null ||
            definition.Behavior == null)
        {
            return;
        }

        definition.Behavior.NotifyInputReleasedServer(owner);
    }

    public bool RegisterAbilityProjectileServer(NetworkObject projectileNetworkObject)
    {
        if (!IsServer || projectileNetworkObject == null || !projectileNetworkObject.IsSpawned)
        {
            return false;
        }

        Projectile projectile = projectileNetworkObject.GetComponent<Projectile>();
        if (projectile == null)
        {
            return false;
        }

        activeAbilityUsageCount.Value = Mathf.Max(0, activeAbilityUsageCount.Value) + 1;
        projectile.SetPreDestroyServerCallback((_, __) =>
        {
            if (!IsServer)
            {
                return;
            }

            activeAbilityUsageCount.Value = Mathf.Max(0, activeAbilityUsageCount.Value - 1);
        });

        return true;
    }

    public void ClearEquippedAbilityServer()
    {
        if (!IsServer)
        {
            return;
        }

        overrideSpriteResourcePath.Value = default;
        overrideModelPrefabResourcePath.Value = default;
        equippedAbilityId.Value = default;
    }

    public void SetModelOverrideSpriteResourceServer(string resourcesPath)
    {
        if (!IsServer)
        {
            return;
        }

        overrideSpriteResourcePath.Value = string.IsNullOrWhiteSpace(resourcesPath) ? default : resourcesPath.Trim();
    }

    public void SetModelOverridePrefabResourceServer(string resourcesPath)
    {
        if (!IsServer)
        {
            return;
        }

        overrideModelPrefabResourcePath.Value = string.IsNullOrWhiteSpace(resourcesPath) ? default : resourcesPath.Trim();
    }

    private void HandleEquippedAbilityChanged(FixedString64Bytes _, FixedString64Bytes newValue)
    {
        ApplyAbilityVisual(newValue.ToString());
    }

    private void HandleOverrideSpritePathChanged(FixedString128Bytes _, FixedString128Bytes newValue)
    {
        ApplyRuntimeModelOverrideSpriteFromPath(newValue.ToString());
    }

    private void HandleOverrideModelPrefabPathChanged(FixedString128Bytes _, FixedString128Bytes __)
    {
        ApplyAbilityVisual(equippedAbilityId.Value.ToString());
    }

    private void ApplyAbilityVisual(string abilityId)
    {
        ClearRuntimeModelOverride();

        bool hasAbility = !string.IsNullOrWhiteSpace(abilityId);
        if (!hasAbility || !AbilityRuntimeDatabase.TryGetById(abilityId, out AbilityDefinition definition) || definition == null)
        {
            SetDefaultBodyOverrideActive(false);
            return;
        }

        GameObject overridePrefab = ResolveModelOverridePrefab(definition);
        if (overridePrefab == null)
        {
            SetDefaultBodyOverrideActive(false);
            return;
        }

        SetDefaultBodyOverrideActive(true);

        Transform mount = ResolveModelMount();
        if (mount == null)
        {
            return;
        }

        runtimeModelOverride = Instantiate(overridePrefab, mount, false);
        runtimeModelOverride.transform.localPosition = Vector3.zero;
        runtimeModelOverride.transform.localRotation = Quaternion.identity;
        SetLayerRecursively(runtimeModelOverride, gameObject.layer);
        runtimeModelOverrideRenderers = runtimeModelOverride.GetComponentsInChildren<SpriteRenderer>(true);
        ApplyRuntimeModelOverrideSpriteFromPath(overrideSpriteResourcePath.Value.ToString());

        TankController ownerTank = GetComponent<TankController>();
        if (ownerTank != null)
        {
            ApplyModelOverrideColor(ownerTank.tankColor.Value);
        }
    }

    private Transform ResolveModelMount()
    {
        if (modelVisualMount != null)
        {
            return modelVisualMount;
        }

        return transform;
    }

    private GameObject ResolveModelOverridePrefab(AbilityDefinition definition)
    {
        string overridePrefabPath = overrideModelPrefabResourcePath.Value.ToString();
        if (!string.IsNullOrWhiteSpace(overridePrefabPath))
        {
            GameObject loadedOverride = Resources.Load<GameObject>(overridePrefabPath);
            if (loadedOverride != null)
            {
                return loadedOverride;
            }
        }

        return definition.TankModelOverridePrefab;
    }

    private void ClearRuntimeModelOverride()
    {
        if (runtimeModelOverride == null)
        {
            return;
        }

        Destroy(runtimeModelOverride);
        runtimeModelOverride = null;
        runtimeModelOverrideRenderers = null;
    }

    private void ApplyRuntimeModelOverrideSpriteFromPath(string resourcesPath)
    {
        if (runtimeModelOverrideRenderers == null || runtimeModelOverrideRenderers.Length == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(resourcesPath))
        {
            return;
        }

        Sprite loadedSprite = Resources.Load<Sprite>(resourcesPath);
        if (loadedSprite == null)
        {
            return;
        }

        for (int i = 0; i < runtimeModelOverrideRenderers.Length; i++)
        {
            if (runtimeModelOverrideRenderers[i] != null)
            {
                runtimeModelOverrideRenderers[i].sprite = loadedSprite;
            }
        }
    }

    public void ApplyModelOverrideColor(Color color)
    {
        if (runtimeModelOverrideRenderers == null || runtimeModelOverrideRenderers.Length == 0)
        {
            return;
        }

        for (int i = 0; i < runtimeModelOverrideRenderers.Length; i++)
        {
            if (runtimeModelOverrideRenderers[i] != null)
            {
                runtimeModelOverrideRenderers[i].color = color;
            }
        }
    }

    private void SetDefaultBodyOverrideActive(bool overrideActive)
    {
        bool shouldHideDefaultBody = overrideActive && hideDefaultTankWhenAbilityActive;
        if (!shouldHideDefaultBody)
        {
            SetDefaultVisualsEnabled(true);
            SetDefaultHitboxEnabled(true);
            return;
        }

        SetDefaultVisualsEnabled(false);
        SetDefaultHitboxEnabled(false);
    }

    private void SetDefaultVisualsEnabled(bool enabled)
    {
        CacheDefaultBodyReferencesIfNeeded();

        if (defaultBodyRenderers != null && defaultBodyRenderers.Length > 0)
        {
            for (int i = 0; i < defaultBodyRenderers.Length; i++)
            {
                if (defaultBodyRenderers[i] != null)
                {
                    defaultBodyRenderers[i].enabled = enabled;
                }
            }
        }

        if (playerBodyVisual != null && deactivatePlayerBodyObjectWhenAbilityActive)
        {
            playerBodyVisual.SetActive(enabled);
        }
    }

    private void SetDefaultHitboxEnabled(bool enabled)
    {
        if (defaultBodyHitboxColliders == null || defaultBodyHitboxColliders.Length == 0)
        {
            return;
        }

        for (int i = 0; i < defaultBodyHitboxColliders.Length; i++)
        {
            if (defaultBodyHitboxColliders[i] != null)
            {
                defaultBodyHitboxColliders[i].enabled = enabled;
            }
        }
    }

    private void CacheDefaultBodyReferencesIfNeeded()
    {
        if (initializedCachedDefaults)
        {
            return;
        }

        initializedCachedDefaults = true;
        if (playerBodyVisual == null)
        {
            return;
        }

        if (defaultBodyRenderers == null || defaultBodyRenderers.Length == 0)
        {
            defaultBodyRenderers = playerBodyVisual.GetComponentsInChildren<SpriteRenderer>(true);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null)
        {
            return;
        }

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].gameObject.layer = layer;
        }
    }
}
