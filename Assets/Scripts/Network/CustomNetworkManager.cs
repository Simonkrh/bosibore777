using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class CustomNetworkManager : NetworkManager
{
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
        var gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            gameManager.SpawnPlayer(clientId);
            gameManager.InitializePlayerDisplay(clientId);
        }
        else
        {
            Debug.LogError("GameManager not found. Cannot spawn player.");
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
            gameManager.RemovePlayerOnDisconnect(clientId);
            gameManager.RemovePlayerDisplayClientRpc(clientId);

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
