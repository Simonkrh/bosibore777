using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

public class GameManager : NetworkBehaviour
{
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
    
    private void Awake()
    {
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

    public void SpawnPlayerOnConnect(ulong clientId)
    {
        Color playerColor = AssignColor(clientId);
        InitializePlayerDisplayAndColors(clientId);

        spawnPlayer(clientId);

        AssignIconColorClientRpc(clientId, playerColor);
        AssignTankColor(clientId, playerColor);
    }

    public void SpawnPlayerOnNewRound(ulong clientId)
    {
        Color existingColor = playerColors[clientId];
        
        spawnPlayer(clientId);
        
        AssignIconColorClientRpc(clientId, existingColor);
        AssignTankColor(clientId, existingColor);
    }

    private void spawnPlayer(ulong clientId) {
        if (availableCells == null || availableCells.Count == 0)
        {
            Debug.LogWarning("No available cells for spawning players.");
            return;
        }

        // Select a random cell or the next available cell
        Vector2Int cell = availableCells[UnityEngine.Random.Range(0, availableCells.Count)];
        availableCells.Remove(cell); // Ensure no duplicates

        Vector3 spawnPosition = mazeGenerator.CellToWorldPosition(cell);

        GameObject player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
        player.GetComponent<NetworkObject>().SpawnWithOwnership(clientId);

        alivePlayers.Add(clientId);
        clientIdToPlayer[clientId] = player;

        Debug.Log($"[Server] Spawned player {clientId} at cell {cell} (world position {spawnPosition})");

    }

    [ClientRpc]
    private void AssignIconColorClientRpc(ulong clientId, Color color, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return; 

        if (PlayerDisplayManager.Instance != null)
        {
            PlayerDisplayManager.Instance.SetIconColor(clientId, color);
        }
        else
        {
            Debug.LogError("PlayerDisplayManager instance not found on client.");
        }
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

        if (PlayerDisplayManager.Instance != null)
        {
            for (int i = 0; i < clientIds.Length; i++)
            {
                PlayerDisplayManager.Instance.CreatePlayerDisplay(clientIds[i], scores[i]);
            }
        }
        else
        {
            Debug.LogError("PlayerDisplayManager instance not found on client.");
        }
    }

    [ClientRpc]
    private void SendExistingPlayerIconColorsClientRpc(ulong[] clientIds, Color[] colors, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        if (PlayerDisplayManager.Instance != null)
        {
            for (int i = 0; i < clientIds.Length; i++)
            {
                PlayerDisplayManager.Instance.SetIconColor(clientIds[i], colors[i]);
            }
        }
        else
        {
            Debug.LogError("PlayerDisplayManager instance not found on client.");
        }
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
        // Debug.Log($"[GameManager] Received available cells: {availableCells.Count}");
    }

    [ClientRpc]
    private void CreatePlayerDisplayClientRpc(ulong clientId, int initialScore)
    {
        if (!IsClient) return; 

        if (PlayerDisplayManager.Instance != null)
        {
            PlayerDisplayManager.Instance.CreatePlayerDisplay(clientId, initialScore);
        }
        else
        {
            Debug.LogError("PlayerDisplayManager instance not found on client.");
        }
    }

    [ClientRpc]
    private void UpdatePlayerScoreClientRpc(ulong clientId, int newScore)
    {
        if (!IsClient) return; // Server doesn't need to handle client-side UI

        if (PlayerDisplayManager.Instance != null)
        {
            PlayerDisplayManager.Instance.UpdatePlayerScore(clientId, newScore);
        }
        else
        {
            Debug.LogError("PlayerDisplayManager instance not found on client.");
        }
    }

    [ClientRpc]
    public void RemovePlayerDisplayClientRpc(ulong clientId)
    {
        if (!IsClient) return; 

        if (PlayerDisplayManager.Instance != null)
        {
            PlayerDisplayManager.Instance.RemovePlayerDisplay(clientId);
        }
        else
        {
            Debug.LogError("PlayerDisplayManager instance not found on client.");
        }
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

        // Remove the last player standing if any
        if (alivePlayers.Count == 1)
        {
            ulong lastPlayerId = alivePlayers.First();
            DespawnPlayer(lastPlayerId);
            RemovePlayerOnNewRound(lastPlayerId);
        
            Debug.Log($"[Server] Removed last player standing: {lastPlayerId}");
        }

        alivePlayers.Clear();

        DespawnAllProjectiles();

        // Regenerate and sync the maze
        mazeGenerator.RegenerateMaze();

        // Wait for the maze to sync
        yield return new WaitForSeconds(1f); // Adjust based on synchronization speed

        // Spawn players after the maze has been regenerated and synced
        foreach (var client in CustomNetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayerOnNewRound(client.ClientId);
        }

        startingNewRound = false;
        Debug.Log("[Server] Round started. Players are now alive.");
    }

    public void PlayerDied(ulong victimId, ulong killerId)
    {
        if (!IsServer) return;

        // Remove victim from alive list
        alivePlayers.Remove(victimId);

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

        // The round ends. Let's find the "winner" (or none if 0 alive)
        ulong winnerId = alivePlayers.Count == 1 ? alivePlayers.First() : 0;
        if (winnerId != 0) {
            Debug.Log($"[Server] Round has ended. Winner: {winnerId}");
        
            playerScores[winnerId]++;
            UpdatePlayerScoreClientRpc(winnerId, playerScores[winnerId]);
        }

        StartNewRound();
    }

}
