using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class AbilityPickupSpawner : NetworkBehaviour
{
    [SerializeField] private AbilityPickup pickupPrefab;
    [SerializeField] private float initialSpawnDelaySeconds = 2f;
    [SerializeField] private float spawnIntervalSeconds = 8f;
    [SerializeField] private int maxActivePickups = 3;
    [SerializeField] private List<AbilityDefinition> spawnPool = new List<AbilityDefinition>();

    private readonly List<AbilityPickup> activePickups = new List<AbilityPickup>();
    private MazeGenerator mazeGenerator;
    private Coroutine spawnRoutine;

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        spawnRoutine = StartCoroutine(SpawnLoop());
    }

    public override void OnNetworkDespawn()
    {
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }

        CleanupInactivePickups();
    }

    public void DespawnAllPickupsServer()
    {
        if (!IsServer)
        {
            return;
        }

        CleanupInactivePickups();
        for (int i = 0; i < activePickups.Count; i++)
        {
            AbilityPickup pickup = activePickups[i];
            if (pickup == null)
            {
                continue;
            }

            NetworkObject pickupNetworkObject = pickup.GetComponent<NetworkObject>();
            if (pickupNetworkObject != null && pickupNetworkObject.IsSpawned)
            {
                pickupNetworkObject.Despawn(true);
            }
            else
            {
                Destroy(pickup.gameObject);
            }
        }

        activePickups.Clear();
    }

    private IEnumerator SpawnLoop()
    {
        float initialDelay = Mathf.Max(0f, initialSpawnDelaySeconds);
        if (initialDelay > 0f)
        {
            yield return new WaitForSeconds(initialDelay);
        }

        while (IsServer && IsSpawned)
        {
            CleanupInactivePickups();
            if (activePickups.Count < Mathf.Max(0, maxActivePickups))
            {
                TrySpawnPickup();
            }

            yield return new WaitForSeconds(Mathf.Max(0.1f, spawnIntervalSeconds));
        }
    }

    private void TrySpawnPickup()
    {
        if (pickupPrefab == null)
        {
            return;
        }

        AbilityDefinition definition = SelectRandomDefinition();
        if (definition == null)
        {
            return;
        }

        if (mazeGenerator == null)
        {
            mazeGenerator = FindFirstObjectByType<MazeGenerator>();
        }

        if (mazeGenerator == null || !mazeGenerator.TryGetRandomAvailableCellWorldPosition(out Vector3 worldPosition))
        {
            return;
        }

        AbilityPickup pickupInstance = Instantiate(pickupPrefab, worldPosition, Quaternion.identity);
        NetworkObject pickupNetworkObject = pickupInstance.GetComponent<NetworkObject>();
        if (pickupNetworkObject == null)
        {
            Debug.LogError("[AbilityPickupSpawner] Pickup prefab requires a NetworkObject component.");
            Destroy(pickupInstance.gameObject);
            return;
        }

        pickupNetworkObject.Spawn(true);
        pickupInstance.InitializeServer(definition);
        activePickups.Add(pickupInstance);
    }

    private AbilityDefinition SelectRandomDefinition()
    {
        List<AbilityDefinition> candidates = new List<AbilityDefinition>();
        for (int i = 0; i < spawnPool.Count; i++)
        {
            if (spawnPool[i] != null)
            {
                candidates.Add(spawnPool[i]);
            }
        }

        if (candidates.Count == 0)
        {
            IReadOnlyList<AbilityDefinition> allDefinitions = AbilityRuntimeDatabase.GetAllDefinitions();
            for (int i = 0; i < allDefinitions.Count; i++)
            {
                if (allDefinitions[i] != null)
                {
                    candidates.Add(allDefinitions[i]);
                }
            }
        }

        return AbilityRuntimeDatabase.PickRandomDefinition(candidates);
    }

    private void CleanupInactivePickups()
    {
        int index = 0;
        while (index < activePickups.Count)
        {
            AbilityPickup pickup = activePickups[index];
            bool remove = pickup == null || !pickup.IsSpawned;
            if (remove)
            {
                activePickups.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }
    }
}
