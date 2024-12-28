using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class GameManager : NetworkBehaviour
{
    public GameObject playerPrefab;  
    public MazeGenerator mazeGenerator; 

    // Keep track of who is still alive.
    private HashSet<ulong> alivePlayers = new HashSet<ulong>();

    // Keep track of each player's score 
    private Dictionary<ulong, int> playerScores = new Dictionary<ulong, int>();

    // Mapping from clientId to player GameObject
    private Dictionary<ulong, GameObject> clientIdToPlayer = new Dictionary<ulong, GameObject>();

    public void SpawnPlayer(ulong clientId)
    {
        Debug.Log("Client availableCells: " + mazeGenerator.availableCells.Count);
        if (mazeGenerator.availableCells == null || mazeGenerator.availableCells.Count == 0)
        {
            Debug.LogWarning("No available cells for spawning players.");
            return;
        }

        // Select a random cell or the next available cell
        Vector2Int cell = mazeGenerator.availableCells[Random.Range(0, mazeGenerator.availableCells.Count)];
        mazeGenerator.availableCells.Remove(cell); // Ensure no duplicates

        Vector3 spawnPosition = mazeGenerator.CellToWorldPosition(cell);

        GameObject player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
        player.GetComponent<NetworkObject>().SpawnWithOwnership(clientId);

        alivePlayers.Add(clientId);
        clientIdToPlayer[clientId] = player;

        Debug.Log($"[Server] Spawned player {clientId} at cell {cell} (world position {spawnPosition})");
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
            clientIdToPlayer.Remove(clientId);
        }
        else
        {
            Debug.LogWarning($"[Server] Attempted to despawn player {clientId}, but no such player was found.");
        }
    }

    public void SetAvailableCells(List<Vector2Int> cells)
    {
        mazeGenerator.availableCells = cells;
        Debug.Log($"[GameManager] Received available cells: {cells.Count}");
    }

    public void RemovePlayer(ulong clientId)
    {
        if (!IsServer) return;

        if (alivePlayers.Contains(clientId))
        {
            alivePlayers.Remove(clientId);
            Debug.Log($"[Server] Player {clientId} removed from alivePlayers.");
        }
    }

    private void StartNewRound()
    {
        if (!IsServer) return;

        Debug.Log("[Server] Starting new round...");

        // Remove the last player standing if any
        if (alivePlayers.Count == 1)
        {
            ulong lastPlayerId = alivePlayers.First();
            RemovePlayer(lastPlayerId);
            DespawnPlayer(lastPlayerId);
            Debug.Log($"[Server] Removed last player standing: {lastPlayerId}");
        }

        alivePlayers.Clear();

        // Regenerate and sync the maze
        mazeGenerator.RegenerateMaze();

        // Spawn players after the maze has been regenerated and synced
        foreach (var client in CustomNetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayer(client.ClientId);
        }

        Debug.Log("[Server] Round started. Players are now alive.");
    }

    public void PlayerDied(ulong victimId, ulong killerId)
    {
        if (!IsServer) return;

        // Remove victim from alive list
        alivePlayers.Remove(victimId);

        Debug.Log($"[Server] Player {victimId} died. Killer: {killerId}");

        // If killer != victim, give them a point
        if (killerId != victimId)
        {
            if (!playerScores.ContainsKey(killerId))
                playerScores[killerId] = 0;

            playerScores[killerId]++;
            Debug.Log($"[Server] Player {killerId} earned a point! Score: {playerScores[killerId]}");
        }

        // Check how many are still alive
        if (alivePlayers.Count <= 1)
        {
            // The round ends. Let's find the "winner" (or none if 0 alive)
            ulong winnerId = alivePlayers.Count == 1 ? alivePlayers.First() : 0;

            Debug.Log($"[Server] Round has ended. Winner: {winnerId}");
            EndRound(winnerId);
        }
    }

    private void EndRound(ulong winnerId)
    {
        Debug.Log("[Server] EndRound called. Winner: " + winnerId);
        AnnounceWinnerClientRpc(winnerId);

        StartCoroutine(RoundEndRoutine());

    }
    private IEnumerator RoundEndRoutine()
    {
        Debug.Log("[Server] RoundEndRoutine waiting 5 seconds...");
        yield return new WaitForSeconds(5f);

        Debug.Log("[Server] Restarting round now!");
        StartNewRound();
    }

    [ClientRpc]
    private void AnnounceWinnerClientRpc(ulong winnerId)
    {
        if (winnerId == 0)
        {
            Debug.Log("[Client] No players survived this round. It's a tie!");
        }
        else
        {
            Debug.Log($"[Client] Player {winnerId} is the winner of this round!");
        }
    }
}
