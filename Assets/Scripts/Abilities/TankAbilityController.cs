using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

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
    private readonly List<NetworkObject> activeAbilityProjectiles = new List<NetworkObject>();
    private bool ownsMegaBombLockdownServer;
    private Coroutine delayedMegaBombLockdownReleaseCoroutine;

    public bool HasAbility => !string.IsNullOrWhiteSpace(equippedAbilityId.Value.ToString());
    public bool IsAbilityUsageActive => activeAbilityUsageCount.Value > 0;
    public string EquippedAbilityId => equippedAbilityId.Value.ToString();

    public bool HasEquippedAbilityId(string abilityId)
    {
        return !string.IsNullOrWhiteSpace(abilityId) &&
               string.Equals(equippedAbilityId.Value.ToString(), abilityId, System.StringComparison.Ordinal);
    }

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

        if (delayedMegaBombLockdownReleaseCoroutine != null)
        {
            StopCoroutine(delayedMegaBombLockdownReleaseCoroutine);
            delayedMegaBombLockdownReleaseCoroutine = null;
        }

        if (ownsMegaBombLockdownServer)
        {
            ownsMegaBombLockdownServer = false;
            ResolveGameManager()?.EndMegaBombLockdownServer();
        }
    }

    public bool TryAssignAbilityServer(AbilityDefinition definition)
    {
        if (!IsServer || definition == null || string.IsNullOrWhiteSpace(definition.Id))
        {
            return false;
        }

        CancelDelayedMegaBombLockdownReleaseServer();
        string normalizedAbilityId = definition.Id.Trim();
        UpdateMegaBombOwnershipLockServer(normalizedAbilityId);
        overrideSpriteResourcePath.Value = default;
        overrideModelPrefabResourcePath.Value = default;
        equippedAbilityId.Value = normalizedAbilityId;
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
            ClearEquippedAbilityServer();
            return false;
        }

        AbilityActivationResult activationResult = definition.Behavior.TryActivateServer(owner, shotSequence);
        if (activationResult == AbilityActivationResult.NotActivated)
        {
            return false;
        }

        if (activationResult == AbilityActivationResult.ActivatedConsume)
        {
            ClearEquippedAbilityServer();
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

    public void NotifyOwnerDiedServer(TankController owner)
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

        definition.Behavior.NotifyOwnerDiedServer(owner);
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

        if (!activeAbilityProjectiles.Contains(projectileNetworkObject))
        {
            activeAbilityProjectiles.Add(projectileNetworkObject);
        }

        TankAbilityController controller = this;
        NetworkObject trackedProjectile = projectileNetworkObject;
        activeAbilityUsageCount.Value = Mathf.Max(0, activeAbilityUsageCount.Value) + 1;
        projectile.SetPreDestroyServerCallback((_, __) =>
        {
            if (controller == null || !controller.IsServer || !controller.IsSpawned)
            {
                return;
            }

            controller.activeAbilityProjectiles.Remove(trackedProjectile);
            controller.activeAbilityUsageCount.Value = Mathf.Max(0, controller.activeAbilityUsageCount.Value - 1);
        });

        return true;
    }

    public void ForceClearAbilityServer()
    {
        if (!IsServer)
        {
            return;
        }

        MinigunAbilityRuntime minigunRuntime = GetComponent<MinigunAbilityRuntime>();
        if (minigunRuntime != null)
        {
            minigunRuntime.ForceCancelServer();
        }

        NetworkObject[] trackedProjectiles = activeAbilityProjectiles.ToArray();
        activeAbilityProjectiles.Clear();
        for (int i = 0; i < trackedProjectiles.Length; i++)
        {
            NetworkObject trackedProjectile = trackedProjectiles[i];
            if (trackedProjectile == null)
            {
                continue;
            }

            Projectile projectile = trackedProjectile.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.ForceDestroy();
                continue;
            }

            if (trackedProjectile.IsSpawned)
            {
                trackedProjectile.Despawn(true);
            }
        }

        activeAbilityUsageCount.Value = 0;
        ClearEquippedAbilityServer();
    }

    public void ClearEquippedAbilityServer(float megaBombLockdownReleaseDelaySeconds = 0f)
    {
        if (!IsServer)
        {
            return;
        }

        bool delayMegaBombLockdownRelease =
            ownsMegaBombLockdownServer &&
            megaBombLockdownReleaseDelaySeconds > 0f;

        CancelDelayedMegaBombLockdownReleaseServer();
        if (delayMegaBombLockdownRelease)
        {
            delayedMegaBombLockdownReleaseCoroutine = StartCoroutine(
                ReleaseMegaBombLockdownAfterDelayServer(megaBombLockdownReleaseDelaySeconds));
        }
        else
        {
            UpdateMegaBombOwnershipLockServer(string.Empty);
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

    private GameManager ResolveGameManager()
    {
        TankController ownerTank = GetComponent<TankController>();
        if (ownerTank != null)
        {
            GameManager ownerGameManager = ownerTank.ResolveGameManager();
            if (ownerGameManager != null)
            {
                return ownerGameManager;
            }
        }

        return FindFirstObjectByType<GameManager>();
    }

    private void CancelDelayedMegaBombLockdownReleaseServer()
    {
        if (delayedMegaBombLockdownReleaseCoroutine == null)
        {
            return;
        }

        StopCoroutine(delayedMegaBombLockdownReleaseCoroutine);
        delayedMegaBombLockdownReleaseCoroutine = null;
    }

    private System.Collections.IEnumerator ReleaseMegaBombLockdownAfterDelayServer(float delaySeconds)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));
        delayedMegaBombLockdownReleaseCoroutine = null;
        UpdateMegaBombOwnershipLockServer(string.Empty);
    }

    private void UpdateMegaBombOwnershipLockServer(string nextAbilityId)
    {
        bool shouldOwnMegaBombLockdown =
            string.Equals(nextAbilityId, MegaBombAbilityBehavior.MegaBombAbilityId, System.StringComparison.Ordinal);

        if (shouldOwnMegaBombLockdown == ownsMegaBombLockdownServer)
        {
            return;
        }

        if (shouldOwnMegaBombLockdown)
        {
            ownsMegaBombLockdownServer = true;
            ResolveGameManager()?.BeginMegaBombLockdownServer();
            return;
        }

        ownsMegaBombLockdownServer = false;
        ResolveGameManager()?.EndMegaBombLockdownServer();
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

    public SpriteRenderer GetActiveBodyRenderer()
    {
        if (runtimeModelOverrideRenderers != null && runtimeModelOverrideRenderers.Length > 0)
        {
            for (int i = 0; i < runtimeModelOverrideRenderers.Length; i++)
            {
                if (runtimeModelOverrideRenderers[i] != null)
                {
                    return runtimeModelOverrideRenderers[i];
                }
            }
        }

        CacheDefaultBodyReferencesIfNeeded();
        if (defaultBodyRenderers != null && defaultBodyRenderers.Length > 0)
        {
            for (int i = 0; i < defaultBodyRenderers.Length; i++)
            {
                if (defaultBodyRenderers[i] != null)
                {
                    return defaultBodyRenderers[i];
                }
            }
        }

        return null;
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
