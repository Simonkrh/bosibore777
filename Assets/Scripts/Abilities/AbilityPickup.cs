using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class AbilityPickup : NetworkBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private GameObject defaultVisual;

    private readonly NetworkVariable<FixedString64Bytes> abilityId = new NetworkVariable<FixedString64Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private GameObject runtimeVisual;
    private bool warnedDefaultVisualIsRoot;
    private GameManager gameManager;

    public override void OnNetworkSpawn()
    {
        abilityId.OnValueChanged += HandleAbilityIdChanged;
        ApplyVisual(abilityId.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        abilityId.OnValueChanged -= HandleAbilityIdChanged;
        ClearRuntimeVisual();
    }

    public void InitializeServer(AbilityDefinition definition)
    {
        if (!IsServer || definition == null || string.IsNullOrWhiteSpace(definition.Id))
        {
            return;
        }

        abilityId.Value = definition.Id.Trim();
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
        if (!AbilityRuntimeDatabase.TryGetById(id, out AbilityDefinition definition) || definition == null)
        {
            return;
        }

        if (!tankAbilityController.TryAssignAbilityServer(definition))
        {
            return;
        }

        ResolveGameManager()?.PlayAbilityPickupSoundServer(transform.position);

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
        ApplyVisual(newValue.ToString());
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
}
