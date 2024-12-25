using UnityEngine;
using Unity.Netcode;

public class DedicatedServerAutoStart : MonoBehaviour
{
    void Start()
    {
        // Check if we are in batch mode or headless mode
        // (Means there is no graphics device or user interface)
        if (Application.isBatchMode) 
        {
            Debug.Log("[DedicatedServerAutoStart] Batch mode detected. Starting server...");
            CustomNetworkManager.Singleton.StartServer();
        }
        else
        {
            Debug.Log("[DedicatedServerAutoStart] Not batch mode, do nothing");
        }
    }
}
