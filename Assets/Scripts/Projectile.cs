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
            NetworkObject.Despawn(true);
        }
    }

     private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsServer) return; 

        if (collision.gameObject.CompareTag("Player"))
        {
            var playerController = collision.gameObject.GetComponent<PlayerController>();
            if (playerController != null)
            {
                ulong killerId = this.OwnerClientId;
                playerController.Die(killerId);
            }

            // Despawn the bullet after hitting a player
            if (NetworkObject != null)
            {
                NetworkObject.Despawn(true);
            }
        }
    }
}
