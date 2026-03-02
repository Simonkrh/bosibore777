using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Abilities/Behaviors/Homing Missile", fileName = "HomingMissileAbilityBehavior")]
public class HomingMissileAbilityBehavior : AbilityBehavior
{
    [Header("Tank Visuals")]
    [Tooltip("Sprite shown on the shooter's tank after firing, until the missile despawns. Must be inside a Resources folder.")]
    [SerializeField] private Sprite firedTankSprite;
    [SerializeField, HideInInspector] private string firedTankSpriteResourcePath = "Sprites/Tanks/HomingMissileFiredTank";

    [Header("Spawn")]
    [Tooltip("Projectile prefab to spawn for this ability.")]
    [SerializeField] private GameObject homingMissilePrefab;
    [Tooltip("Base launch speed. Higher = faster missile travel. Lower = slower travel.")]
    [SerializeField] private float missileSpeed = 1.9f;
    [Tooltip("Extra distance from muzzle before spawn. Higher = starts farther forward. Lower = starts closer to shooter.")]
    [SerializeField] private float extraSpawnDistance = 0.1f;

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

    [Header("Adaptive Navigation")]
    [Tooltip("Path distance in tiles considered close-range chase. Lower = missile stays in aggressive mode only when very close.")]
    [SerializeField] private float closeRangeTiles = 2f;
    [Tooltip("Path distance in tiles considered long-range navigation. Higher = missile stays cautious for longer.")]
    [SerializeField] private float farRangeTiles = 8f;
    [Tooltip("Wobble strength multiplier at long range (0..1). Lower = steadier and less wall-prone far from target.")]
    [SerializeField] private float farRangeWobbleMultiplier = 0.35f;
    [Tooltip("Wall-hug strength multiplier at long range (0..1). Lower = keeps to safer corridor centerlines at distance.")]
    [SerializeField] private float farRangeWallHugMultiplier = 0.1f;
    [Tooltip("Turn-rate scale at long range. Higher = corners are handled more decisively when far.")]
    [SerializeField] private float farRangeTurnRateMultiplier = 1.15f;
    [Tooltip("Corner turn-rate multiplier at long range (0..1). 1 means no extra corner widening when far.")]
    [SerializeField] private float farRangeCornerTurnRateMultiplier = 1f;
    [Tooltip("Line-of-sight probe scale at long range. Higher = more conservative wall clearance when far.")]
    [SerializeField] private float farRangeLineOfSightProbeMultiplier = 1.5f;
    private readonly Dictionary<ulong, NetworkObject> activeMissilesByOwner = new Dictionary<ulong, NetworkObject>();

    public override AbilityActivationResult TryActivateServer(TankController owner, int shotSequence)
    {
        if (owner == null || homingMissilePrefab == null || !owner.IsServer)
        {
            return AbilityActivationResult.NotActivated;
        }

        ulong ownerClientId = owner.OwnerClientId;
        if (TryGetActiveMissile(ownerClientId, out _))
        {
            return AbilityActivationResult.ActivatedKeep;
        }

        if (!owner.TryComputeAbilityProjectileSpawn(
                extraSpawnDistance,
                out Vector2 spawnPosition2D,
                out Quaternion spawnRotation,
                out Vector2 fireDirection,
                out _))
        {
            return AbilityActivationResult.NotActivated;
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
            return AbilityActivationResult.NotActivated;
        }

        Projectile projectile = spawnedProjectile.gameObject.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.SetVisualColorServer(owner.tankColor.Value);
        }

        TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
        if (abilityController != null)
        {
            abilityController.RegisterAbilityProjectileServer(spawnedProjectile);
            abilityController.SetModelOverrideSpriteResourceServer(firedTankSpriteResourcePath);
        }

        Projectile missileProjectile = spawnedProjectile.gameObject.GetComponent<Projectile>();
        if (missileProjectile != null)
        {
            missileProjectile.SetPreDestroyServerCallback((projectileInstance, _) =>
                HandleMissilePreDestroyServer(owner, ownerClientId, projectileInstance));
        }
        activeMissilesByOwner[ownerClientId] = spawnedProjectile;

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
            lineOfSightProbeRadius,
            closeRangeTiles,
            farRangeTiles,
            farRangeWobbleMultiplier,
            farRangeWallHugMultiplier,
            farRangeTurnRateMultiplier,
            farRangeCornerTurnRateMultiplier,
            farRangeLineOfSightProbeMultiplier);

        return AbilityActivationResult.ActivatedKeep;
    }

    private bool TryGetActiveMissile(ulong ownerClientId, out NetworkObject activeMissile)
    {
        activeMissile = null;
        if (!activeMissilesByOwner.TryGetValue(ownerClientId, out NetworkObject trackedMissile))
        {
            return false;
        }

        if (trackedMissile == null || !trackedMissile.IsSpawned)
        {
            activeMissilesByOwner.Remove(ownerClientId);
            return false;
        }

        activeMissile = trackedMissile;
        return true;
    }

    private void HandleMissilePreDestroyServer(TankController owner, ulong ownerClientId, Projectile projectile)
    {
        if (owner == null || projectile == null)
        {
            return;
        }

        if (!TryGetActiveMissile(ownerClientId, out NetworkObject activeMissile))
        {
            return;
        }

        if (activeMissile == null || activeMissile.gameObject != projectile.gameObject)
        {
            return;
        }

        activeMissilesByOwner.Remove(ownerClientId);

        TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
        if (abilityController != null)
        {
            abilityController.ClearEquippedAbilityServer();
        }
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
        closeRangeTiles = Mathf.Max(0f, closeRangeTiles);
        farRangeTiles = Mathf.Max(closeRangeTiles + 0.01f, farRangeTiles);
        farRangeWobbleMultiplier = Mathf.Clamp01(farRangeWobbleMultiplier);
        farRangeWallHugMultiplier = Mathf.Clamp01(farRangeWallHugMultiplier);
        farRangeTurnRateMultiplier = Mathf.Max(0.05f, farRangeTurnRateMultiplier);
        farRangeCornerTurnRateMultiplier = Mathf.Clamp(farRangeCornerTurnRateMultiplier, 0.05f, 1f);
        farRangeLineOfSightProbeMultiplier = Mathf.Max(0.05f, farRangeLineOfSightProbeMultiplier);

#if UNITY_EDITOR
        if (firedTankSprite != null)
        {
            string spriteAssetPath = AssetDatabase.GetAssetPath(firedTankSprite);
            string marker = "/Resources/";
            int markerIndex = spriteAssetPath.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                string relativePath = spriteAssetPath.Substring(markerIndex + marker.Length);
                int extensionIndex = relativePath.LastIndexOf('.');
                firedTankSpriteResourcePath = extensionIndex >= 0
                    ? relativePath.Substring(0, extensionIndex)
                    : relativePath;
            }
        }
#endif
    }

    private void OnDisable()
    {
        activeMissilesByOwner.Clear();
    }
}
