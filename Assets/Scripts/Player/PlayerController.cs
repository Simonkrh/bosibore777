using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    private ulong lastAttackerId = 0;
    [Header("Debug")]
    [Tooltip("When enabled, this player cannot die. Server-authoritative.")]
    [SerializeField] private bool godMode;

    public bool GodMode => godMode;

    public void SetGodModeServer(bool enabled)
    {
        if (!IsServer)
        {
            return;
        }

        godMode = enabled;
    }

    public void Die(ulong killerId)
    {
        if (!IsServer || !IsSpawned) return;  // Only the server kills spawned players
        if (godMode) return;

        // Grab the GameManager from the scene
        var GameManager = FindFirstObjectByType<GameManager>();
        if (GameManager != null)
        {
            // Notify it that "this player" died, with the given killer
            GameManager.PlayerDied(OwnerClientId, killerId);
        }

        // Despawn the player object
        NetworkObject.Despawn(true);
    }
}
