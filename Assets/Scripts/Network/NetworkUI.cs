using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    public void StartHost()
    {
        CustomNetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        CustomNetworkManager.Singleton.StartClient();
    }
}
