using UnityEngine;
using Unity.Netcode;

public class DedicatedServerAutoStart : MonoBehaviour
{
    void Start()
    {
        if (Application.isBatchMode) 
        {
            Debug.Log("[DedicatedServerAutoStart] Batch mode detected. Starting server...");
            CustomNetworkManager.Singleton.StartServer();
        }
    
    }
}
