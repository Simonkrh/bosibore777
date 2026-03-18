using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Behaviors/Bomb", fileName = "BombAbilityBehavior")]
public class BombAbilityBehavior : AbilityBehavior
{
    [Header("Bomb Spawn")]
    [Tooltip("Projectile prefab spawned on first press. Should include NetworkObject + Projectile.")]
    [SerializeField] private GameObject bombProjectilePrefab;
    [Tooltip("Launch speed for the initial bomb projectile.")]
    [SerializeField] private float bombSpeed = 3f;
    [Tooltip("Extra distance from muzzle before spawning the bomb.")]
    [SerializeField] private float extraSpawnDistance = 0.09f;

    [Header("Detonation")]
    [Tooltip("Projectile prefab spawned for each explosion shard. Should include NetworkObject + Projectile.")]
    [SerializeField] private GameObject shardProjectilePrefab;
    [Tooltip("Number of radial shards emitted on detonation.")]
    [SerializeField] private int shardCount = 40;
    [Tooltip("Travel speed for each shard.")]
    [SerializeField] private float shardSpeed = 2f;
    [Tooltip("Small radial offset so shards do not overlap perfectly at spawn.")]
    [SerializeField] private float shardSpawnRadius = 0.04f;
    [Tooltip("Multiplier used to derive unique shot sequences per shard from the detonation shot sequence.")]
    [SerializeField] private int shardSequenceStride = 1000;
    [Tooltip("When enabled, non-trigger colliders on shards are disabled so shards pass through walls.")]
    [SerializeField] private bool shardsPassThroughWalls = true;
    [Tooltip("When enabled, both bomb and shards are tinted to the shooter's tank color.")]
    [SerializeField] private bool tintProjectilesWithShooterColor = false;

    [Header("Shard Motion")]
    [Tooltip("Minimum random spin speed for shards (degrees/second).")]
    [SerializeField] private float shardSpinSpeedMinDegreesPerSecond = -540f;
    [Tooltip("Maximum random spin speed for shards (degrees/second).")]
    [SerializeField] private float shardSpinSpeedMaxDegreesPerSecond = 540f;
    [Tooltip("Speed multiplier while a shard is over wall colliders (0..1).")]
    [SerializeField] private float overWallSpeedMultiplier = 0.15f;
    [Tooltip("Probe radius used to detect whether a shard is currently over a wall.")]
    [SerializeField] private float overWallProbeRadius = 0.04f;
    [Tooltip("How quickly shard speed blends between normal and over-wall slowed speed.")]
    [SerializeField] private float overWallSpeedTransitionPerSecond = 15f;

    private readonly Dictionary<ulong, NetworkObject> activeBombsByOwner = new Dictionary<ulong, NetworkObject>();
    private int nextAutoDetonationSequence = -1000000000;

    public override AbilityActivationResult TryActivateServer(TankController owner, int shotSequence)
    {
        if (owner == null || !owner.IsServer)
        {
            return AbilityActivationResult.NotActivated;
        }

        ulong ownerClientId = owner.OwnerClientId;
        if (TryGetActiveBomb(ownerClientId, out NetworkObject activeBomb))
        {
            if (TryDetonateBomb(owner, ownerClientId, activeBomb, shotSequence))
            {
                return AbilityActivationResult.ActivatedConsume;
            }

            // Keep the ability equipped so the user can retry detonation instead of firing fallback bullets.
            return AbilityActivationResult.ActivatedKeep;
        }

        if (!TrySpawnBomb(owner, ownerClientId, shotSequence))
        {
            return AbilityActivationResult.NotActivated;
        }

        // Keep equipped until a successful detonation.
        return AbilityActivationResult.ActivatedKeep;
    }

    private bool TrySpawnBomb(TankController owner, ulong ownerClientId, int shotSequence)
    {
        if (bombProjectilePrefab == null)
        {
            return false;
        }

        if (!owner.TryComputeAbilityProjectileSpawn(
                extraSpawnDistance,
                out Vector2 spawnPosition2D,
                out Quaternion spawnRotation,
                out Vector2 fireDirection,
                out _))
        {
            return false;
        }

        if (!owner.TrySpawnAbilityProjectile(
                bombProjectilePrefab,
                shotSequence,
                spawnPosition2D,
                spawnRotation,
                fireDirection,
                bombSpeed,
                Projectile.AudioProfile.Bomb,
                out NetworkObject spawnedBomb))
        {
            return false;
        }

        Projectile projectile = spawnedBomb.gameObject.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.SetPreDestroyServerCallback((projectileInstance, cause) =>
                HandleBombPreDestroyServer(owner, ownerClientId, projectileInstance, cause));
        }

        TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
        if (abilityController != null)
        {
            abilityController.RegisterAbilityProjectileServer(spawnedBomb);
        }

        if (tintProjectilesWithShooterColor)
        {
            if (projectile != null)
            {
                projectile.SetVisualColorServer(owner.tankColor.Value);
            }
        }

        owner.ResolveGameManager()?.PlayBulletShootSoundServer(spawnedBomb.transform.position);
        activeBombsByOwner[ownerClientId] = spawnedBomb;
        return true;
    }

    private bool TryDetonateBomb(TankController owner, ulong ownerClientId, NetworkObject bombNetworkObject, int shotSequence)
    {
        if (shardProjectilePrefab == null || bombNetworkObject == null || !bombNetworkObject.IsSpawned)
        {
            return false;
        }

        Vector2 detonationPosition = bombNetworkObject.transform.position;
        bool spawnedAnyShard = TrySpawnShards(owner, detonationPosition, shotSequence);
        if (spawnedAnyShard)
        {
            owner.ResolveGameManager()?.PlayBombExplodeSoundServer(detonationPosition);
        }

        activeBombsByOwner.Remove(ownerClientId);
        ForceDespawnBomb(bombNetworkObject.gameObject);
        return spawnedAnyShard;
    }

    private bool TrySpawnShards(TankController owner, Vector2 detonationPosition, int sequenceBase)
    {
        if (owner == null || !owner.IsServer || shardProjectilePrefab == null)
        {
            return false;
        }

        Color projectileColor = owner.tankColor.Value;
        bool spawnedAnyShard = false;
        int count = Mathf.Max(1, shardCount);
        for (int i = 0; i < count; i++)
        {
            // True random direction per shard; allows clustered bursts in one region.
            float angle = Random.Range(0f, 360f);
            float radians = angle * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)).normalized;
            Vector2 spawnPosition = detonationPosition + direction * Mathf.Max(0f, shardSpawnRadius);
            Quaternion spawnRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            int shardShotSequence = unchecked(sequenceBase * Mathf.Max(1, shardSequenceStride) + i);

            if (!owner.TrySpawnAbilityProjectile(
                    shardProjectilePrefab,
                    shardShotSequence,
                    spawnPosition,
                    spawnRotation,
                    direction,
                    shardSpeed,
                    Projectile.AudioProfile.Bomb,
                    out NetworkObject shardNetworkObject))
            {
                continue;
            }

            if (shardsPassThroughWalls)
            {
                DisableSolidColliders(shardNetworkObject.gameObject);
            }

            ConfigureShardMotion(shardNetworkObject.gameObject, shardSpeed);

            Projectile spawnedShardProjectile = shardNetworkObject.gameObject.GetComponent<Projectile>();
            if (spawnedShardProjectile != null)
            {
                // Bomb shards are intended to be lethal immediately, even without bounce.
                spawnedShardProjectile.ConfigureSelfHitBehaviorServer(true);
            }

            if (tintProjectilesWithShooterColor)
            {
                if (spawnedShardProjectile != null)
                {
                    spawnedShardProjectile.SetVisualColorServer(projectileColor);
                }
            }

            spawnedAnyShard = true;
        }

        return spawnedAnyShard;
    }

    private void HandleBombPreDestroyServer(
        TankController owner,
        ulong ownerClientId,
        Projectile bombProjectile,
        Projectile.DestroyCause destroyCause)
    {
        if (bombProjectile == null || !TryGetActiveBomb(ownerClientId, out NetworkObject activeBomb))
        {
            return;
        }

        if (activeBomb.gameObject != bombProjectile.gameObject)
        {
            return;
        }

        if (destroyCause != Projectile.DestroyCause.PlayerHit &&
            destroyCause != Projectile.DestroyCause.LifetimeExpired)
        {
            return;
        }

        int autoSequenceBase = nextAutoDetonationSequence++;
        Vector2 detonationPosition = bombProjectile.transform.position;
        bool spawnedAnyShard = TrySpawnShards(owner, detonationPosition, autoSequenceBase);
        if (spawnedAnyShard)
        {
            owner.ResolveGameManager()?.PlayBombExplodeSoundServer(detonationPosition);
        }
        activeBombsByOwner.Remove(ownerClientId);

        if ((destroyCause == Projectile.DestroyCause.LifetimeExpired ||
             destroyCause == Projectile.DestroyCause.PlayerHit) &&
            owner != null)
        {
            TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
            if (abilityController != null)
            {
                abilityController.ClearEquippedAbilityServer();
            }
        }
    }

    private void ConfigureShardMotion(GameObject shardObject, float baseSpeed)
    {
        if (shardObject == null)
        {
            return;
        }

        BombShardMotion motion = shardObject.GetComponent<BombShardMotion>();
        if (motion == null)
        {
            motion = shardObject.AddComponent<BombShardMotion>();
        }

        float minSpin = Mathf.Min(shardSpinSpeedMinDegreesPerSecond, shardSpinSpeedMaxDegreesPerSecond);
        float maxSpin = Mathf.Max(shardSpinSpeedMinDegreesPerSecond, shardSpinSpeedMaxDegreesPerSecond);
        float spinSpeed = Random.Range(minSpin, maxSpin);

        motion.Configure(
            Mathf.Max(0f, baseSpeed),
            spinSpeed,
            Mathf.Clamp01(overWallSpeedMultiplier),
            Mathf.Max(0f, overWallProbeRadius),
            Mathf.Max(0f, overWallSpeedTransitionPerSecond));
    }

    private bool TryGetActiveBomb(ulong ownerClientId, out NetworkObject activeBomb)
    {
        activeBomb = null;
        if (!activeBombsByOwner.TryGetValue(ownerClientId, out NetworkObject trackedBomb))
        {
            return false;
        }

        if (trackedBomb == null || !trackedBomb.IsSpawned)
        {
            activeBombsByOwner.Remove(ownerClientId);
            return false;
        }

        activeBomb = trackedBomb;
        return true;
    }

    private static void DisableSolidColliders(GameObject projectileObject)
    {
        if (projectileObject == null)
        {
            return;
        }

        Collider2D[] colliders = projectileObject.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider != null && !collider.isTrigger)
            {
                collider.enabled = false;
            }
        }
    }

    private static void ForceDespawnBomb(GameObject bombObject)
    {
        if (bombObject == null)
        {
            return;
        }

        Projectile projectile = bombObject.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.ForceDestroy();
            return;
        }

        NetworkObject networkObject = bombObject.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn(true);
            return;
        }

        Object.Destroy(bombObject);
    }

    private void OnDisable()
    {
        activeBombsByOwner.Clear();
    }

    private void OnValidate()
    {
        bombSpeed = Mathf.Max(0f, bombSpeed);
        extraSpawnDistance = Mathf.Max(0f, extraSpawnDistance);
        shardCount = Mathf.Clamp(shardCount, 1, 128);
        shardSpeed = Mathf.Max(0f, shardSpeed);
        shardSpawnRadius = Mathf.Max(0f, shardSpawnRadius);
        shardSequenceStride = Mathf.Max(1, shardSequenceStride);
        overWallSpeedMultiplier = Mathf.Clamp01(overWallSpeedMultiplier);
        overWallProbeRadius = Mathf.Max(0f, overWallProbeRadius);
        overWallSpeedTransitionPerSecond = Mathf.Max(0f, overWallSpeedTransitionPerSecond);
    }
}
