using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "GameScene";
    [SerializeField] private string defaultServerAddress = NetworkRuntimeConfig.DefaultLoopbackAddress;
    [SerializeField] private int defaultPort = 7777;

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

        if (!NetworkRuntimeConfig.TryConfigureClient(manager, normalizedAddress, port))
        {
            Debug.LogError("[NetworkUI] Failed to configure client transport.");
            return false;
        }

        Debug.Log($"[NetworkUI] Starting client to {normalizedAddress}:{port}.");
        if (!manager.StartClient())
        {
            Debug.LogError("[NetworkUI] Failed to start client.");
            return false;
        }

        return true;
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
