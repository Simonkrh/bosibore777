using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

public class GameManager : NetworkBehaviour
{
    private struct ClientDisplayState
    {
        public int Score;
        public Color IconColor;
        public bool HasIconColor;
    }

    public GameObject playerPrefab;
    public MazeGenerator mazeGenerator;
    public GameObject projectilesContainer;
    private PlayerDisplayManager displayManager;

    private HashSet<ulong> alivePlayers = new HashSet<ulong>();

    private Dictionary<ulong, int> playerScores = new Dictionary<ulong, int>();

    private Dictionary<ulong, GameObject> clientIdToPlayer = new Dictionary<ulong, GameObject>();

    private List<Vector2Int> availableCells = new List<Vector2Int>();
    private bool startingNewRound = false;
    private readonly List<Color> primaryColors = new List<Color> { Color.green, Color.red, Color.blue };
    private readonly List<Color> availablePrimaryColors = new List<Color>();
    private readonly Dictionary<ulong, Color> playerColors = new Dictionary<ulong, Color>();
    private float nextAutoSpawnCheckTime = 0f;
    private const float AutoSpawnCheckIntervalSeconds = 0.25f;
    private static bool collisionLayersConfigured;
    private readonly Dictionary<ulong, ClientDisplayState> clientDisplayStates = new Dictionary<ulong, ClientDisplayState>();
    private bool clientDisplayStateDirty;

    private void Awake()
    {
        ConfigureCollisionLayers();
        availablePrimaryColors.AddRange(primaryColors);
    }

    private void Start()
    {
        displayManager = FindObjectOfType<PlayerDisplayManager>();
        if (displayManager == null)
        {
            Debug.LogError("PlayerDisplayManager not found in the scene!");
        }
    }

    private void ConfigureCollisionLayers()
    {
        if (collisionLayersConfigured)
        {
            return;
        }

        int playerLayer = LayerMask.NameToLayer("Player");
        int bulletLayer = LayerMask.NameToLayer("Bullet");
        int bulletTriggerLayer = LayerMask.NameToLayer("BulletTrigger");

        if (playerLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(playerLayer, playerLayer, true);
        }
        else
        {
            Debug.LogWarning("[GameManager] Layer 'Player' was not found.");
        }

        if (bulletLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(bulletLayer, bulletLayer, true);
        }
        else
        {
            Debug.LogWarning("[GameManager] Layer 'Bullet' was not found.");
        }

        if (bulletLayer >= 0 && bulletTriggerLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(bulletLayer, bulletTriggerLayer, true);
        }

        if (bulletTriggerLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(bulletTriggerLayer, bulletTriggerLayer, true);
        }

        collisionLayersConfigured = true;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        nextAutoSpawnCheckTime = 0f;
        TrySpawnMissingPlayers();
    }

    private void Update()
    {
        if (IsClient)
        {
            TryApplyClientDisplayState();
        }

        if (!IsServer || !IsSpawned || NetworkManager == null)
        {
            return;
        }

        if (startingNewRound)
        {
            return;
        }

        if (Time.unscaledTime < nextAutoSpawnCheckTime)
        {
            return;
        }

        nextAutoSpawnCheckTime = Time.unscaledTime + AutoSpawnCheckIntervalSeconds;
        TrySpawnMissingPlayers();
    }

    private void StageScoreForClientUi(ulong clientId, int score)
    {
        if (!clientDisplayStates.TryGetValue(clientId, out ClientDisplayState state))
        {
            state = new ClientDisplayState();
        }

        state.Score = score;
        clientDisplayStates[clientId] = state;
        clientDisplayStateDirty = true;
    }

    private void StageColorForClientUi(ulong clientId, Color color)
    {
        if (!clientDisplayStates.TryGetValue(clientId, out ClientDisplayState state))
        {
            state = new ClientDisplayState();
        }

        state.IconColor = color;
        state.HasIconColor = true;
        clientDisplayStates[clientId] = state;
        clientDisplayStateDirty = true;
    }

    private void RemoveClientFromUiState(ulong clientId)
    {
        clientDisplayStates.Remove(clientId);
        clientDisplayStateDirty = true;
    }

    private void TryApplyClientDisplayState()
    {
        if (!clientDisplayStateDirty)
        {
            return;
        }

        PlayerDisplayManager manager = PlayerDisplayManager.Instance;
        if (manager == null || !manager.IsReady())
        {
            return;
        }

        List<ulong> displayedClientIds = manager.GetDisplayedClientIds();
        for (int i = 0; i < displayedClientIds.Count; i++)
        {
            ulong displayedClientId = displayedClientIds[i];
            if (!clientDisplayStates.ContainsKey(displayedClientId))
            {
                manager.RemovePlayerDisplay(displayedClientId);
            }
        }

        foreach (var kvp in clientDisplayStates)
        {
            ulong clientId = kvp.Key;
            ClientDisplayState state = kvp.Value;

            if (manager.HasPlayerDisplay(clientId))
            {
                manager.UpdatePlayerScore(clientId, state.Score);
            }
            else
            {
                manager.CreatePlayerDisplay(clientId, state.Score);
            }

            if (state.HasIconColor)
            {
                manager.SetIconColor(clientId, state.IconColor);
            }
        }

        clientDisplayStateDirty = false;
    }

    private void TrySpawnMissingPlayers()
    {
        if (NetworkManager == null)
        {
            return;
        }

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            ulong clientId = client.ClientId;
            if (HasSpawnForClient(clientId))
            {
                continue;
            }

            if (!CanSpawnPlayerNow())
            {
                Debug.Log($"[GameManager] Auto-spawn delayed for client {clientId}: {GetSpawnReadinessReason()}.");
                continue;
            }

            if (SpawnPlayerOnConnect(clientId))
            {
                Debug.Log($"[GameManager] Auto-spawned player for client {clientId}.");
            }
        }
    }

    private Color AssignColor(ulong clientId)
    {

        if (playerColors.TryGetValue(clientId, out Color existingColor))
        {
            return existingColor;
        }

        Color assignedColor;

        if (availablePrimaryColors.Count > 0)
        {
            // Assign the first available primary color
            assignedColor = availablePrimaryColors[0];
            availablePrimaryColors.RemoveAt(0);
        }
        else
        {
            // Assign a random color
            assignedColor = UnityEngine.Random.ColorHSV();
        }

        playerColors[clientId] = assignedColor;

        return assignedColor;
    }

    private void ReleaseColor(ulong clientId)
    {
        if (playerColors.TryGetValue(clientId, out Color color))
        {
            if (primaryColors.Contains(color))
            {
                availablePrimaryColors.Add(color);
            }

            playerColors.Remove(clientId);
        }
    }

    public bool SpawnPlayerOnConnect(ulong clientId)
    {
        if (HasSpawnForClient(clientId))
        {
            return true;
        }

        if (!CanSpawnPlayerNow())
        {
            return false;
        }

        Color playerColor = AssignColor(clientId);

        if (!spawnPlayer(clientId))
        {
            return false;
        }

        InitializePlayerDisplayAndColors(clientId);
        AssignIconColorClientRpc(clientId, playerColor);
        AssignTankColor(clientId, playerColor);
        return true;
    }

    public bool SpawnPlayerOnNewRound(ulong clientId)
    {
        if (HasSpawnForClient(clientId))
        {
            return true;
        }

        if (!CanSpawnPlayerNow())
        {
            return false;
        }

        if (!playerColors.TryGetValue(clientId, out Color existingColor))
        {
            existingColor = AssignColor(clientId);
        }

        if (!spawnPlayer(clientId))
        {
            return false;
        }

        AssignIconColorClientRpc(clientId, existingColor);
        AssignTankColor(clientId, existingColor);
        return true;
    }

    private bool spawnPlayer(ulong clientId)
    {
        if (availableCells == null || availableCells.Count == 0)
        {
            Debug.LogWarning("No available cells for spawning players.");
            return false;
        }


        int cellIndex = UnityEngine.Random.Range(0, availableCells.Count);
        Vector2Int cell = availableCells[cellIndex];

        Vector3 spawnPosition = mazeGenerator.CellToWorldPosition(cell);

        try
        {
            NetworkObject spawnedPlayerNetworkObject = NetworkObject.InstantiateAndSpawn(
                playerPrefab,
                NetworkManager,
                ownerClientId: clientId,
                destroyWithScene: false,
                isPlayerObject: false,
                forceOverride: false,
                position: spawnPosition,
                rotation: Quaternion.identity
            );

            if (spawnedPlayerNetworkObject == null)
            {
                Debug.LogError($"[GameManager] InstantiateAndSpawn returned null for client {clientId}.");
                return false;
            }

            availableCells.RemoveAt(cellIndex);

            GameObject player = spawnedPlayerNetworkObject.gameObject;
            ApplyPlayerCollisionSettings(player, clientId);
            alivePlayers.Add(clientId);
            clientIdToPlayer[clientId] = player;

            Debug.Log($"[Server] Spawned player {clientId} at cell {cell} (world position {spawnPosition})");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameManager] InstantiateAndSpawn failed for client {clientId}: {ex.Message}");
            return false;
        }
    }

    public bool HasSpawnForClient(ulong clientId)
    {
        return clientIdToPlayer.TryGetValue(clientId, out GameObject player) && player != null;
    }

    private void ApplyPlayerCollisionSettings(GameObject player, ulong ownerClientId)
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
        {
            SetLayerRecursively(player, playerLayer);
        }

        Collider2D[] newPlayerColliders = player.GetComponentsInChildren<Collider2D>(true);
        foreach (var kvp in clientIdToPlayer)
        {
            if (kvp.Key == ownerClientId || kvp.Value == null)
            {
                continue;
            }

            Collider2D[] existingPlayerColliders = kvp.Value.GetComponentsInChildren<Collider2D>(true);
            IgnoreColliderPairs(newPlayerColliders, existingPlayerColliders);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].gameObject.layer = layer;
        }
    }

    private static void IgnoreColliderPairs(Collider2D[] first, Collider2D[] second)
    {
        for (int i = 0; i < first.Length; i++)
        {
            Collider2D firstCollider = first[i];
            if (firstCollider == null)
            {
                continue;
            }

            for (int j = 0; j < second.Length; j++)
            {
                Collider2D secondCollider = second[j];
                if (secondCollider == null)
                {
                    continue;
                }

                Physics2D.IgnoreCollision(firstCollider, secondCollider, true);
            }
        }
    }

    public bool CanSpawnPlayerNow()
    {
        if (mazeGenerator == null)
        {
            mazeGenerator = FindFirstObjectByType<MazeGenerator>();
        }

        return playerPrefab != null &&
               mazeGenerator != null &&
               availableCells != null &&
               availableCells.Count > 0;
    }

    public string GetSpawnReadinessReason()
    {
        if (playerPrefab == null)
        {
            return "playerPrefab is null";
        }

        if (mazeGenerator == null)
        {
            return "mazeGenerator is null";
        }

        if (availableCells == null)
        {
            return "availableCells is null";
        }

        if (availableCells.Count <= 0)
        {
            return "availableCells is empty";
        }

        return "ready";
    }

    [ClientRpc]
    private void AssignIconColorClientRpc(ulong clientId, Color color, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        StageColorForClientUi(clientId, color);
        TryApplyClientDisplayState();
    }

    private void AssignTankColor(ulong clientId, Color color)
    {
        if (clientIdToPlayer.TryGetValue(clientId, out GameObject player))
        {
            TankController tank = player.GetComponent<TankController>();
            if (tank != null)
            {
                tank.ServerSetColor(color);
            }
            else
            {
                Debug.LogWarning($"TankController not found on player {clientId}");
            }
        }
    }

    public void InitializePlayerDisplayAndColors(ulong clientId)
    {
        if (!playerScores.ContainsKey(clientId))
        {
            playerScores[clientId] = 0; // Initialize score if not present
        }

        CreatePlayerDisplayClientRpc(clientId, playerScores[clientId]);

        List<ulong> existingClientIds = new List<ulong>();
        List<int> existingScores = new List<int>();
        List<Color> existingColors = new List<Color>();

        foreach (var kvp in playerScores)
        {
            if (kvp.Key != clientId) // Exclude the new client
            {
                existingClientIds.Add(kvp.Key);
                existingScores.Add(kvp.Value);

                if (playerColors.TryGetValue(kvp.Key, out Color color))
                {
                    existingColors.Add(color);
                }
                else
                {
                    existingColors.Add(Color.white); // Default color if not found
                }
            }
        }

        // Convert lists to arrays for serialization
        ulong[] existingClientIdsArray = existingClientIds.ToArray();
        int[] existingScoresArray = existingScores.ToArray();
        Color[] existingColorsArray = existingColors.ToArray();

        if (existingClientIdsArray.Length > 0)
        {
            // Define ClientRpcParams to target only the new client
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { clientId }
                }
            };

            // Send existing players' data to the new client
            SendExistingPlayerDisplaysClientRpc(existingClientIdsArray, existingScoresArray, clientRpcParams);
            SendExistingPlayerIconColorsClientRpc(existingClientIdsArray, existingColorsArray, clientRpcParams);
        }
    }

    [ClientRpc]
    private void SendExistingPlayerDisplaysClientRpc(ulong[] clientIds, int[] scores, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        int count = Mathf.Min(clientIds.Length, scores.Length);
        for (int i = 0; i < count; i++)
        {
            StageScoreForClientUi(clientIds[i], scores[i]);
        }

        TryApplyClientDisplayState();
    }

    [ClientRpc]
    private void SendExistingPlayerIconColorsClientRpc(ulong[] clientIds, Color[] colors, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        int count = Mathf.Min(clientIds.Length, colors.Length);
        for (int i = 0; i < count; i++)
        {
            StageColorForClientUi(clientIds[i], colors[i]);
        }

        TryApplyClientDisplayState();
    }

    /*
    [ClientRpc]
    private void SendExistingTankColorsClientRpc(ulong[] clientIds, Color[] colors, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        for (int i = 0; i < clientIds.Length; i++)
        {
            if (clientIdToPlayer.TryGetValue(clientIds[i], out GameObject player))
            {
                TankController tank = player.GetComponent<TankController>();
                if (tank != null)
                {
                    Debug.Log(colors[i]);
                    tank.SetColor(colors[i]);
                }
            }
        }
    }
    */
    public void SetAvailableCells(List<Vector2Int> cells)
    {
        availableCells = cells;
        Debug.Log($"[GameManager] Received available cells: {availableCells.Count}");
    }

    [ClientRpc]
    private void CreatePlayerDisplayClientRpc(ulong clientId, int initialScore)
    {
        if (!IsClient) return;

        StageScoreForClientUi(clientId, initialScore);
        TryApplyClientDisplayState();
    }

    [ClientRpc]
    private void UpdatePlayerScoreClientRpc(ulong clientId, int newScore)
    {
        if (!IsClient) return; // Server doesn't need to handle client-side UI

        StageScoreForClientUi(clientId, newScore);
        TryApplyClientDisplayState();
    }

    [ClientRpc]
    public void RemovePlayerDisplayClientRpc(ulong clientId)
    {
        if (!IsClient) return;

        RemoveClientFromUiState(clientId);
        TryApplyClientDisplayState();
    }
    private void RemovePlayerOnNewRound(ulong clientId)
    {
        if (!IsServer) return;

        if (alivePlayers.Contains(clientId))
        {
            alivePlayers.Remove(clientId);
        }

        if (clientIdToPlayer.ContainsKey(clientId))
        {
            clientIdToPlayer.Remove(clientId);
        }

    }

    public void DespawnPlayer(ulong clientId)
    {
        if (clientIdToPlayer.TryGetValue(clientId, out GameObject player))
        {
            if (player != null)
            {
                player.GetComponent<NetworkObject>().Despawn(true);
                Destroy(player);
                Debug.Log($"[Server] Despawned player {clientId}");
            }
        }
        else
        {
            Debug.LogWarning($"[Server] Attempted to despawn player {clientId}, but no such player was found.");
        }
    }

    public void RemovePlayerOnDisconnect(ulong clientId)
    {
        if (!IsServer) return;

        if (alivePlayers.Contains(clientId))
        {
            alivePlayers.Remove(clientId);
        }

        if (playerScores.ContainsKey(clientId))
        {
            playerScores.Remove(clientId);
        }

        if (clientIdToPlayer.ContainsKey(clientId))
        {
            clientIdToPlayer.Remove(clientId);
        }

        ReleaseColor(clientId);

        RemovePlayerDisplayClientRpc(clientId);
    }

    public void DespawnAllProjectiles()
    {
        if (projectilesContainer == null)
        {
            Debug.LogError("[GameManager] ProjectilesContainer is not assigned.");
            return;
        }

        foreach (Transform projectileTransform in projectilesContainer.transform)
        {
            NetworkObject projectileNetObj = projectileTransform.GetComponent<NetworkObject>();
            if (projectileNetObj != null && projectileNetObj.IsSpawned)
            {
                projectileNetObj.Despawn(true);
            }
            else
            {
                Debug.LogWarning($"[GameManager] Projectile {projectileTransform.name} has no NetworkObject or is already despawned.");
            }
        }
    }

    private void StartNewRound()
    {
        if (!IsServer) return;

        StartCoroutine(StartNewRoundCoroutine());
    }

    private IEnumerator StartNewRoundCoroutine()
    {
        Debug.Log("[Server] Starting new round...");

        DespawnAllPlayersForNewRound();

        DespawnAllProjectiles();

        if (mazeGenerator == null)
        {
            mazeGenerator = FindFirstObjectByType<MazeGenerator>();
        }

        if (mazeGenerator == null)
        {
            Debug.LogError("[GameManager] Cannot start a new round because MazeGenerator is missing.");
            startingNewRound = false;
            yield break;
        }

        // Regenerate and sync the maze
        mazeGenerator.RegenerateMaze();

        // Wait for the maze to sync
        yield return new WaitForSeconds(1f); // Adjust based on synchronization speed

        // Spawn players after the maze has been regenerated and synced
        if (NetworkManager == null)
        {
            Debug.LogError("[GameManager] NetworkManager is null while starting a new round.");
            startingNewRound = false;
            yield break;
        }

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (!SpawnPlayerOnNewRound(client.ClientId))
            {
                Debug.LogWarning($"[GameManager] SpawnPlayerOnNewRound failed for client {client.ClientId}. Auto-spawn retry will continue.");
            }
        }

        startingNewRound = false;
        Debug.Log("[Server] Round started. Players are now alive.");
    }

    public void PlayerDied(ulong victimId, ulong killerId)
    {
        if (!IsServer) return;

        // Remove victim from alive list
        alivePlayers.Remove(victimId);
        clientIdToPlayer.Remove(victimId);

        Debug.Log($"[Server] Player {victimId} died. Killer: {killerId}");

        // Check how many are still alive
        if (alivePlayers.Count <= 1 && startingNewRound == false)
        {
            startingNewRound = true;
            StartCoroutine(RoundEndRoutine());
        }
    }

    private IEnumerator RoundEndRoutine()
    {
        Debug.Log("[Server] RoundEndRoutine waiting 5 seconds...");
        yield return new WaitForSeconds(5f);

        // Award a point only if exactly one player is still alive when the round ends.
        if (alivePlayers.Count == 1)
        {
            ulong winnerId = alivePlayers.First();
            Debug.Log($"[Server] Round has ended. Winner: {winnerId}");

            if (!playerScores.ContainsKey(winnerId))
            {
                playerScores[winnerId] = 0;
            }

            playerScores[winnerId]++;
            UpdatePlayerScoreClientRpc(winnerId, playerScores[winnerId]);
        }

        StartNewRound();
    }

    private void DespawnAllPlayersForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        foreach (var kvp in clientIdToPlayer)
        {
            GameObject player = kvp.Value;
            if (player == null)
            {
                continue;
            }

            NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();
            if (playerNetworkObject != null && playerNetworkObject.IsSpawned)
            {
                playerNetworkObject.Despawn(true);
            }
        }

        clientIdToPlayer.Clear();
        alivePlayers.Clear();
    }

}
