using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    public void Die()
    {
        if (IsServer)
        {
            HandleDie();
        }
        else
        {
            RequestDieServerRpc();
        }
    }

    private void HandleDie()
    {
        if (NetworkObject != null)
        {
            NetworkObject.Despawn(true); // Despawn the object for all clients
        }
        else
        {
            Destroy(gameObject); // Fallback for non-networked scenarios
        }
    }

    [ServerRpc]
    private void RequestDieServerRpc(ServerRpcParams rpcParams = default)
    {
        HandleDie();
    }
}
