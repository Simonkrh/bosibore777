using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Behaviors/Homing Missile", fileName = "HomingMissileAbilityBehavior")]
public class HomingMissileAbilityBehavior : AbilityBehavior
{
    [SerializeField] private GameObject homingMissilePrefab;
    [SerializeField] private float missileSpeed = 8f;
    [SerializeField] private float extraSpawnDistance = 0f;
    [SerializeField] private float homingDelaySeconds = 3f;
    [SerializeField] private float targetRefreshIntervalSeconds = 0.2f;
    [SerializeField] private float turnRateDegreesPerSecond = 90f;

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
            missileSpeed);

        return true;
    }

    private void OnValidate()
    {
        missileSpeed = Mathf.Max(0f, missileSpeed);
        extraSpawnDistance = Mathf.Max(0f, extraSpawnDistance);
        homingDelaySeconds = Mathf.Max(0f, homingDelaySeconds);
        targetRefreshIntervalSeconds = Mathf.Max(0.02f, targetRefreshIntervalSeconds);
        turnRateDegreesPerSecond = Mathf.Max(0f, turnRateDegreesPerSecond);
    }
}
