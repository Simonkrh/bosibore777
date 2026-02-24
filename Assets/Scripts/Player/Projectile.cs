using Unity.Netcode;
using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : NetworkBehaviour
{
    public float lifetime = 10f;
    [Tooltip("How many wall bounces before despawn. -1 means unlimited.")]
    public int maxWallBounces = -1;
    [Tooltip("Fallback minimum travel radius before self-hit is armed if no shooter radius is provided.")]
    public float minSelfHitUnlockRadius = 0.1f;

    private Rigidbody2D rb;
    private bool hasCollided = false;
    private ulong shooterId;
    private int wallBounceCount = 0;
    private bool hasBouncedOffWall;
    private bool shooterConfigured;
    private bool shooterCanBeHit;
    private Vector2 shooterPositionAtFire;
    private float shooterSelfHitUnlockRadius;
    private bool destroyInvoked;
    private int shotSequence = -1;
    private Action<ulong, int> destroyedCallback;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f; // top-down, no gravity
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    private void Start()
    {
        if (IsServer)
        {
            DestroyProjectileAfterLifetime();
        }
    }

    public override void OnNetworkSpawn()
    {
        shooterCanBeHit = false;
        IgnoreCollisionWithOtherProjectiles();

        if (IsServer)
        {
            return;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.simulated = false;
        }

        Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = false;
            }
        }
    }

    private void IgnoreCollisionWithOtherProjectiles()
    {
        Collider2D[] myColliders = GetComponentsInChildren<Collider2D>(true);
        Projectile[] allProjectiles = FindObjectsByType<Projectile>(FindObjectsSortMode.None);

        for (int i = 0; i < allProjectiles.Length; i++)
        {
            Projectile other = allProjectiles[i];
            if (other == null || other == this)
            {
                continue;
            }

            Collider2D[] otherColliders = other.GetComponentsInChildren<Collider2D>(true);
            for (int myIndex = 0; myIndex < myColliders.Length; myIndex++)
            {
                Collider2D myCollider = myColliders[myIndex];
                if (myCollider == null)
                {
                    continue;
                }

                for (int otherIndex = 0; otherIndex < otherColliders.Length; otherIndex++)
                {
                    Collider2D otherCollider = otherColliders[otherIndex];
                    if (otherCollider == null)
                    {
                        continue;
                    }

                    Physics2D.IgnoreCollision(myCollider, otherCollider, true);
                }
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer || hasCollided || hasBouncedOffWall || shooterCanBeHit || !shooterConfigured)
        {
            return;
        }

        float unlockRadius = Mathf.Max(minSelfHitUnlockRadius, shooterSelfHitUnlockRadius);
        Vector2 currentPosition = rb != null ? rb.position : (Vector2)transform.position;
        if ((currentPosition - shooterPositionAtFire).sqrMagnitude >= unlockRadius * unlockRadius)
        {
            shooterCanBeHit = true;
        }
    }

    private void DestroyProjectileAfterLifetime()
    {
        StartCoroutine(DestroyAfterLifetime());
    }

    private System.Collections.IEnumerator DestroyAfterLifetime()
    {
        yield return new WaitForSeconds(lifetime);
        if (!IsServer)
        {
            yield break;
        }

        DespawnOrDestroyProjectile();
    }

    public void ConfigureShooter(ulong id, Vector2 shooterPosition, float selfHitUnlockRadius)
    {
        shooterId = id;
        shooterPositionAtFire = shooterPosition;
        shooterSelfHitUnlockRadius = Mathf.Max(0f, selfHitUnlockRadius);
        shooterConfigured = true;
        shooterCanBeHit = false;
    }

    public void SetShooterId(ulong id)
    {
        ConfigureShooter(id, rb != null ? rb.position : (Vector2)transform.position, minSelfHitUnlockRadius);
    }

    public void ConfigureServerProjectile(ulong id, int sequence, Vector2 shooterPosition, float selfHitUnlockRadius, Action<ulong, int> onDestroyed)
    {
        ConfigureShooter(id, shooterPosition, selfHitUnlockRadius);
        shotSequence = sequence;
        destroyedCallback = onDestroyed;
    }

    public void ForceDestroy()
    {
        if (!IsServer)
        {
            return;
        }

        hasCollided = true;
        DespawnOrDestroyProjectile();
    }

    private void DespawnOrDestroyProjectile()
    {
        if (destroyInvoked)
        {
            return;
        }

        destroyInvoked = true;
        destroyedCallback?.Invoke(shooterId, shotSequence);

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
            return;
        }

        Destroy(gameObject);
    }

    private bool TryHitPlayer(Collider2D collisionCollider)
    {
        var playerController = collisionCollider.GetComponentInParent<PlayerController>();
        if (playerController == null || hasCollided)
        {
            return false;
        }

        if (playerController.OwnerClientId == shooterId && !hasBouncedOffWall && !shooterCanBeHit)
        {
            return false;
        }

        Debug.Log("killed");
        ulong killerId = shooterId;
        playerController.Die(killerId);
        hasCollided = true;
        DespawnOrDestroyProjectile();

        return true;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Only the server handles the hit logic
        if (!IsServer) return;

        TryHitPlayer(collision);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!IsServer || hasCollided) return;

        if (TryHitPlayer(collision.collider))
        {
            return;
        }

        if (collision.collider.GetComponentInParent<Projectile>() != null)
        {
            return;
        }

        // Let physics handle reflection on walls; only despawn after configured bounce count.
        if (collision.collider.CompareTag("Wall"))
        {
            hasBouncedOffWall = true;
            wallBounceCount++;

            if (maxWallBounces >= 0 && wallBounceCount > maxWallBounces)
            {
                hasCollided = true;
                DespawnOrDestroyProjectile();
            }
            return;
        }

        hasCollided = true;
        DespawnOrDestroyProjectile();
    }
}
