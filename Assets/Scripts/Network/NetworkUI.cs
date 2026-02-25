using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "GameScene";
    [SerializeField] private string defaultServerAddress = NetworkRuntimeConfig.DefaultLoopbackAddress;
    [SerializeField] private int defaultPort = 7777;

    public void StartClient()
    {
        CustomNetworkManager manager = EnsureNetworkManager();
        if (manager == null)
        {
            Debug.LogError("[NetworkUI] CustomNetworkManager.Singleton is null.");
            return;
        }

        string serverAddress = NetworkRuntimeConfig.ReadAddress(defaultServerAddress);
        ushort port = NetworkRuntimeConfig.ReadPort(defaultPort);
        if (!NetworkRuntimeConfig.TryConfigureClient(manager, serverAddress, port))
        {
            Debug.LogError("[NetworkUI] Failed to configure client transport.");
            return;
        }

        Debug.Log($"[NetworkUI] Starting client to {serverAddress}:{port}.");
        if (!manager.StartClient())
        {
            Debug.LogError("[NetworkUI] Failed to start client.");
        }
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
