using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Abilities/Behaviors/Lazer", fileName = "LazerAbilityBehavior")]
public class LazerAbilityBehavior : AbilityBehavior
{
    public const string LazerAbilityId = "ability.lazer";

    [Header("Tank Visuals")]
    [Tooltip("Body prefab used while this ability is equipped. Must be inside a Resources folder.")]
    [SerializeField] private GameObject lazerTankBodyPrefab;
    [SerializeField, HideInInspector] private string lazerTankBodyPrefabResourcePath = "Prefabs/Abilities/Lazer/LazerTankBody";

    [Header("Lazer Shot")]
    [Tooltip("Projectile prefab fired by the lazer shot.")]
    [SerializeField] private GameObject lazerProjectilePrefab;
    [Tooltip("Always-on projected lazer distance in tile units.")]
    [SerializeField] private float projectedLazerLength = 4f;
    [Tooltip("Speed of the fired lazer projectile.")]
    [SerializeField] private float lazerBulletSpeed = 18f;
    [Tooltip("Maximum travel distance for the fired lazer projectile in tile units.")]
    [SerializeField] private float lazerBulletMaxDistance = 4f;
    [Tooltip("Extra forward spawn distance from the muzzle.")]
    [SerializeField] private float extraSpawnDistance = 0.08f;
    [Tooltip("When enabled, projectile color matches the shooter's tank color.")]
    [SerializeField] private bool tintProjectilesWithShooterColor = true;

    private static LazerAbilityBehavior cachedBehavior;

    public float ProjectedLazerLength => projectedLazerLength;
    public float LazerBulletMaxDistance => lazerBulletMaxDistance;
    public float ExtraSpawnDistance => extraSpawnDistance;
    public GameObject LazerProjectilePrefab => lazerProjectilePrefab;

    public override AbilityActivationResult TryActivateServer(TankController owner, int shotSequence)
    {
        if (owner == null || !owner.IsServer || lazerProjectilePrefab == null)
        {
            return AbilityActivationResult.NotActivated;
        }

        if (!owner.TryComputeAbilityProjectileSpawn(
                lazerProjectilePrefab,
                extraSpawnDistance,
                out Vector2 spawnPosition2D,
                out Quaternion spawnRotation,
                out Vector2 fireDirection,
                out _))
        {
            return AbilityActivationResult.NotActivated;
        }

        if (!owner.TrySpawnAbilityProjectile(
                lazerProjectilePrefab,
                shotSequence,
                spawnPosition2D,
                spawnRotation,
                fireDirection,
                lazerBulletSpeed,
                out NetworkObject spawnedProjectile))
        {
            return AbilityActivationResult.NotActivated;
        }

        float effectiveTravelDistance = Mathf.Max(0f, lazerBulletMaxDistance);
        LazerProjectileBounce bounce = spawnedProjectile.gameObject.GetComponent<LazerProjectileBounce>();
        LazerProjectileSolidVisual solidVisual = spawnedProjectile.gameObject.GetComponent<LazerProjectileSolidVisual>();
        float beamLifetime = solidVisual != null ? Mathf.Max(0f, solidVisual.BeamLifetime) : 0.5f;

        if (bounce != null)
        {
            bounce.Configure(effectiveTravelDistance);
            bounce.SetDespawnDelayAfterPath(beamLifetime);
        }

        LazerProjectileRangeLimiter limiter = spawnedProjectile.gameObject.GetComponent<LazerProjectileRangeLimiter>();
        if (bounce != null)
        {
            if (limiter != null)
            {
                limiter.enabled = false;
            }
        }
        else
        {
            if (limiter == null)
            {
                limiter = spawnedProjectile.gameObject.AddComponent<LazerProjectileRangeLimiter>();
            }

            limiter.enabled = true;
            limiter.Configure(spawnPosition2D, effectiveTravelDistance);
        }

        Projectile projectile = spawnedProjectile.gameObject.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.maxWallBounces = -1;
            projectile.ConfigureSelfHitBehaviorServer(true);
            if (tintProjectilesWithShooterColor)
            {
                projectile.SetVisualColorServer(owner.tankColor.Value);
            }

            float travelTime = lazerBulletSpeed > 0.0001f
                ? effectiveTravelDistance / lazerBulletSpeed
                : 0f;
            float requiredLifetime = travelTime + beamLifetime + 0.1f;
            projectile.lifetime = Mathf.Max(projectile.lifetime, requiredLifetime);
        }

        TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
        if (abilityController != null)
        {
            abilityController.RegisterAbilityProjectileServer(spawnedProjectile);
        }

        return AbilityActivationResult.ActivatedConsume;
    }

    public static bool TryGetRuntimeConfig(
        out GameObject projectilePrefab,
        out float previewLength,
        out float projectileMaxDistance,
        out float spawnOffset)
    {
        projectilePrefab = null;
        previewLength = 0f;
        projectileMaxDistance = 0f;
        spawnOffset = 0f;

        if (!TryResolveBehavior(out LazerAbilityBehavior behavior) || behavior == null)
        {
            return false;
        }

        projectilePrefab = behavior.lazerProjectilePrefab;
        previewLength = behavior.projectedLazerLength;
        projectileMaxDistance = behavior.lazerBulletMaxDistance;
        spawnOffset = behavior.extraSpawnDistance;
        return projectilePrefab != null;
    }

    private static bool TryResolveBehavior(out LazerAbilityBehavior behavior)
    {
        if (cachedBehavior != null)
        {
            behavior = cachedBehavior;
            return true;
        }

        if (!AbilityRuntimeDatabase.TryGetById(LazerAbilityId, out AbilityDefinition definition) ||
            definition == null ||
            definition.Behavior == null)
        {
            behavior = null;
            return false;
        }

        cachedBehavior = definition.Behavior as LazerAbilityBehavior;
        behavior = cachedBehavior;
        return behavior != null;
    }

    private void OnValidate()
    {
        projectedLazerLength = Mathf.Max(0f, projectedLazerLength);
        lazerBulletSpeed = Mathf.Max(0f, lazerBulletSpeed);
        lazerBulletMaxDistance = Mathf.Max(0f, lazerBulletMaxDistance);
        extraSpawnDistance = Mathf.Max(0f, extraSpawnDistance);

#if UNITY_EDITOR
        if (lazerTankBodyPrefab != null)
        {
            string prefabAssetPath = AssetDatabase.GetAssetPath(lazerTankBodyPrefab);
            string marker = "/Resources/";
            int markerIndex = prefabAssetPath.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                string relativePath = prefabAssetPath.Substring(markerIndex + marker.Length);
                int extensionIndex = relativePath.LastIndexOf('.');
                lazerTankBodyPrefabResourcePath = extensionIndex >= 0
                    ? relativePath.Substring(0, extensionIndex)
                    : relativePath;
            }
        }
#endif
    }
}
