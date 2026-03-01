using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Behaviors/Homing Missile", fileName = "HomingMissileAbilityBehavior")]
public class HomingMissileAbilityBehavior : AbilityBehavior
{
    [Header("Spawn")]
    [Tooltip("Projectile prefab to spawn for this ability.")]
    [SerializeField] private GameObject homingMissilePrefab;
    [Tooltip("Base launch speed. Higher = faster missile travel. Lower = slower travel.")]
    [SerializeField] private float missileSpeed = 1.9f;
    [Tooltip("Extra distance from muzzle before spawn. Higher = starts farther forward. Lower = starts closer to shooter.")]
    [SerializeField] private float extraSpawnDistance = 0.15f;

    [Header("Homing")]
    [Tooltip("Time before homing starts. Higher = flies straight longer first. Lower = starts tracking sooner.")]
    [SerializeField] private float homingDelaySeconds = 3f;
    [Tooltip("How often target/path is refreshed. Higher = fewer updates (smoother but slower reactions). Lower = faster reactions (more twitchy).")]
    [SerializeField] private float targetRefreshIntervalSeconds = 0.2f;
    [Tooltip("Maximum steering turn rate. Higher = can turn tighter/faster. Lower = wider turns, more 'ice' feel.")]
    [SerializeField] private float turnRateDegreesPerSecond = 220f;
    [Tooltip("Turn-rate scale applied when a corner is immediately ahead (0..1). Lower = wider/slower corner turns. 1 = no extra corner widening.")]
    [SerializeField] private float cornerTurnRateMultiplier = 0.75f;

    [Header("Steering Wobble")]
    [Tooltip("Maximum wobble steering offset in degrees. Higher = larger side-to-side sway. Lower = steadier steering.")]
    [SerializeField] private float wobbleAmplitudeDegrees = 60f;
    [Tooltip("Wobble oscillation frequency. Higher = faster wiggle. Lower = slower, heavier wiggle.")]
    [SerializeField] private float wobbleFrequencyHz = 1f;
    [Tooltip("Minimum wobble intensity (0..1). Higher = never fully straightens. Lower = can stabilize more.")]
    [SerializeField] private float wobbleBaselineStrength = 0.25f;
    [Tooltip("How quickly wobble grows during hard turns. Higher = wobble ramps up quickly. Lower = wobble builds slowly.")]
    [SerializeField] private float wobbleBuildUpPerSecond = 1.5f;
    [Tooltip("How quickly wobble fades when turn demand is low. Higher = settles faster. Lower = wobble lingers longer.")]
    [SerializeField] private float wobbleDecayPerSecond = 5f;

    [Header("Path Wall Bias")]
    [Tooltip("How many nodes ahead can be considered on straight corridors. Higher = more forward-looking. Lower = more local tracking.")]
    [SerializeField] private int pathLookaheadNodes = 3;
    [Tooltip("Sideways wall-hug offset from path center. Higher = hugs wall harder. Lower = stays closer to center.")]
    [SerializeField] private float wallHugOffset = 0.4f;
    [Tooltip("Safety radius used when validating wall-hug points. Higher = keeps more clearance from walls. Lower = allows tighter hugging.")]
    [SerializeField] private float wallHugProbeRadius = 0.05f;
    [Tooltip("Probe radius for line-of-sight tests. Higher = more conservative LOS checks. Lower = more permissive/aggressive LOS.")]
    [SerializeField] private float lineOfSightProbeRadius = 0.04f;

    public override bool TryActivateServer(TankController owner, int shotSequence)
    {
        if (owner == null || homingMissilePrefab == null || !owner.IsServer)
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
                homingMissilePrefab,
                shotSequence,
                spawnPosition2D,
                spawnRotation,
                fireDirection,
                missileSpeed,
                out NetworkObject spawnedProjectile))
        {
            return false;
        }

        HomingMissileGuidance guidance = spawnedProjectile.gameObject.GetComponent<HomingMissileGuidance>();
        if (guidance == null)
        {
            guidance = spawnedProjectile.gameObject.AddComponent<HomingMissileGuidance>();
        }

        guidance.Configure(
            homingDelaySeconds,
            targetRefreshIntervalSeconds,
            turnRateDegreesPerSecond,
            cornerTurnRateMultiplier,
            missileSpeed,
            wobbleAmplitudeDegrees,
            wobbleFrequencyHz,
            wobbleBaselineStrength,
            wobbleBuildUpPerSecond,
            wobbleDecayPerSecond,
            pathLookaheadNodes,
            wallHugOffset,
            wallHugProbeRadius,
            lineOfSightProbeRadius);

        return true;
    }

    private void OnValidate()
    {
        missileSpeed = Mathf.Max(0f, missileSpeed);
        extraSpawnDistance = Mathf.Max(0f, extraSpawnDistance);
        homingDelaySeconds = Mathf.Max(0f, homingDelaySeconds);
        targetRefreshIntervalSeconds = Mathf.Max(0.02f, targetRefreshIntervalSeconds);
        turnRateDegreesPerSecond = Mathf.Max(0f, turnRateDegreesPerSecond);
        cornerTurnRateMultiplier = Mathf.Clamp(cornerTurnRateMultiplier, 0.05f, 1f);
        wobbleAmplitudeDegrees = Mathf.Max(0f, wobbleAmplitudeDegrees);
        wobbleFrequencyHz = Mathf.Max(0f, wobbleFrequencyHz);
        wobbleBaselineStrength = Mathf.Clamp01(wobbleBaselineStrength);
        wobbleBuildUpPerSecond = Mathf.Max(0f, wobbleBuildUpPerSecond);
        wobbleDecayPerSecond = Mathf.Max(0f, wobbleDecayPerSecond);
        pathLookaheadNodes = Mathf.Max(1, pathLookaheadNodes);
        wallHugOffset = Mathf.Max(0f, wallHugOffset);
        wallHugProbeRadius = Mathf.Max(0f, wallHugProbeRadius);
        lineOfSightProbeRadius = Mathf.Max(0f, lineOfSightProbeRadius);
    }
}
