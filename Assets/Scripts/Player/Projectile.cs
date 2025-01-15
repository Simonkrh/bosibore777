using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : NetworkBehaviour
{
    public float lifetime = 10f;
    private Rigidbody2D rb;
    private bool hasCollided = false;
    private ulong shooterId;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f; // top-down, no gravity

        if (IsServer)
        {
            // For continuous collision detection with walls
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }
    }

    private void Start()
    {
        if (IsServer)
        {
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

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Only the server handles the hit logic
        if (!IsServer) return;
   
        var playerController = collision.GetComponentInParent<PlayerController>();

        // Make sure we haven't already collided & we don't kill the shooter themselves
        if (playerController != null && (hasCollided || playerController.OwnerClientId != shooterId))
        {
            Debug.Log("killed");
            ulong killerId = shooterId;
            playerController.Die(killerId);

            // Despawn this projectile
            if (NetworkObject != null)
            {
                NetworkObject.Despawn(true);
            }
            hasCollided = true;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision) {
        hasCollided = true;
    }
}
