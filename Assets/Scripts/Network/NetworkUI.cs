using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkUI : MonoBehaviour
{
    public void StartClient()
    {
        CustomNetworkManager.Singleton.StartClient();
    }
}
