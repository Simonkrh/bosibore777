using Unity.Netcode;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    private NetworkManagerData managerData;

    private void Start()
    {
        // Find and cache the NetworkManagerData component
        managerData = FindObjectOfType<NetworkManagerData>();

        if (managerData == null)
        {
            Debug.LogError("NetworkManagerData is not found in the scene!");
            return;
        }

        // Use the Singleton instance to access the callbacks
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnDestroy()
    {
        // Unsubscribe from the callbacks to avoid memory leaks
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (managerData.playerPrefab == null)
        {
            Debug.LogError("Player prefab is not assigned in NetworkManagerData!");
            return;
        }

        if (IsServer)
        {
            // Get a spawn point
            Transform spawnPoint = managerData.spawnPoints[(int)(clientId % (ulong)managerData.spawnPoints.Length)];
            if (spawnPoint == null)
            {
                Debug.LogError("No spawn points are available in NetworkManagerData!");
                return;
            }

            // Instantiate and spawn the player
            GameObject playerObject = Instantiate(managerData.playerPrefab, spawnPoint.position, spawnPoint.rotation);
            NetworkObject networkObject = playerObject.GetComponent<NetworkObject>();

            if (networkObject != null)
            {
                // Assign ownership to the connected client
                networkObject.SpawnWithOwnership(clientId);
                Debug.Log($"Player {clientId} spawned at {spawnPoint.position}");
            }
            else
            {
                Debug.LogError("Player prefab does not have a NetworkObject component!");
            }
        }
    }


    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected");

        // Find and destroy the player's object if it exists
        foreach (var obj in NetworkManager.Singleton.SpawnManager.SpawnedObjects.Values)
        {
            if (obj.OwnerClientId == clientId)
            {
                Destroy(obj.gameObject);
                break;
            }
        }
    }
}
