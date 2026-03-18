using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    private ulong lastAttackerId = 0;
    private GameManager gameManager;
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

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        if (gameManager != null)
        {
            gameManager.PlayPlayerDieSoundServer(transform.position);
            gameManager.PlayerDied(OwnerClientId, killerId);
        }

        // Despawn the player object
        NetworkObject.Despawn(true);
    }
}
