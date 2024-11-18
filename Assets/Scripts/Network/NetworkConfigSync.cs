using Unity.Netcode;
using UnityEngine;

public class NetworkConfigSync : MonoBehaviour
{
    void Awake()
    {
        var networkManager = NetworkManager.Singleton;

        if (networkManager != null)
        {
            // Set explicit matching configurations
            networkManager.NetworkConfig.ConnectionApproval = false;
            networkManager.NetworkConfig.EnableSceneManagement = true;
            // Add any other NetworkConfig settings that need to match
        }
    }
}
