using Unity.Netcode;
using UnityEngine;

public class Projectile : NetworkBehaviour
{
    public float lifetime = 10f;

    private void Start()
    {
        if (IsServer)
        {
            // Only the server schedules the destruction of the projectile
            DestroyProjectileAfterLifetimeServerRpc();
        }
    }

    [ServerRpc]
    private void DestroyProjectileAfterLifetimeServerRpc()
    {
        StartCoroutine(DestroyAfterLifetime());
    }

    private System.Collections.IEnumerator DestroyAfterLifetime()
    {
        yield return new WaitForSeconds(lifetime);
        if (IsServer && NetworkObject != null)
        {
            NetworkObject.Despawn(true); // Despawn ensures proper destruction across the network
        }
    }

     private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsServer) return; // Only handle collision logic on the server

        if (collision.gameObject.CompareTag("Player"))
        {
            var playerController = collision.gameObject.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.Die(); // Handle the player's death
            }

            // Despawn the bullet after hitting a player
            if (NetworkObject != null)
            {
                NetworkObject.Despawn(true);
            }
        }
    }
}
