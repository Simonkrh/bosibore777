using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Abilities/Behaviors/Lazer", fileName = "LazerAbilityBehavior")]
public class LazerAbilityBehavior : AbilityBehavior
{
    public const string LazerAbilityId = "ability.lazer";

    public struct PreviewVisualSettings
    {
        public float BeamWidth;
        public float BeamAlpha;
        public float FlickerRefreshRate;
        public int SortingOrder;
        public int ReflectionSafetyLimit;
        public float MinRectLength;
        public float MaxRectLength;
        public float MinGapLength;
        public float MaxGapLength;
        public int MaxRectsPerFrame;
        public Sprite RectSprite;
    }

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

    [Header("Projected Lazer Preview")]
    [SerializeField] private float previewBeamWidth = 0.055f;
    [SerializeField] private float previewBeamAlpha = 0.65f;
    [SerializeField] private float previewFlickerRefreshRate = 30f;
    [SerializeField] private int previewSortingOrder = 120;
    [SerializeField] private int previewReflectionSafetyLimit = 24;
    [SerializeField] private float previewMinRectLength = 0.04f;
    [SerializeField] private float previewMaxRectLength = 0.35f;
    [SerializeField] private float previewMinGapLength = 0.01f;
    [SerializeField] private float previewMaxGapLength = 0.14f;
    [SerializeField] private int previewMaxRectsPerFrame = 200;
    [SerializeField] private Sprite previewRectSprite;

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
                Projectile.AudioProfile.Lazer,
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

        owner.ResolveGameManager()?.PlayLazerShootSoundServer(spawnedProjectile.transform.position);

        return AbilityActivationResult.ActivatedConsume;
    }

    public static bool TryGetRuntimeConfig(
        out GameObject projectilePrefab,
        out float previewLength,
        out float projectileMaxDistance,
        out float spawnOffset,
        out PreviewVisualSettings previewVisualSettings)
    {
        projectilePrefab = null;
        previewLength = 0f;
        projectileMaxDistance = 0f;
        spawnOffset = 0f;
        previewVisualSettings = default;

        if (!TryResolveBehavior(out LazerAbilityBehavior behavior) || behavior == null)
        {
            return false;
        }

        projectilePrefab = behavior.lazerProjectilePrefab;
        previewLength = behavior.projectedLazerLength;
        projectileMaxDistance = behavior.lazerBulletMaxDistance;
        spawnOffset = behavior.extraSpawnDistance;
        previewVisualSettings = new PreviewVisualSettings
        {
            BeamWidth = behavior.previewBeamWidth,
            BeamAlpha = behavior.previewBeamAlpha,
            FlickerRefreshRate = behavior.previewFlickerRefreshRate,
            SortingOrder = behavior.previewSortingOrder,
            ReflectionSafetyLimit = behavior.previewReflectionSafetyLimit,
            MinRectLength = behavior.previewMinRectLength,
            MaxRectLength = behavior.previewMaxRectLength,
            MinGapLength = behavior.previewMinGapLength,
            MaxGapLength = behavior.previewMaxGapLength,
            MaxRectsPerFrame = behavior.previewMaxRectsPerFrame,
            RectSprite = behavior.previewRectSprite
        };
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
        previewBeamWidth = Mathf.Max(0.001f, previewBeamWidth);
        previewBeamAlpha = Mathf.Clamp01(previewBeamAlpha);
        previewFlickerRefreshRate = Mathf.Clamp(previewFlickerRefreshRate, 0.1f, 240f);
        previewReflectionSafetyLimit = Mathf.Max(0, previewReflectionSafetyLimit);
        previewMinRectLength = Mathf.Max(0.001f, previewMinRectLength);
        previewMaxRectLength = Mathf.Max(previewMinRectLength, previewMaxRectLength);
        previewMinGapLength = Mathf.Max(0f, previewMinGapLength);
        previewMaxGapLength = Mathf.Max(previewMinGapLength, previewMaxGapLength);
        previewMaxRectsPerFrame = Mathf.Clamp(previewMaxRectsPerFrame, 1, 1024);
        previewSortingOrder = Mathf.Clamp(previewSortingOrder, -32768, 32767);

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
