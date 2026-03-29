using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class AbilityPickupSpawner : NetworkBehaviour
{
    [SerializeField] private AbilityPickup pickupPrefab;
    [SerializeField] private float initialSpawnDelaySeconds = 2f;
    [SerializeField] private float minSpawnIntervalSeconds = 2f;
    [SerializeField] private float maxSpawnIntervalSeconds = 4f;
    [Tooltip("When enabled, maxActivePickups is ignored and spawning continues until no valid tiles remain.")]
    [SerializeField] private bool noLimitSpawning;
    [SerializeField] private int maxActivePickups = 3;
    [Tooltip("Pickup cannot spawn within this traversable tile distance from players (1 = same tile + directly reachable neighbors).")]
    [SerializeField] private int blockedTileRadiusAroundPlayers = 1;
    [SerializeField] private int maxSpawnPositionAttempts = 32;
    [SerializeField] private List<AbilityDefinition> spawnPool = new List<AbilityDefinition>();

    private readonly List<AbilityPickup> activePickups = new List<AbilityPickup>();
    private MazeGenerator mazeGenerator;
    private GameManager gameManager;
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
            GameManager resolvedGameManager = ResolveGameManager();
            bool megaBombLockdownActive = resolvedGameManager != null && resolvedGameManager.IsMegaBombLockdownActive;
            bool canSpawnMore = noLimitSpawning || activePickups.Count < Mathf.Max(0, maxActivePickups);
            if (!megaBombLockdownActive && canSpawnMore)
            {
                TrySpawnPickup();
            }

            float minInterval = Mathf.Min(minSpawnIntervalSeconds, maxSpawnIntervalSeconds);
            float maxInterval = Mathf.Max(minSpawnIntervalSeconds, maxSpawnIntervalSeconds);
            float nextSpawnDelay = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(Mathf.Max(0.1f, nextSpawnDelay));
        }
    }

    private void TrySpawnPickup()
    {
        if (pickupPrefab == null)
        {
            return;
        }

        GameManager resolvedGameManager = ResolveGameManager();
        if (resolvedGameManager != null && resolvedGameManager.IsMegaBombLockdownActive)
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

        if (mazeGenerator == null || !TryGetValidSpawnWorldPosition(out Vector3 worldPosition))
        {
            return;
        }

        float randomZRotation = Random.Range(-45f, 45f);
        AbilityPickup pickupInstance = Instantiate(
            pickupPrefab,
            worldPosition,
            Quaternion.Euler(0f, 0f, randomZRotation));
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

        if (resolvedGameManager != null)
        {
            resolvedGameManager.PlayAbilitySpawnSoundServer(pickupInstance.transform.position);
        }
    }

    private GameManager ResolveGameManager()
    {
        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        return gameManager;
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

    private bool TryGetValidSpawnWorldPosition(out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (mazeGenerator == null)
        {
            return false;
        }

        int attempts = Mathf.Max(1, maxSpawnPositionAttempts);
        for (int i = 0; i < attempts; i++)
        {
            if (!mazeGenerator.TryGetRandomAvailableCellWorldPosition(out Vector3 candidatePosition))
            {
                return false;
            }

            if (!mazeGenerator.TryWorldToCell(candidatePosition, out Vector2Int candidateCell))
            {
                continue;
            }

            if (IsBlockedByNearbyPlayer(candidateCell))
            {
                continue;
            }

            if (IsBlockedByExistingPickup(candidateCell))
            {
                continue;
            }

            worldPosition = candidatePosition;
            return true;
        }

        return false;
    }

    private bool IsBlockedByNearbyPlayer(Vector2Int candidateCell)
    {
        int tileRadius = Mathf.Max(0, blockedTileRadiusAroundPlayers);
        if (tileRadius <= 0)
        {
            return false;
        }

        TankController[] tanks = FindObjectsByType<TankController>(FindObjectsSortMode.None);
        for (int i = 0; i < tanks.Length; i++)
        {
            TankController tank = tanks[i];
            if (tank == null)
            {
                continue;
            }

            NetworkObject tankNetworkObject = tank.GetComponent<NetworkObject>();
            if (tankNetworkObject == null || !tankNetworkObject.IsSpawned)
            {
                continue;
            }

            if (!mazeGenerator.TryWorldToCell(tank.transform.position, out Vector2Int playerCell))
            {
                continue;
            }

            if (mazeGenerator.IsWithinPathDistanceInTiles(playerCell, candidateCell, tileRadius))
            {
                return true;
            }
        }

        return false;
    }

    private void OnValidate()
    {
        initialSpawnDelaySeconds = Mathf.Max(0f, initialSpawnDelaySeconds);
        minSpawnIntervalSeconds = Mathf.Max(0.1f, minSpawnIntervalSeconds);
        maxSpawnIntervalSeconds = Mathf.Max(minSpawnIntervalSeconds, maxSpawnIntervalSeconds);
        maxActivePickups = Mathf.Max(0, maxActivePickups);
        blockedTileRadiusAroundPlayers = Mathf.Max(0, blockedTileRadiusAroundPlayers);
        maxSpawnPositionAttempts = Mathf.Max(1, maxSpawnPositionAttempts);
    }

    private bool IsBlockedByExistingPickup(Vector2Int candidateCell)
    {
        AbilityPickup[] pickups = FindObjectsByType<AbilityPickup>(FindObjectsSortMode.None);
        for (int i = 0; i < pickups.Length; i++)
        {
            AbilityPickup pickup = pickups[i];
            if (pickup == null || !pickup.IsSpawned)
            {
                continue;
            }

            if (!mazeGenerator.TryWorldToCell(pickup.transform.position, out Vector2Int pickupCell))
            {
                continue;
            }

            if (pickupCell == candidateCell)
            {
                return true;
            }
        }

        return false;
    }
}
