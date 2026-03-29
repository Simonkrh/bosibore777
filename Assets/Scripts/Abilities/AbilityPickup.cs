using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class AbilityPickup : NetworkBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private GameObject defaultVisual;
    [SerializeField] private SmokeEffect spawnSmokeEffect;
    [SerializeField] private SpawnScaleAnimation spawnScaleAnimation;

    private readonly NetworkVariable<FixedString64Bytes> abilityId = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private GameObject runtimeVisual;
    private bool warnedDefaultVisualIsRoot;
    private bool hasPlayedSpawnSmokeEffect;
    private bool registeredMegaBombLockdownServer;
    private GameManager gameManager;

    private void Awake()
    {
        ResolveSpawnSmokeEffect();
        ResolveSpawnScaleAnimation();
    }

    public override void OnNetworkSpawn()
    {
        abilityId.OnValueChanged += HandleAbilityIdChanged;

        string id = abilityId.Value.ToString();
        ApplyVisual(id);

        if (IsServer)
        {
            StartCoroutine(BroadcastSpawnSmokeNextFrame());
        }
    }

    public override void OnNetworkDespawn()
    {
        abilityId.OnValueChanged -= HandleAbilityIdChanged;
        hasPlayedSpawnSmokeEffect = false;
        ClearRuntimeVisual();

        if (IsServer && registeredMegaBombLockdownServer)
        {
            registeredMegaBombLockdownServer = false;
            ResolveGameManager()?.EndMegaBombLockdownServer();
        }
    }

    public void InitializeServer(AbilityDefinition definition)
    {
        if (!IsServer || definition == null || string.IsNullOrWhiteSpace(definition.Id))
        {
            return;
        }

        string normalizedAbilityId = definition.Id.Trim();
        abilityId.Value = normalizedAbilityId;

        if (string.Equals(normalizedAbilityId, MegaBombAbilityBehavior.MegaBombAbilityId, System.StringComparison.Ordinal))
        {
            registeredMegaBombLockdownServer = true;
            GameManager resolvedGameManager = ResolveGameManager();
            resolvedGameManager?.BeginMegaBombLockdownServer(this);
            resolvedGameManager?.DespawnAllProjectiles();
            resolvedGameManager?.ForceClearAllTankAbilitiesServer();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        TankAbilityController tankAbilityController = other.GetComponentInParent<TankAbilityController>();
        if (tankAbilityController == null)
        {
            return;
        }

        TryAssignToTank(tankAbilityController);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        TankAbilityController tankAbilityController = other.GetComponentInParent<TankAbilityController>();
        if (tankAbilityController == null)
        {
            return;
        }

        TryAssignToTank(tankAbilityController);
    }

    private void TryAssignToTank(TankAbilityController tankAbilityController)
    {
        if (tankAbilityController == null || tankAbilityController.HasAbility || tankAbilityController.IsAbilityUsageActive)
        {
            return;
        }

        string id = abilityId.Value.ToString();
        GameManager resolvedGameManager = ResolveGameManager();
        if (resolvedGameManager != null &&
            resolvedGameManager.IsMegaBombLockdownActive &&
            !string.Equals(id, MegaBombAbilityBehavior.MegaBombAbilityId, System.StringComparison.Ordinal))
        {
            return;
        }

        if (!AbilityRuntimeDatabase.TryGetById(id, out AbilityDefinition definition) || definition == null)
        {
            return;
        }

        if (!tankAbilityController.TryAssignAbilityServer(definition))
        {
            return;
        }

        resolvedGameManager?.PlayAbilityPickupSoundServer(transform.position);

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void HandleAbilityIdChanged(FixedString64Bytes _, FixedString64Bytes newValue)
    {
        string id = newValue.ToString();
        ApplyVisual(id);
    }

    private IEnumerator BroadcastSpawnSmokeNextFrame()
    {
        yield return null;

        if (!IsServer || !IsSpawned)
        {
            yield break;
        }

        PlaySpawnSmokeClientRpc();
    }

    [ClientRpc]
    private void PlaySpawnSmokeClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        TryPlaySpawnSmokeEffect();
    }

    private void ApplyVisual(string id)
    {
        ClearRuntimeVisual();

        SetDefaultVisualActiveSafely(true);

        if (!AbilityRuntimeDatabase.TryGetById(id, out AbilityDefinition definition) || definition == null)
        {
            return;
        }

        if (definition.PickupVisualPrefab == null)
        {
            return;
        }

        Transform parent = visualRoot != null ? visualRoot : transform;
        runtimeVisual = Instantiate(definition.PickupVisualPrefab, parent, false);
        runtimeVisual.transform.localPosition = Vector3.zero;
        runtimeVisual.transform.localRotation = Quaternion.identity;

        SetDefaultVisualActiveSafely(false);
        PlaySpawnScaleAnimation();
    }

    private void ClearRuntimeVisual()
    {
        if (runtimeVisual == null)
        {
            return;
        }

        Destroy(runtimeVisual);
        runtimeVisual = null;
    }

    private GameManager ResolveGameManager()
    {
        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        return gameManager;
    }

    private void TryPlaySpawnSmokeEffect()
    {
        if (hasPlayedSpawnSmokeEffect)
        {
            return;
        }

        SmokeEffect smokeEffect = ResolveSpawnSmokeEffect();
        if (smokeEffect == null)
        {
            return;
        }

        hasPlayedSpawnSmokeEffect = true;
        smokeEffect.Play();
    }

    private void SetDefaultVisualActiveSafely(bool isActive)
    {
        if (defaultVisual == null)
        {
            return;
        }

        if (defaultVisual == gameObject)
        {
            if (!warnedDefaultVisualIsRoot)
            {
                warnedDefaultVisualIsRoot = true;
                Debug.LogWarning("[AbilityPickup] defaultVisual points to the pickup root object. Assign a child visual object instead to avoid deactivating the whole pickup.");
            }

            return;
        }

        defaultVisual.SetActive(isActive);
    }

    private SmokeEffect ResolveSpawnSmokeEffect()
    {
        if (spawnSmokeEffect == null)
        {
            spawnSmokeEffect = GetComponent<SmokeEffect>();
        }

        return spawnSmokeEffect;
    }

    private SpawnScaleAnimation ResolveSpawnScaleAnimation()
    {
        if (spawnScaleAnimation == null)
        {
            spawnScaleAnimation = GetComponent<SpawnScaleAnimation>();
        }

        return spawnScaleAnimation;
    }

    private void PlaySpawnScaleAnimation()
    {
        SpawnScaleAnimation scaleAnimation = ResolveSpawnScaleAnimation();
        if (scaleAnimation == null || runtimeVisual == null)
        {
            return;
        }

        scaleAnimation.SetTarget(runtimeVisual.transform);
        scaleAnimation.Play();
    }

    private void OnValidate()
    {
        ResolveSpawnSmokeEffect();
        ResolveSpawnScaleAnimation();
    }
}
