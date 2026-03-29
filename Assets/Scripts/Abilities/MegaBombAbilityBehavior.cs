using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Behaviors/Mega Bomb", fileName = "MegaBombAbilityBehavior")]
public class MegaBombAbilityBehavior : AbilityBehavior
{
    public const string MegaBombAbilityId = "ability.megabomb";

    private enum MegaBombSpawnResult
    {
        Failed = 0,
        Spawned = 1,
        ReleasedImmediately = 2
    }

    private sealed class ActiveMegaBombState
    {
        public NetworkObject BombNetworkObject;
        public Color ShooterColor;
        public bool IsReleaseTriggered;
    }

    [Header("Mega Bomb Spawn")]
    [SerializeField] private GameObject megaBombProjectilePrefab;
    [SerializeField] private float megaBombSpeed = 1.8f;
    [SerializeField] private float extraSpawnDistance = 0.06f;

    [Header("Mini Bomb Release")]
    [SerializeField] private GameObject miniBombProjectilePrefab;
    [SerializeField] private int miniBombCount = 4;
    [SerializeField] private float miniBombSpeed = 2.25f;
    [SerializeField] private float miniBombLifetime = 1.75f;
    [SerializeField] private float releaseDelaySeconds = 0.35f;
    [SerializeField] private float lockdownReleaseDelaySeconds = 1f;
    [SerializeField] private float miniBombSpawnRadius = 0.05f;
    [SerializeField] private int miniBombSequenceStride = 1000;
    [SerializeField] private bool miniBombsPassThroughWalls = true;

    [Header("Shard Detonation")]
    [SerializeField] private GameObject shardProjectilePrefab;
    [SerializeField] private int shardCount = 40;
    [SerializeField] private float shardSpeed = 2.5f;
    [SerializeField] private float shardSpawnRadius = 0.04f;
    [SerializeField] private int shardSequenceStride = 1000;
    [SerializeField] private bool shardsPassThroughWalls = true;
    [SerializeField] private bool tintProjectilesWithShooterColor = false;

    [Header("Explosion Smoke")]
    [SerializeField]
    private DirectionalSmokeBurst.Config explosionSmoke = new DirectionalSmokeBurst.Config
    {
        directionVariationDegrees = 180f
    };

    [Header("Shard Motion")]
    [SerializeField] private float shardSpinSpeedMinDegreesPerSecond = -540f;
    [SerializeField] private float shardSpinSpeedMaxDegreesPerSecond = 540f;
    [SerializeField] private float overWallSpeedMultiplier = 0.15f;
    [SerializeField] private float overWallProbeRadius = 0.04f;
    [SerializeField] private float overWallSpeedTransitionPerSecond = 15f;

    private readonly Dictionary<ulong, ActiveMegaBombState> activeMegaBombsByOwner = new Dictionary<ulong, ActiveMegaBombState>();

    public override AbilityActivationResult TryActivateServer(TankController owner, int shotSequence)
    {
        if (owner == null || !owner.IsServer)
        {
            return AbilityActivationResult.NotActivated;
        }

        ulong ownerClientId = owner.OwnerClientId;
        if (TryGetActiveMegaBomb(ownerClientId, out ActiveMegaBombState activeMegaBomb))
        {
            if (TryReleaseMegaBomb(owner, ownerClientId, activeMegaBomb, shotSequence))
            {
                return AbilityActivationResult.ActivatedKeep;
            }

            return AbilityActivationResult.ActivatedKeep;
        }

        MegaBombSpawnResult spawnResult = TrySpawnMegaBomb(owner, ownerClientId, shotSequence);
        if (spawnResult == MegaBombSpawnResult.Failed)
        {
            return AbilityActivationResult.NotActivated;
        }

        if (spawnResult == MegaBombSpawnResult.ReleasedImmediately)
        {
            return AbilityActivationResult.ActivatedKeep;
        }

        return AbilityActivationResult.ActivatedKeep;
    }

    private MegaBombSpawnResult TrySpawnMegaBomb(TankController owner, ulong ownerClientId, int shotSequence)
    {
        if (megaBombProjectilePrefab == null)
        {
            return MegaBombSpawnResult.Failed;
        }

        if (!owner.TryComputeAbilityProjectileSpawn(
                megaBombProjectilePrefab,
                extraSpawnDistance,
                out Vector2 spawnPosition2D,
                out Quaternion spawnRotation,
                out Vector2 fireDirection,
                out _,
                out bool blockedByImmediateWallShot,
                false))
        {
            return MegaBombSpawnResult.Failed;
        }

        if (blockedByImmediateWallShot)
        {
            TrySpawnMiniBombs(ownerClientId, owner.tankColor.Value, spawnPosition2D, shotSequence);
            ResolveGameManager(owner)?.PlayMegaBombActivateSoundServer(spawnPosition2D);
            TankAbilityController blockedShotAbilityController = owner.GetComponent<TankAbilityController>();
            if (blockedShotAbilityController != null)
            {
                blockedShotAbilityController.ClearEquippedAbilityServer(lockdownReleaseDelaySeconds);
            }
            owner.TriggerBlockedShotBackfireServer();
            return MegaBombSpawnResult.ReleasedImmediately;
        }

        if (!owner.TrySpawnAbilityProjectile(
                megaBombProjectilePrefab,
                shotSequence,
                spawnPosition2D,
                spawnRotation,
                fireDirection,
                megaBombSpeed,
                Projectile.AudioProfile.Bomb,
                out NetworkObject spawnedMegaBomb))
        {
            return MegaBombSpawnResult.Failed;
        }

        Projectile projectile = spawnedMegaBomb.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.ConfigurePlayerHitBehaviorServer(false, false, true);
            projectile.SetPreDestroyServerCallback((projectileInstance, destroyCause) =>
                HandleMegaBombPreDestroyServer(owner, ownerClientId, shotSequence, projectileInstance, destroyCause));

            if (tintProjectilesWithShooterColor)
            {
                projectile.SetVisualColorServer(owner.tankColor.Value);
            }
        }

        TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
        if (abilityController != null)
        {
            abilityController.RegisterAbilityProjectileServer(spawnedMegaBomb);
        }
        GameManager resolvedGameManager = owner.ResolveGameManager();
        resolvedGameManager?.PlayBulletShootSoundServer(spawnedMegaBomb.transform.position);
        resolvedGameManager?.PlayMegaBombMusicServer(ownerClientId, spawnedMegaBomb.transform.position);
        owner.PlayShotAnimationServer(TankController.ShotAnimationType.Bomb);

        activeMegaBombsByOwner[ownerClientId] = new ActiveMegaBombState
        {
            BombNetworkObject = spawnedMegaBomb,
            ShooterColor = owner.tankColor.Value,
            IsReleaseTriggered = false
        };
        return MegaBombSpawnResult.Spawned;
    }

    private bool TryReleaseMegaBomb(
        TankController owner,
        ulong ownerClientId,
        ActiveMegaBombState activeMegaBombState,
        int releaseShotSequence)
    {
        NetworkObject megaBombNetworkObject = activeMegaBombState != null ? activeMegaBombState.BombNetworkObject : null;
        if (activeMegaBombState == null ||
            activeMegaBombState.IsReleaseTriggered ||
            megaBombNetworkObject == null ||
            !megaBombNetworkObject.IsSpawned)
        {
            return false;
        }

        Vector2 releasePosition = megaBombNetworkObject.transform.position;
        GameManager resolvedGameManager = ResolveGameManager(owner);
        resolvedGameManager?.StopMegaBombMusicServer(ownerClientId);
        resolvedGameManager?.PlayMegaBombActivateSoundServer(releasePosition);
        activeMegaBombState.IsReleaseTriggered = true;
        FreezeMegaBomb(megaBombNetworkObject.gameObject);
        owner.StartCoroutine(ReleaseMegaBombAfterDelay(ownerClientId, activeMegaBombState, releaseShotSequence));
        return true;
    }

    private System.Collections.IEnumerator ReleaseMegaBombAfterDelay(
        ulong ownerClientId,
        ActiveMegaBombState activeMegaBombState,
        int releaseShotSequence)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, releaseDelaySeconds));

        if (activeMegaBombState == null ||
            activeMegaBombState.BombNetworkObject == null ||
            !activeMegaBombState.BombNetworkObject.IsSpawned)
        {
            yield break;
        }

        if (!activeMegaBombsByOwner.TryGetValue(ownerClientId, out ActiveMegaBombState trackedState) ||
            !ReferenceEquals(trackedState, activeMegaBombState))
        {
            yield break;
        }

        Vector2 releasePosition = activeMegaBombState.BombNetworkObject.transform.position;
        bool spawnedAnyMiniBomb = TrySpawnMiniBombs(
            ownerClientId,
            activeMegaBombState.ShooterColor,
            releasePosition,
            releaseShotSequence);
        if (!spawnedAnyMiniBomb)
        {
            activeMegaBombState.IsReleaseTriggered = false;
            yield break;
        }

        ForceDespawnProjectile(activeMegaBombState.BombNetworkObject.gameObject);
    }

    private bool TrySpawnMiniBombs(
        ulong shooterClientId,
        Color projectileColor,
        Vector2 releasePosition,
        int sequenceBase)
    {
        if (miniBombProjectilePrefab == null)
        {
            return false;
        }

        bool spawnedAnyMiniBomb = false;
        int count = Mathf.Max(1, miniBombCount);
        int sequenceStride = Mathf.Max(1, miniBombSequenceStride);

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, 360f);
            float radians = angle * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)).normalized;
            Vector2 spawnPosition = releasePosition + direction * Mathf.Max(0f, miniBombSpawnRadius);
            Quaternion spawnRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            int miniBombShotSequence = unchecked(sequenceBase * sequenceStride + i);

            if (!TrySpawnProjectile(
                    miniBombProjectilePrefab,
                    shooterClientId,
                    miniBombShotSequence,
                    spawnPosition,
                    spawnRotation,
                    direction,
                    miniBombSpeed,
                    Projectile.AudioProfile.Bomb,
                    out NetworkObject miniBombNetworkObject))
            {
                continue;
            }

            if (miniBombsPassThroughWalls)
            {
                DisableSolidColliders(miniBombNetworkObject.gameObject);
            }

            Projectile miniBombProjectile = miniBombNetworkObject.GetComponent<Projectile>();
            if (miniBombProjectile != null)
            {
                miniBombProjectile.SetLifetimeServer(miniBombLifetime);
                miniBombProjectile.SetPreDestroyServerCallback((projectileInstance, destroyCause) =>
                    HandleMiniBombPreDestroyServer(
                        projectileColor,
                        projectileInstance,
                        destroyCause,
                        miniBombShotSequence));

                if (tintProjectilesWithShooterColor)
                {
                    miniBombProjectile.SetVisualColorServer(projectileColor);
                }
            }

            spawnedAnyMiniBomb = true;
        }

        return spawnedAnyMiniBomb;
    }

    private void HandleMegaBombPreDestroyServer(
        TankController owner,
        ulong ownerClientId,
        int spawnShotSequence,
        Projectile megaBombProjectile,
        Projectile.DestroyCause destroyCause)
    {
        if (megaBombProjectile == null || !TryGetActiveMegaBomb(ownerClientId, out ActiveMegaBombState activeMegaBomb))
        {
            return;
        }

        if (activeMegaBomb.BombNetworkObject == null || activeMegaBomb.BombNetworkObject.gameObject != megaBombProjectile.gameObject)
        {
            return;
        }

        GameManager resolvedGameManager = ResolveGameManager(owner);
        resolvedGameManager?.StopMegaBombMusicServer(ownerClientId);
        activeMegaBombsByOwner.Remove(ownerClientId);

        if (destroyCause == Projectile.DestroyCause.PlayerHit ||
            destroyCause == Projectile.DestroyCause.LifetimeExpired)
        {
            TrySpawnMiniBombs(ownerClientId, activeMegaBomb.ShooterColor, megaBombProjectile.transform.position, spawnShotSequence);
        }

        if (owner != null)
        {
            TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
            if (abilityController != null)
            {
                abilityController.ClearEquippedAbilityServer(lockdownReleaseDelaySeconds);
            }
        }
    }

    private void HandleMiniBombPreDestroyServer(
        Color projectileColor,
        Projectile miniBombProjectile,
        Projectile.DestroyCause destroyCause,
        int sequenceBase)
    {
        if (miniBombProjectile == null)
        {
            return;
        }

        if (destroyCause != Projectile.DestroyCause.PlayerHit &&
            destroyCause != Projectile.DestroyCause.LifetimeExpired)
        {
            return;
        }

        Vector2 detonationPosition = miniBombProjectile.transform.position;
        bool spawnedAnyShard = TrySpawnExplosionShards(
            miniBombProjectile.ShooterClientId,
            projectileColor,
            detonationPosition,
            sequenceBase);
        if (spawnedAnyShard)
        {
            PlayExplosionEffects(null, detonationPosition);
        }
    }

    private bool TrySpawnExplosionShards(
        ulong shooterClientId,
        Color projectileColor,
        Vector2 detonationPosition,
        int sequenceBase)
    {
        if (shardProjectilePrefab == null)
        {
            return false;
        }

        bool spawnedAnyShard = false;
        int count = Mathf.Max(1, shardCount);
        int sequenceStride = Mathf.Max(1, shardSequenceStride);

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, 360f);
            float radians = angle * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)).normalized;
            Vector2 spawnPosition = detonationPosition + direction * Mathf.Max(0f, shardSpawnRadius);
            Quaternion spawnRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            int shardShotSequence = unchecked(sequenceBase * sequenceStride + i);

            if (!TrySpawnProjectile(
                    shardProjectilePrefab,
                    shooterClientId,
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

            ConfigureShardMotion(shardNetworkObject.gameObject);

            Projectile spawnedShardProjectile = shardNetworkObject.GetComponent<Projectile>();
            if (spawnedShardProjectile != null)
            {
                spawnedShardProjectile.ConfigureSelfHitBehaviorServer(true);

                if (tintProjectilesWithShooterColor)
                {
                    spawnedShardProjectile.SetVisualColorServer(projectileColor);
                }
            }

            spawnedAnyShard = true;
        }

        return spawnedAnyShard;
    }

    private bool TrySpawnProjectile(
        GameObject projectilePrefab,
        ulong shooterClientId,
        int shotSequence,
        Vector2 spawnPosition,
        Quaternion spawnRotation,
        Vector2 direction,
        float launchSpeed,
        Projectile.AudioProfile audioProfile,
        out NetworkObject spawnedProjectile)
    {
        spawnedProjectile = null;
        NetworkManager manager = NetworkManager.Singleton;
        if (projectilePrefab == null || manager == null || !manager.IsServer || !manager.IsListening)
        {
            return false;
        }

        Vector3 spawnPosition3D = new Vector3(spawnPosition.x, spawnPosition.y, 0f);
        NetworkObject projectileNetObj = NetworkObject.InstantiateAndSpawn(
            projectilePrefab,
            manager,
            ownerClientId: Unity.Netcode.NetworkManager.ServerClientId,
            destroyWithScene: false,
            isPlayerObject: false,
            forceOverride: false,
            position: spawnPosition3D,
            rotation: spawnRotation);

        if (projectileNetObj == null)
        {
            return false;
        }

        GameObject projectileObject = projectileNetObj.gameObject;
        EnableProjectileTransformSyncComponents(projectileObject);
        projectileObject.layer = LayerMask.NameToLayer("Bullet");

        Rigidbody2D projectileRb = projectileObject.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            projectileRb.interpolation = RigidbodyInterpolation2D.None;
            projectileRb.linearVelocity = direction * Mathf.Max(0f, launchSpeed);
        }

        GameManager resolvedGameManager = ResolveGameManager(null);
        if (resolvedGameManager != null && resolvedGameManager.projectilesContainer != null)
        {
            projectileObject.transform.SetParent(resolvedGameManager.projectilesContainer.transform, true);
        }

        Projectile projectileComponent = projectileObject.GetComponent<Projectile>();
        if (projectileComponent != null)
        {
            projectileComponent.ConfigureServerProjectile(
                shooterClientId,
                shotSequence,
                spawnPosition,
                0f,
                null);
            projectileComponent.ConfigureAudioProfileServer(audioProfile);
        }

        spawnedProjectile = projectileNetObj;
        return true;
    }

    private void ConfigureShardMotion(GameObject shardObject)
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
            Mathf.Max(0f, shardSpeed),
            spinSpeed,
            Mathf.Clamp01(overWallSpeedMultiplier),
            Mathf.Max(0f, overWallProbeRadius),
            Mathf.Max(0f, overWallSpeedTransitionPerSecond));
    }

    private void PlayExplosionEffects(TankController owner, Vector2 detonationPosition)
    {
        ResolveGameManager(owner)?.PlayBombExplodeSoundServer(detonationPosition);

        if (explosionSmoke != null &&
            explosionSmoke.TryCreateSettings(detonationPosition, Vector2.up, out DirectionalSmokeBurst.Settings settings))
        {
            GameManager resolvedGameManager = ResolveGameManager(owner);
            if (resolvedGameManager != null)
            {
                resolvedGameManager.PlayDirectionalSmokeBurstServer(settings, "MegaBombExplosionSmokeBurst");
            }
            else
            {
                DirectionalSmokeBurst.Spawn(settings, "MegaBombExplosionSmokeBurst");
            }
        }
    }

    private static void EnableProjectileTransformSyncComponents(GameObject projectile)
    {
        if (projectile == null)
        {
            return;
        }

        Unity.Netcode.Components.NetworkRigidbody2D netRigidbody = projectile.GetComponent<Unity.Netcode.Components.NetworkRigidbody2D>();
        if (netRigidbody != null)
        {
            netRigidbody.enabled = false;
        }

        Unity.Netcode.Components.NetworkTransform[] transforms =
            projectile.GetComponentsInChildren<Unity.Netcode.Components.NetworkTransform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Unity.Netcode.Components.NetworkTransform transformComponent = transforms[i];
            if (transformComponent == null)
            {
                continue;
            }

            transformComponent.enabled = true;
            transformComponent.Interpolate = false;
            transformComponent.PositionThreshold = 0.0001f;
        }
    }

    private static GameManager ResolveGameManager(TankController owner)
    {
        if (owner != null)
        {
            GameManager ownerGameManager = owner.ResolveGameManager();
            if (ownerGameManager != null)
            {
                return ownerGameManager;
            }
        }

        return Object.FindFirstObjectByType<GameManager>();
    }

    private bool TryGetActiveMegaBomb(ulong ownerClientId, out ActiveMegaBombState activeMegaBomb)
    {
        activeMegaBomb = null;
        if (!activeMegaBombsByOwner.TryGetValue(ownerClientId, out ActiveMegaBombState trackedBomb))
        {
            return false;
        }

        if (trackedBomb == null ||
            trackedBomb.BombNetworkObject == null ||
            !trackedBomb.BombNetworkObject.IsSpawned)
        {
            activeMegaBombsByOwner.Remove(ownerClientId);
            return false;
        }

        activeMegaBomb = trackedBomb;
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

    private static void FreezeMegaBomb(GameObject projectileObject)
    {
        if (projectileObject == null)
        {
            return;
        }

        Rigidbody2D rb = projectileObject.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
        }

        DisableSolidColliders(projectileObject);
    }

    private static void ForceDespawnProjectile(GameObject projectileObject)
    {
        if (projectileObject == null)
        {
            return;
        }

        Projectile projectile = projectileObject.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.ForceDestroy();
            return;
        }

        NetworkObject networkObject = projectileObject.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkObject.Despawn(true);
            return;
        }

        Object.Destroy(projectileObject);
    }

    private void OnDisable()
    {
        activeMegaBombsByOwner.Clear();
    }

    private void OnValidate()
    {
        megaBombSpeed = Mathf.Max(0f, megaBombSpeed);
        extraSpawnDistance = Mathf.Max(0f, extraSpawnDistance);
        miniBombCount = Mathf.Clamp(miniBombCount, 1, 16);
        miniBombSpeed = Mathf.Max(0f, miniBombSpeed);
        miniBombLifetime = Mathf.Max(0.05f, miniBombLifetime);
        releaseDelaySeconds = Mathf.Max(0f, releaseDelaySeconds);
        lockdownReleaseDelaySeconds = Mathf.Max(0f, lockdownReleaseDelaySeconds);
        miniBombSpawnRadius = Mathf.Max(0f, miniBombSpawnRadius);
        miniBombSequenceStride = Mathf.Max(1, miniBombSequenceStride);
        shardCount = Mathf.Clamp(shardCount, 1, 128);
        shardSpeed = Mathf.Max(0f, shardSpeed);
        shardSpawnRadius = Mathf.Max(0f, shardSpawnRadius);
        shardSequenceStride = Mathf.Max(1, shardSequenceStride);
        overWallSpeedMultiplier = Mathf.Clamp01(overWallSpeedMultiplier);
        overWallProbeRadius = Mathf.Max(0f, overWallProbeRadius);
        overWallSpeedTransitionPerSecond = Mathf.Max(0f, overWallSpeedTransitionPerSecond);
        explosionSmoke?.ClampInEditor();
    }
}
