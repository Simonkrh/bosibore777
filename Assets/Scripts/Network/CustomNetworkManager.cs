using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System;

public class CustomNetworkManager : NetworkManager
{
    [SerializeField] private bool autoStartDedicatedServerInBatchMode = true;
    [SerializeField] private string gameplaySceneName = "GameScene";
    [SerializeField] private int defaultPort = 7777;
    [SerializeField] private bool matchNetworkTickRateToFixedTimestep = true;

    private bool serverEventsSubscribed;
    private readonly HashSet<ulong> pendingPlayerSpawns = new HashSet<ulong>();
    private float nextSpawnRetryTime;
    private const float SpawnRetryIntervalSeconds = 0.25f;

    private void ApplyRuntimeTickRateConfiguration()
    {
        if (!matchNetworkTickRateToFixedTimestep || NetworkConfig == null || Time.fixedDeltaTime <= 0f)
        {
            return;
        }

        uint physicsTickRate = (uint)Mathf.Clamp(Mathf.RoundToInt(1f / Time.fixedDeltaTime), 30, 120);
        if (NetworkConfig.TickRate == physicsTickRate)
        {
            return;
        }

        NetworkConfig.TickRate = physicsTickRate;
        Debug.Log($"[CustomNetworkManager] Network tick rate set to {physicsTickRate} to match fixed timestep.");
    }

    private void Start()
    {
        if (Singleton != null && Singleton != this)
        {
            Destroy(gameObject);
            return;
        }

        ApplyRuntimeTickRateConfiguration();
        Debug.Log("[CustomNetworkManager] Singleton is initialized by the NetworkManager base class.");

        OnServerStarted += HandleServerStarted;
        OnServerStopped += HandleServerStopped;

        if (Application.isBatchMode &&
            autoStartDedicatedServerInBatchMode &&
            !IsServer &&
            !IsClient)
        {
            StartDedicatedServer();
            return;
        }

        // If this object is already running as server (e.g. domain reload/restart), subscribe immediately.
        if (IsServer)
        {
            SubscribeServerEvents();
        }
    }

    private void OnDisable()
    {
        OnServerStarted -= HandleServerStarted;
        OnServerStopped -= HandleServerStopped;
        UnsubscribeServerEvents();
    }

    private void StartDedicatedServer()
    {
        ushort port = NetworkRuntimeConfig.ReadPort(defaultPort);
        string listenAddress = NetworkRuntimeConfig.ReadListenAddress(NetworkRuntimeConfig.ListenOnAllInterfaces);

        if (!NetworkRuntimeConfig.TryConfigureDedicatedServer(this, port, listenAddress))
        {
            Debug.LogError("[CustomNetworkManager] Dedicated server startup aborted: transport configuration failed.");
            return;
        }

        Debug.Log($"[CustomNetworkManager] Batch mode detected. Starting dedicated server on {listenAddress}:{port}.");
        if (!StartServer())
        {
            Debug.LogError("[CustomNetworkManager] Failed to start dedicated server.");
            return;
        }

        if (this.SceneManager == null)
        {
            Debug.LogError("[CustomNetworkManager] SceneManager is null; cannot load gameplay scene.");
            return;
        }

        if (string.IsNullOrWhiteSpace(gameplaySceneName))
        {
            Debug.LogError("[CustomNetworkManager] gameplaySceneName is empty.");
            return;
        }

        this.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Single);
    }

    private void HandleServerStarted()
    {
        pendingPlayerSpawns.Clear();
        nextSpawnRetryTime = 0f;
        SubscribeServerEvents();
    }

    private void HandleServerStopped(bool _)
    {
        pendingPlayerSpawns.Clear();
        nextSpawnRetryTime = 0f;
    }

    private void Update()
    {
        if (!IsServer || pendingPlayerSpawns.Count == 0)
        {
            return;
        }

        if (Time.unscaledTime < nextSpawnRetryTime)
        {
            return;
        }

        nextSpawnRetryTime = Time.unscaledTime + SpawnRetryIntervalSeconds;

        var pendingIds = new List<ulong>(pendingPlayerSpawns);
        for (int i = 0; i < pendingIds.Count; i++)
        {
            TrySpawnPlayer(pendingIds[i], "RetryLoop");
        }
    }

    private void SubscribeServerEvents()
    {
        if (serverEventsSubscribed || Singleton == null)
        {
            return;
        }

        Singleton.OnClientConnectedCallback += OnClientConnected;
        Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        if (Singleton.SceneManager != null)
        {
            Singleton.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
            Singleton.SceneManager.OnSynchronizeComplete += OnSceneSynchronizeComplete;
        }

        serverEventsSubscribed = true;
    }

    private void UnsubscribeServerEvents()
    {
        if (!serverEventsSubscribed)
        {
            return;
        }

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

        serverEventsSubscribed = false;
    }
    
    private void OnClientConnected(ulong clientId)
    {
        pendingPlayerSpawns.Add(clientId);
        FindFirstObjectByType<GameManager>()?.HandleClientConnectedServer(clientId);
        TrySpawnPlayer(clientId, "OnClientConnected");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[Server] OnClientDisconnected: client {clientId}");
        pendingPlayerSpawns.Remove(clientId);

        var gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogWarning($"[Server] GameManager not found while handling disconnect for client {clientId}.");
            return;
        }

        gameManager.RemovePlayerOnDisconnect(clientId);
    }

    private void OnSceneLoadCompleted(
        string sceneName,
        LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        Debug.Log($"[Netcode] Scene load completed for scene: {sceneName}");

        if (!string.Equals(sceneName, gameplaySceneName, StringComparison.Ordinal))
        {
            return;
        }

        foreach (ulong clientId in clientsCompleted)
        {
            pendingPlayerSpawns.Add(clientId);
            FindFirstObjectByType<GameManager>()?.HandleClientConnectedServer(clientId);
            TrySpawnPlayer(clientId, "OnSceneLoadCompleted");
        }
    }

    private void OnSceneSynchronizeComplete(ulong clientId)
    {
        Debug.Log($"[Netcode] Scene synchronization completed for client {clientId}");
        pendingPlayerSpawns.Add(clientId);

        var mazeGenerator = FindFirstObjectByType<MazeGenerator>();
        if (mazeGenerator != null)
        {
            mazeGenerator.SendMazeDataToClient(clientId);
        }

        FindFirstObjectByType<GameManager>()?.HandleClientConnectedServer(clientId);
        TrySpawnPlayer(clientId, "OnSceneSynchronizeComplete");
    }

    private void TrySpawnPlayer(ulong clientId, string source)
    {
        if (!IsServer || !pendingPlayerSpawns.Contains(clientId))
        {
            return;
        }

        if (!ConnectedClients.ContainsKey(clientId))
        {
            Debug.Log($"[Server] Delaying spawn for client {clientId} from {source}: client not in ConnectedClients yet.");
            return;
        }

        var gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.Log($"[Server] Delaying spawn for client {clientId} from {source}: GameManager not ready.");
            return;
        }

        if (gameManager.HasSpawnForClient(clientId))
        {
            pendingPlayerSpawns.Remove(clientId);
            return;
        }

        if (!gameManager.IsGameplaySessionActive)
        {
            return;
        }

        if (!gameManager.IsGameplayParticipant(clientId))
        {
            return;
        }

        if (!gameManager.CanSpawnPlayerNow())
        {
            Debug.Log($"[Server] Delaying spawn for client {clientId} from {source}: {gameManager.GetSpawnReadinessReason()}.");
            return;
        }

        if (gameManager.SpawnPlayerOnConnect(clientId))
        {
            pendingPlayerSpawns.Remove(clientId);
        }
    }
}
