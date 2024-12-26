using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class CustomNetworkManager : NetworkManager
{
    private NetworkManagerData managerData;

    private void Awake()
    {
        if (Singleton != null && Singleton != this)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        Debug.Log("[CustomNetworkManager] Singleton is initialized by the NetworkManager base class.");
    }

    private void Start()
    {
        // By Start(), NetworkManager has already initialized Singleton
        // So we can safely subscribe to events here:

        if (Singleton == this && Singleton.IsServer)
        {
            Singleton.OnClientConnectedCallback += OnClientConnected;
            Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            if (Singleton.SceneManager != null)
            {
                Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
                Singleton.SceneManager.OnSynchronizeComplete += OnSceneSynchronizeComplete;
            }
        }

        // Set your TickRate
        NetworkConfig.TickRate = 128;

        // Find and cache the NetworkManagerData component
        managerData = FindFirstObjectByType<NetworkManagerData>();
        if (managerData == null)
        {
            Debug.LogError("NetworkManagerData is not found in the scene!");
        }
    }   

    private void OnDisable()
    {
        // Unsubscribe if Singleton is still valid
        if (Singleton != null)
        {
            Singleton.OnClientConnectedCallback -= OnClientConnected;
            Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

            if (Singleton.SceneManager != null)
            {
                Singleton.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
                Singleton.SceneManager.OnSynchronizeComplete -= OnSceneSynchronizeComplete;
            }
        }
    }
    
    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[Server] OnClientConnected: client {clientId}");
        
        if (managerData == null || managerData.playerPrefab == null)
        {
            Debug.LogError("Player prefab or managerData is not assigned in NetworkManagerData!");
            return;
        }

        // Only the server spawns player objects (dedicated or host)
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
                Destroy(playerObject); // Clean up if the object is invalid
            }
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[Server] OnClientDisconnected: client {clientId}");

        if (Singleton == null || Singleton.SpawnManager == null)
        {
            Debug.LogWarning("SpawnManager is not available during client disconnection.");
            return;
        }

        // Find and destroy the player's object if it exists
        foreach (var obj in Singleton.SpawnManager.SpawnedObjects.Values)
        {
            if (obj != null && obj.OwnerClientId == clientId)
            {
                var playerController = obj.GetComponent<PlayerController>();
                if (playerController != null)
                {
                    playerController.Die(clientId); 
                    Debug.Log($"[Server] Player {clientId} killed on disconnection.");
                }
            }
        }

        var gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            gameManager.RemovePlayer(clientId);
        }
    }

    private void OnSceneLoadCompleted(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        Debug.Log($"[Netcode] Scene load completed for scene: {sceneName}");
    }

    private void OnSceneSynchronizeComplete(ulong clientId)
    {
        Debug.Log($"[Netcode] Scene synchronization completed for client {clientId}");
    }
}
