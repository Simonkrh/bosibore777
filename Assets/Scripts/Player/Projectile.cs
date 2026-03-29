using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using System;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : NetworkBehaviour
{
    public enum DestroyCause
    {
        Unknown = 0,
        LifetimeExpired = 1,
        PlayerHit = 2,
        WallBounceLimit = 3,
        Collision = 4,
        Forced = 5
    }

    public enum AudioProfile
    {
        Standard = 0,
        Bomb = 1,
        Minigun = 2,
        Rocket = 3,
        Lazer = 4
    }

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
    private Action<Projectile, DestroyCause> preDestroyServerCallback;
    private SpriteRenderer[] visualRenderers;
    private bool allowImmediateSelfHit;
    private bool damagesPlayersOnContact = true;
    private bool destroyOnPlayerContact = true;
    private bool ignorePlayerCollisions;
    private DestroyCause pendingDestroyCause = DestroyCause.Unknown;
    private AudioProfile audioProfile = AudioProfile.Standard;
    private GameManager gameManager;
    private DirectionalSmokeBurst.Config directionalDespawnSmokeConfig;
    private Coroutine destroyAfterLifetimeCoroutine;

    public ulong ShooterClientId => shooterId;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f; // top-down, no gravity
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.None;
        CacheVisualRenderersIfNeeded();
    }

    private void Start()
    {
        if (IsServer)
        {
            RestartLifetimeCountdown();
        }
    }

    public override void OnNetworkSpawn()
    {
        shooterCanBeHit = false;
        damagesPlayersOnContact = true;
        destroyOnPlayerContact = true;
        ignorePlayerCollisions = false;
        IgnoreCollisionWithOtherProjectiles();
        EnsureNetworkSyncComponentsEnabled();

        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.None;

            if (IsServer)
            {
                rb.bodyType = RigidbodyType2D.Dynamic;
                rb.simulated = true;
            }
            else
            {
                // Non-authority projectiles are display-only; server owns all projectile physics.
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = true;
            }
        }

        if (!IsServer)
        {
            Collider2D[] colliders = GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = false;
                }
            }
        }
    }

    private void EnsureNetworkSyncComponentsEnabled()
    {
        NetworkRigidbody2D netRigidbody = GetComponent<NetworkRigidbody2D>();
        if (netRigidbody != null)
        {
            netRigidbody.enabled = false;
        }

        NetworkTransform[] transforms = GetComponentsInChildren<NetworkTransform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null)
            {
                transforms[i].enabled = true;
                transforms[i].Interpolate = false;
                transforms[i].PositionThreshold = 0.0001f;
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
        if (!IsServer || hasCollided || shooterCanBeHit || !shooterConfigured)
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

    public void SetLifetimeServer(float lifetimeSeconds)
    {
        if (!IsServer)
        {
            return;
        }

        lifetime = Mathf.Max(0f, lifetimeSeconds);
        if (isActiveAndEnabled)
        {
            RestartLifetimeCountdown();
        }
    }

    private void RestartLifetimeCountdown()
    {
        if (!IsServer)
        {
            return;
        }

        if (destroyAfterLifetimeCoroutine != null)
        {
            StopCoroutine(destroyAfterLifetimeCoroutine);
        }

        destroyAfterLifetimeCoroutine = StartCoroutine(DestroyAfterLifetime());
    }

    private System.Collections.IEnumerator DestroyAfterLifetime()
    {
        yield return new WaitForSeconds(lifetime);
        destroyAfterLifetimeCoroutine = null;
        if (!IsServer)
        {
            yield break;
        }

        pendingDestroyCause = DestroyCause.LifetimeExpired;
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

    public void ConfigureSelfHitBehaviorServer(bool allowImmediateSelfHitValue)
    {
        if (!IsServer)
        {
            return;
        }

        allowImmediateSelfHit = allowImmediateSelfHitValue;
    }

    public void ConfigurePlayerHitBehaviorServer(bool damagesPlayers, bool destroyOnContact, bool ignoreCollisionsWithPlayers = false)
    {
        if (!IsServer)
        {
            return;
        }

        damagesPlayersOnContact = damagesPlayers;
        destroyOnPlayerContact = destroyOnContact;
        ignorePlayerCollisions = ignoreCollisionsWithPlayers;

        if (ignorePlayerCollisions)
        {
            IgnoreCollisionWithPlayers();
        }
    }

    public void SetPreDestroyServerCallback(Action<Projectile, DestroyCause> callback)
    {
        if (!IsServer)
        {
            return;
        }

        preDestroyServerCallback += callback;
    }

    public void ConfigureAudioProfileServer(AudioProfile profile)
    {
        if (!IsServer)
        {
            return;
        }

        audioProfile = profile;
    }

    public void ConfigureDirectionalDespawnSmokeServer(DirectionalSmokeBurst.Config config)
    {
        if (!IsServer)
        {
            return;
        }

        directionalDespawnSmokeConfig = config;
    }

    public void SetVisualColorServer(Color color)
    {
        if (!IsServer)
        {
            return;
        }

        ApplyVisualColor(color);
        SetVisualColorClientRpc(color);
    }

    [ClientRpc]
    private void SetVisualColorClientRpc(Color color)
    {
        if (IsServer)
        {
            return;
        }

        ApplyVisualColor(color);
    }

    private void CacheVisualRenderersIfNeeded()
    {
        if (visualRenderers != null && visualRenderers.Length > 0)
        {
            return;
        }

        visualRenderers = GetComponentsInChildren<SpriteRenderer>(true);
    }

    private void ApplyVisualColor(Color color)
    {
        CacheVisualRenderersIfNeeded();
        for (int i = 0; i < visualRenderers.Length; i++)
        {
            if (visualRenderers[i] != null)
            {
                visualRenderers[i].color = color;
            }
        }
    }

    public void ForceDestroy()
    {
        if (!IsServer)
        {
            return;
        }

        hasCollided = true;
        pendingDestroyCause = DestroyCause.Forced;
        DespawnOrDestroyProjectile();
    }

    private GameManager ResolveGameManager()
    {
        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        return gameManager;
    }

    private void TryPlayBounceSoundServer()
    {
        if (!IsServer || audioProfile == AudioProfile.Lazer)
        {
            return;
        }

        ResolveGameManager()?.PlayBulletBounceSoundServer(transform.position);
    }

    private void TryPlayDespawnSoundServer(DestroyCause destroyCause)
    {
        if (!IsServer)
        {
            return;
        }

        if (audioProfile != AudioProfile.Standard && audioProfile != AudioProfile.Rocket)
        {
            return;
        }

        if (destroyCause == DestroyCause.Unknown ||
            destroyCause == DestroyCause.PlayerHit ||
            destroyCause == DestroyCause.Forced)
        {
            return;
        }

        ResolveGameManager()?.PlayBulletDespawnSoundServer(transform.position);
    }

    private void TryPlayDirectionalDespawnSmokeServer(DestroyCause destroyCause)
    {
        if (!IsServer)
        {
            return;
        }

        if (destroyCause == DestroyCause.Unknown ||
            destroyCause == DestroyCause.PlayerHit ||
            destroyCause == DestroyCause.Forced)
        {
            return;
        }

        if (directionalDespawnSmokeConfig == null)
        {
            return;
        }

        Vector2 facingDirection = transform.up;
        if (rb != null && rb.linearVelocity.sqrMagnitude > 0.0001f)
        {
            facingDirection = rb.linearVelocity.normalized;
        }

        if (directionalDespawnSmokeConfig.TryCreateSettings(transform.position, facingDirection, out DirectionalSmokeBurst.Settings settings))
        {
            GameManager resolvedGameManager = ResolveGameManager();
            if (resolvedGameManager != null)
            {
                resolvedGameManager.PlayDirectionalSmokeBurstServer(settings, "ProjectileDespawnSmokeBurst");
            }
            else
            {
                DirectionalSmokeBurst.Spawn(settings, "ProjectileDespawnSmokeBurst");
            }
        }
    }

    private void DespawnOrDestroyProjectile()
    {
        if (destroyInvoked)
        {
            return;
        }

        destroyInvoked = true;
        if (destroyAfterLifetimeCoroutine != null)
        {
            StopCoroutine(destroyAfterLifetimeCoroutine);
            destroyAfterLifetimeCoroutine = null;
        }

        TryPlayDespawnSoundServer(pendingDestroyCause);
        TryPlayDirectionalDespawnSmokeServer(pendingDestroyCause);
        try
        {
            preDestroyServerCallback?.Invoke(this, pendingDestroyCause);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
        }

        try
        {
            destroyedCallback?.Invoke(shooterId, shotSequence);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
        }

        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
            return;
        }

        Destroy(gameObject);
    }

    private bool TryGetPlayerController(Collider2D collisionCollider, out PlayerController playerController)
    {
        playerController = collisionCollider != null ? collisionCollider.GetComponentInParent<PlayerController>() : null;
        return playerController != null;
    }

    private void IgnoreCollisionWithPlayers()
    {
        Collider2D[] myColliders = GetComponentsInChildren<Collider2D>(true);
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            PlayerController player = players[i];
            if (player == null)
            {
                continue;
            }

            Collider2D[] playerColliders = player.GetComponentsInChildren<Collider2D>(true);
            for (int myIndex = 0; myIndex < myColliders.Length; myIndex++)
            {
                Collider2D myCollider = myColliders[myIndex];
                if (myCollider == null)
                {
                    continue;
                }

                for (int playerIndex = 0; playerIndex < playerColliders.Length; playerIndex++)
                {
                    Collider2D playerCollider = playerColliders[playerIndex];
                    if (playerCollider == null)
                    {
                        continue;
                    }

                    Physics2D.IgnoreCollision(myCollider, playerCollider, true);
                }
            }
        }
    }

    private bool TryHitPlayer(Collider2D collisionCollider)
    {
        if (!TryGetPlayerController(collisionCollider, out PlayerController playerController))
        {
            return false;
        }

        if (playerController == null || hasCollided)
        {
            return false;
        }

        // By default self-hit is only valid after bounce + unlock travel; special projectiles can override this.
        if (!allowImmediateSelfHit && playerController.OwnerClientId == shooterId && (!hasBouncedOffWall || !shooterCanBeHit))
        {
            return false;
        }

        if (!damagesPlayersOnContact)
        {
            if (!destroyOnPlayerContact)
            {
                return false;
            }

            hasCollided = true;
            pendingDestroyCause = DestroyCause.PlayerHit;
            DespawnOrDestroyProjectile();
            return true;
        }

        Debug.Log("killed");
        ulong killerId = shooterId;
        playerController.Die(killerId);
        hasCollided = true;
        pendingDestroyCause = DestroyCause.PlayerHit;
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

        if (ignorePlayerCollisions && TryGetPlayerController(collision.collider, out _))
        {
            return;
        }

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
            bool shouldDespawnFromBounce = maxWallBounces >= 0 && wallBounceCount > maxWallBounces;
            if (shouldDespawnFromBounce)
            {
                hasCollided = true;
                pendingDestroyCause = DestroyCause.WallBounceLimit;
                DespawnOrDestroyProjectile();
                return;
            }

            TryPlayBounceSoundServer();
            return;
        }

        hasCollided = true;
        pendingDestroyCause = DestroyCause.Collision;
        DespawnOrDestroyProjectile();
    }
}
