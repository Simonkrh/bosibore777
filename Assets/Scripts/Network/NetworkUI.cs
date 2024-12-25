using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    public void StartServer()
    {
        CustomNetworkManager.Singleton.StartServer();
    }
    public void StartClient()
    {
        CustomNetworkManager.Singleton.StartClient();
    }
}
