using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    private ulong lastAttackerId = 0;

    public void Die(ulong killerId)
    {
        if (!IsServer) return;  // Only the server kills players

        // Grab the GameManager from the scene
        var GameManager = FindObjectOfType<GameManager>();
        if (GameManager != null)
        {
            // Notify it that "this player" died, with the given killer
            GameManager.PlayerDied(OwnerClientId, killerId);
        }

        // Despawn the player object
        NetworkObject.Despawn(true);
    }
}
