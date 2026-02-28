using System.Collections;
using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "GameScene";
    [SerializeField] private string defaultServerAddress = NetworkRuntimeConfig.DefaultLoopbackAddress;
    [SerializeField] private int defaultPort = 7777;
    [SerializeField] private float clientStartResetTimeoutSeconds = 4f;
    [SerializeField] private float clientConnectWatchdogSeconds = 6f;
    [SerializeField] private int clientConnectTimeoutMs = 1000;
    [SerializeField] private int clientMaxConnectAttempts = 3;

    private CustomNetworkManager startClientRoutineHost;
    private Coroutine startClientRoutine;
    private Coroutine connectWatchdogRoutine;
    private CustomNetworkManager connectWatchdogHost;
    private bool clientStartInProgress;
    private string currentJoinEndpoint = string.Empty;
    private bool currentAttemptConnected;
    private CustomNetworkManager currentClientEventManager;

    public event Action<string> ClientJoinStatusChanged;

    public void StartClient()
    {
        string serverAddress = NetworkRuntimeConfig.ReadAddress(defaultServerAddress);
        ushort port = NetworkRuntimeConfig.ReadPort(defaultPort);
        StartClientTo(serverAddress, port);
    }

    public bool StartClientTo(string serverAddress, int port)
    {
        if (port < 1 || port > 65535)
        {
            Debug.LogError($"[NetworkUI] Invalid port: {port}.");
            return false;
        }

        return StartClientTo(serverAddress, (ushort)port);
    }

    public bool StartClientTo(string serverAddress, ushort port)
    {
        CustomNetworkManager manager = EnsureNetworkManager();
        if (manager == null)
        {
            Debug.LogError("[NetworkUI] CustomNetworkManager.Singleton is null.");
            return false;
        }

        string normalizedAddress = string.IsNullOrWhiteSpace(serverAddress)
            ? defaultServerAddress
            : serverAddress.Trim();

        if (clientStartInProgress)
        {
            Debug.LogWarning("[NetworkUI] Client start is already in progress.");
            return false;
        }

        currentJoinEndpoint = $"{normalizedAddress}:{port}";
        currentAttemptConnected = false;
        NotifyClientJoinStatus($"Joining {currentJoinEndpoint}...");

        startClientRoutineHost = manager;
        startClientRoutine = manager.StartCoroutine(StartClientRoutine(normalizedAddress, port));
        return true;
    }

    private IEnumerator StartClientRoutine(string normalizedAddress, ushort port)
    {
        clientStartInProgress = true;

        CustomNetworkManager manager = EnsureNetworkManager();
        if (manager == null)
        {
            Debug.LogError("[NetworkUI] CustomNetworkManager.Singleton is null.");
            NotifyClientJoinStatus($"Failed to join {currentJoinEndpoint}: network manager not found.");
            clientStartInProgress = false;
            startClientRoutineHost = null;
            startClientRoutine = null;
            yield break;
        }

        if (manager.IsServer || manager.IsClient || manager.IsListening || manager.ShutdownInProgress)
        {
            manager.Shutdown(discardMessageQueue: true);

            float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(0.5f, clientStartResetTimeoutSeconds);
            while (manager != null && (manager.ShutdownInProgress || manager.IsListening))
            {
                if (Time.realtimeSinceStartup >= timeoutAt)
                {
                    Debug.LogWarning("[NetworkUI] Timed out waiting for previous network state to stop before client start.");
                    break;
                }

                yield return null;
            }
        }

        manager = EnsureNetworkManager();
        if (manager == null)
        {
            Debug.LogError("[NetworkUI] Network manager is unavailable after reset.");
            NotifyClientJoinStatus($"Failed to join {currentJoinEndpoint}: network manager unavailable after reset.");
            clientStartInProgress = false;
            startClientRoutineHost = null;
            startClientRoutine = null;
            yield break;
        }

        if (!NetworkRuntimeConfig.TryConfigureClient(manager, normalizedAddress, port))
        {
            Debug.LogError("[NetworkUI] Failed to configure client transport.");
            NotifyClientJoinStatus($"Failed to join {currentJoinEndpoint}: transport configuration failed.");
            clientStartInProgress = false;
            startClientRoutineHost = null;
            startClientRoutine = null;
            yield break;
        }

        ApplyClientConnectRetrySettings(manager);
        SubscribeClientEvents(manager);

        Debug.Log($"[NetworkUI] Starting client to {normalizedAddress}:{port}.");
        if (!manager.StartClient())
        {
            Debug.LogError("[NetworkUI] Failed to start client.");
            NotifyClientJoinStatus($"Failed to join {currentJoinEndpoint}: start client failed.");
            clientStartInProgress = false;
            startClientRoutineHost = null;
            startClientRoutine = null;
            yield break;
        }

        StartConnectWatchdog(manager);
        clientStartInProgress = false;
        startClientRoutineHost = null;
        startClientRoutine = null;
    }

    private void HandleTransportFailure()
    {
        CustomNetworkManager manager = CustomNetworkManager.Singleton as CustomNetworkManager;
        if (manager == null)
        {
            return;
        }

        Debug.LogError("[NetworkUI] Transport failure detected. Shutting down to allow a clean reconnect.");
        NotifyClientJoinStatus($"Failed to join {currentJoinEndpoint}: transport failure.");
        StopConnectWatchdog();

        if (!manager.ShutdownInProgress)
        {
            manager.Shutdown(discardMessageQueue: true);
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        CustomNetworkManager manager = currentClientEventManager != null
            ? currentClientEventManager
            : CustomNetworkManager.Singleton as CustomNetworkManager;
        if (manager == null || clientId != manager.LocalClientId)
        {
            return;
        }

        currentAttemptConnected = true;
        StopConnectWatchdog();
        NotifyClientJoinStatus($"Joined {currentJoinEndpoint}");
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        CustomNetworkManager manager = currentClientEventManager != null
            ? currentClientEventManager
            : CustomNetworkManager.Singleton as CustomNetworkManager;
        if (manager == null || clientId != manager.LocalClientId)
        {
            return;
        }

        StopConnectWatchdog();

        string disconnectReason = manager.DisconnectReason;
        if (!currentAttemptConnected)
        {
            string reasonSuffix = string.IsNullOrWhiteSpace(disconnectReason) ? string.Empty : $": {disconnectReason}";
            NotifyClientJoinStatus($"Failed to join {currentJoinEndpoint}{reasonSuffix}");
        }
        else
        {
            string reasonSuffix = string.IsNullOrWhiteSpace(disconnectReason) ? string.Empty : $": {disconnectReason}";
            NotifyClientJoinStatus($"Disconnected from {currentJoinEndpoint}{reasonSuffix}");
        }

        if (!manager.ShutdownInProgress)
        {
            manager.Shutdown(discardMessageQueue: true);
        }
    }

    private void StartConnectWatchdog(CustomNetworkManager manager)
    {
        StopConnectWatchdog();
        if (manager == null)
        {
            return;
        }

        connectWatchdogHost = manager;
        connectWatchdogRoutine = manager.StartCoroutine(ClientConnectWatchdogRoutine(manager));
    }

    private void StopConnectWatchdog()
    {
        if (connectWatchdogRoutine == null || connectWatchdogHost == null)
        {
            connectWatchdogHost = null;
            connectWatchdogRoutine = null;
            return;
        }

        connectWatchdogHost.StopCoroutine(connectWatchdogRoutine);
        connectWatchdogHost = null;
        connectWatchdogRoutine = null;
    }

    private IEnumerator ClientConnectWatchdogRoutine(CustomNetworkManager manager)
    {
        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(1f, clientConnectWatchdogSeconds);
        while (manager != null &&
               manager.IsClient &&
               !manager.IsConnectedClient &&
               !manager.ShutdownInProgress)
        {
            if (Time.realtimeSinceStartup >= timeoutAt)
            {
                NotifyClientJoinStatus($"Failed to join {currentJoinEndpoint}: connection timed out.");
                if (!manager.ShutdownInProgress)
                {
                    manager.Shutdown(discardMessageQueue: true);
                }
                break;
            }

            yield return null;
        }

        connectWatchdogRoutine = null;
        connectWatchdogHost = null;
    }

    private void SubscribeClientEvents(CustomNetworkManager manager)
    {
        UnsubscribeClientEvents();
        if (manager == null)
        {
            return;
        }

        manager.OnTransportFailure -= HandleTransportFailure;
        manager.OnTransportFailure += HandleTransportFailure;
        manager.OnClientConnectedCallback -= HandleClientConnected;
        manager.OnClientConnectedCallback += HandleClientConnected;
        manager.OnClientDisconnectCallback -= HandleClientDisconnected;
        manager.OnClientDisconnectCallback += HandleClientDisconnected;
        currentClientEventManager = manager;
    }

    private void UnsubscribeClientEvents()
    {
        if (currentClientEventManager == null)
        {
            return;
        }

        currentClientEventManager.OnTransportFailure -= HandleTransportFailure;
        currentClientEventManager.OnClientConnectedCallback -= HandleClientConnected;
        currentClientEventManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        currentClientEventManager = null;
    }

    private void ApplyClientConnectRetrySettings(CustomNetworkManager manager)
    {
        if (manager == null || manager.NetworkConfig == null)
        {
            return;
        }

        UnityTransport transport = manager.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport == null)
        {
            return;
        }

        transport.ConnectTimeoutMS = Mathf.Clamp(clientConnectTimeoutMs, 100, 60000);
        transport.MaxConnectAttempts = Mathf.Clamp(clientMaxConnectAttempts, 1, 20);
    }

    private void NotifyClientJoinStatus(string message)
    {
        ClientJoinStatusChanged?.Invoke(message);
    }

    private void OnDisable()
    {
        StopConnectWatchdog();
        UnsubscribeClientEvents();

        if (startClientRoutine != null && startClientRoutineHost != null)
        {
            startClientRoutineHost.StopCoroutine(startClientRoutine);
            startClientRoutineHost = null;
            startClientRoutine = null;
        }

        clientStartInProgress = false;
    }

    public void StartHost()
    {
        CustomNetworkManager manager = EnsureNetworkManager();
        if (manager == null)
        {
            Debug.LogError("[NetworkUI] CustomNetworkManager.Singleton is null.");
            return;
        }

        ushort port = NetworkRuntimeConfig.ReadPort(defaultPort);
        if (!NetworkRuntimeConfig.TryConfigureHost(manager, port))
        {
            Debug.LogError("[NetworkUI] Failed to configure host transport.");
            return;
        }

        Debug.Log($"[NetworkUI] Starting host on port {port}.");
        if (manager.StartHost())
        {
            if (manager.SceneManager == null)
            {
                Debug.LogError("[NetworkUI] SceneManager is null; cannot load gameplay scene.");
                return;
            }

            manager.SceneManager.LoadScene(gameplaySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        else
        {
            Debug.LogError("[NetworkUI] Failed to start host.");
        }
    }

    private CustomNetworkManager EnsureNetworkManager()
    {
        if (CustomNetworkManager.Singleton != null)
        {
            return CustomNetworkManager.Singleton as CustomNetworkManager;
        }

        CustomNetworkManager existingManager = FindFirstObjectByType<CustomNetworkManager>();
        if (existingManager != null)
        {
            return existingManager;
        }

        GameObject networkManagerPrefab = Resources.Load<GameObject>("Prefabs/Network/CustomNetworkManager");
        if (networkManagerPrefab == null)
        {
            Debug.LogError("[NetworkUI] Could not load Resources/Prefabs/Network/CustomNetworkManager.");
            return null;
        }

        Instantiate(networkManagerPrefab);

        if (CustomNetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkUI] Spawned CustomNetworkManager prefab, but Singleton is still null.");
            return null;
        }

        return CustomNetworkManager.Singleton as CustomNetworkManager;
    }
}
