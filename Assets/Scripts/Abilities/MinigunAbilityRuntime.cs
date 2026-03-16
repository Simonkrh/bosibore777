using System;
using Unity.Netcode;
using UnityEngine;

public class MinigunAbilityRuntime : MonoBehaviour
{
    private enum RuntimeState
    {
        Idle = 0,
        Charging = 1,
        Firing = 2,
        WaitingToClear = 3
    }

    private TankController owner;
    private GameObject projectilePrefab;
    private float spreadDegrees;
    private int bulletCount;
    private float firingDurationSeconds;
    private float bulletSpeed;
    private float chargeUpSeconds;
    private float clearDelaySeconds;
    private float extraSpawnDistance;
    private int shotSequenceStride;
    private bool tintProjectilesWithShooterColor;
    private Action<TankController> completedCallback;

    private RuntimeState state;
    private bool releasedDuringCharge;
    private int shotSequenceBase;
    private int bulletsFired;
    private float chargeStartTime;
    private float fireStartTime;
    private float nextBulletTime;
    private float clearAtTime;
    private float perBulletIntervalSeconds;

    public bool IsRunning => state != RuntimeState.Idle;

    public void Configure(
        TankController ownerTank,
        GameObject projectilePrefabToSpawn,
        float spreadAngleDegrees,
        int totalBullets,
        float fireDurationSeconds,
        float projectileSpeed,
        float chargeDurationSeconds,
        float cleanupDelaySeconds,
        float spawnDistanceOffset,
        int sequenceStride,
        bool tintProjectiles,
        Action<TankController> onCompleted)
    {
        owner = ownerTank;
        projectilePrefab = projectilePrefabToSpawn;
        spreadDegrees = Mathf.Max(0f, spreadAngleDegrees);
        bulletCount = Mathf.Max(0, totalBullets);
        firingDurationSeconds = Mathf.Max(0f, fireDurationSeconds);
        bulletSpeed = Mathf.Max(0f, projectileSpeed);
        chargeUpSeconds = Mathf.Max(0f, chargeDurationSeconds);
        clearDelaySeconds = Mathf.Max(0f, cleanupDelaySeconds);
        extraSpawnDistance = Mathf.Max(0f, spawnDistanceOffset);
        shotSequenceStride = Mathf.Max(1, sequenceStride);
        tintProjectilesWithShooterColor = tintProjectiles;
        completedCallback = onCompleted;
    }

    public bool BeginCharge(int initialShotSequence)
    {
        if (owner == null || !owner.IsServer || !owner.IsSpawned || projectilePrefab == null)
        {
            return false;
        }

        shotSequenceBase = initialShotSequence;
        bulletsFired = 0;
        releasedDuringCharge = false;
        chargeStartTime = Time.time;
        fireStartTime = 0f;
        nextBulletTime = float.PositiveInfinity;
        clearAtTime = float.PositiveInfinity;
        perBulletIntervalSeconds = 0f;
        state = RuntimeState.Charging;
        return true;
    }

    public void NotifyInputReleased()
    {
        if (state == RuntimeState.Idle)
        {
            return;
        }

        if (state == RuntimeState.WaitingToClear)
        {
            return;
        }

        if (state == RuntimeState.Charging)
        {
            releasedDuringCharge = true;
        }

        BeginClearCountdown(Time.time);
    }

    private void Update()
    {
        if (owner == null || !owner.IsServer || !owner.IsSpawned)
        {
            if (state != RuntimeState.Idle)
            {
                ResetRuntime();
            }

            return;
        }

        float now = Time.time;
        switch (state)
        {
            case RuntimeState.Charging:
            {
                if (now - chargeStartTime < chargeUpSeconds)
                {
                    return;
                }

                if (releasedDuringCharge)
                {
                    BeginClearCountdown(now);
                    return;
                }

                StartFiring(now);
                return;
            }
            case RuntimeState.Firing:
            {
                FireDueBullets(now);
                return;
            }
            case RuntimeState.WaitingToClear:
            {
                if (now >= clearAtTime)
                {
                    CompleteAndReset();
                }

                return;
            }
            default:
            {
                return;
            }
        }
    }

    private void StartFiring(float now)
    {
        if (bulletCount <= 0)
        {
            BeginClearCountdown(now);
            return;
        }

        fireStartTime = now;
        nextBulletTime = now;
        perBulletIntervalSeconds = bulletCount > 1
            ? firingDurationSeconds / (bulletCount - 1)
            : 0f;
        state = RuntimeState.Firing;
    }

    private void FireDueBullets(float now)
    {
        while (bulletsFired < bulletCount && now >= nextBulletTime)
        {
            SpawnBullet(bulletsFired);
            bulletsFired++;

            if (bulletsFired >= bulletCount)
            {
                break;
            }

            nextBulletTime = fireStartTime + bulletsFired * perBulletIntervalSeconds;
        }

        if (bulletsFired >= bulletCount)
        {
            BeginClearCountdown(now);
        }
    }

    private void SpawnBullet(int bulletIndex)
    {
        if (projectilePrefab == null || owner == null || !owner.IsServer || !owner.IsSpawned)
        {
            return;
        }

        if (!owner.TryComputeAbilityProjectileSpawn(
                extraSpawnDistance,
                out Vector2 spawnPosition2D,
                out _,
                out Vector2 fireDirection,
                out _))
        {
            return;
        }

        float spreadOffset = UnityEngine.Random.Range(-spreadDegrees, spreadDegrees);
        Vector2 spreadDirection = (Quaternion.Euler(0f, 0f, spreadOffset) * fireDirection).normalized;
        Quaternion spawnRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(spreadDirection.y, spreadDirection.x) * Mathf.Rad2Deg - 90f);
        int shotSequence = unchecked(shotSequenceBase * shotSequenceStride + bulletIndex);

        if (!owner.TrySpawnAbilityProjectile(
                projectilePrefab,
                shotSequence,
                spawnPosition2D,
                spawnRotation,
                spreadDirection,
                bulletSpeed,
                out NetworkObject spawnedProjectile))
        {
            return;
        }

        if (!tintProjectilesWithShooterColor || spawnedProjectile == null)
        {
            return;
        }

        Projectile projectile = spawnedProjectile.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.SetVisualColorServer(owner.tankColor.Value);
        }
    }

    private void BeginClearCountdown(float now)
    {
        state = RuntimeState.WaitingToClear;
        clearAtTime = now + clearDelaySeconds;
    }

    private void CompleteAndReset()
    {
        Action<TankController> callback = completedCallback;
        TankController ownerTank = owner;
        ResetRuntime();
        callback?.Invoke(ownerTank);
    }

    private void ResetRuntime()
    {
        state = RuntimeState.Idle;
        releasedDuringCharge = false;
        shotSequenceBase = 0;
        bulletsFired = 0;
        chargeStartTime = 0f;
        fireStartTime = 0f;
        nextBulletTime = float.PositiveInfinity;
        clearAtTime = float.PositiveInfinity;
        perBulletIntervalSeconds = 0f;
        completedCallback = null;
    }
}
