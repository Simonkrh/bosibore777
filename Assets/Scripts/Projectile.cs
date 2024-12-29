using Unity.Netcode;
using UnityEngine;

public class Projectile : NetworkBehaviour
{
    public float lifetime = 10f;
    private Rigidbody2D rb;
    private bool hasCollided = false;
    private ulong shooterId;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0; // 2D top-down, no gravity

        if (IsServer)
        {
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }
    }
    private void Start()
    {
        if (IsServer)
        {
            // Only the server schedules the destruction of the projectile
            DestroyProjectileAfterLifetime();
        }
    }

    private void DestroyProjectileAfterLifetime()
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
    
    public void SetShooterId(ulong id)
    {
        shooterId = id;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsServer) return; 

        if (collision.gameObject.CompareTag("Player"))
        {
            var playerController = collision.gameObject.GetComponent<PlayerController>();

            // Check if the player hit is the shooter and if the bullet has already collided
            if (playerController != null && (hasCollided || playerController.OwnerClientId != shooterId))
            {
                ulong killerId = shooterId;
                playerController.Die(killerId);

                // Despawn the bullet after hitting a player
                if (NetworkObject != null)
                {
                    NetworkObject.Despawn(true);
                }
            }
        }
        hasCollided = true;
    }
}
