using UnityEngine;
using Unity.Netcode;

public class DedicatedServerAutoStart : MonoBehaviour
{
    void Start()
    {
        if (Application.isBatchMode)
        {
            Debug.Log("[DedicatedServerAutoStart] Batch mode detected. Starting server...");

            if (CustomNetworkManager.Singleton == null)
            {
                Debug.LogError("[DedicatedServerAutoStart] CustomNetworkManager instance is null!");
                return;
            }

            StartServer();
        }
    }
    private void StartServer()
    {
        Debug.Log("[Server] Server starts now...");
        CustomNetworkManager.Singleton.StartServer();
        Debug.Log("[Server] Loading GameScene...");
        CustomNetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }
}
