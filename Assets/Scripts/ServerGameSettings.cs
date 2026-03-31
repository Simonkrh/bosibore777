using Unity.Netcode;
using UnityEngine;

public enum ServerSettingsFieldId
{
    MazeMinSize = 0,
    MazeMaxSize = 1,
    MazeWallRemovalPercent = 2,
    PlayerMoveSpeed = 3,
    PlayerProjectileSpeed = 4,
    PlayerShootCooldown = 5,
    AbilityInitialSpawnDelaySeconds = 6,
    AbilityMinSpawnIntervalSeconds = 7,
    AbilityMaxSpawnIntervalSeconds = 8,
    AbilityMaxActivePickups = 9,
    AbilityBlockedTileRadius = 10,
    BombSpawnPercent = 11,
    BombProjectileSpeed = 12,
    BombShardCount = 13,
    BombShardSpeed = 14,
    BombShardSpawnRadius = 15,
    MinigunSpawnPercent = 16,
    MinigunSpreadDegrees = 17,
    MinigunBulletCount = 18,
    MinigunFiringDurationSeconds = 19,
    MinigunBulletSpeed = 20,
    MinigunChargeUpSeconds = 21,
    MinigunClearAfterSeconds = 22,
    LazerSpawnPercent = 23,
    LazerProjectedLength = 24,
    LazerProjectileSpeed = 25,
    LazerMaxDistance = 26,
    HomingMissileSpawnPercent = 27,
    HomingMissileSpeed = 28,
    HomingMissileHomingDelaySeconds = 29,
    HomingMissileTargetRefreshIntervalSeconds = 30,
    HomingMissileTurnRateDegreesPerSecond = 31,
    HomingMissileCornerTurnRateMultiplier = 32,
    HomingMissileWobbleAmplitudeDegrees = 33,
    HomingMissileWobbleFrequencyHz = 34
}

public struct ServerGameSettingsState : INetworkSerializable
{
    public const float FixedMegaBombSpawnPercent = 1f;
    public const float ConfigurableAbilitySpawnPercentTotal = 99f;
    public const float ConfigurableAbilitySpawnShareTotal = 100f;
    private const int ConfigurableAbilityCount = 4;

    public int MazeMinSize;
    public int MazeMaxSize;
    public float MazeWallRemovalPercent;

    public float PlayerMoveSpeed;
    public float PlayerProjectileSpeed;
    public float PlayerShootCooldown;

    public float AbilityInitialSpawnDelaySeconds;
    public float AbilityMinSpawnIntervalSeconds;
    public float AbilityMaxSpawnIntervalSeconds;
    public int AbilityMaxActivePickups;
    public int AbilityBlockedTileRadius;

    public float BombSpawnPercent;
    public float BombProjectileSpeed;
    public int BombShardCount;
    public float BombShardSpeed;
    public float BombShardSpawnRadius;

    public float MinigunSpawnPercent;
    public float MinigunSpreadDegrees;
    public int MinigunBulletCount;
    public float MinigunFiringDurationSeconds;
    public float MinigunBulletSpeed;
    public float MinigunChargeUpSeconds;
    public float MinigunClearAfterSeconds;

    public float LazerSpawnPercent;
    public float LazerProjectedLength;
    public float LazerProjectileSpeed;
    public float LazerMaxDistance;

    public float HomingMissileSpawnPercent;
    public float HomingMissileSpeed;
    public float HomingMissileHomingDelaySeconds;
    public float HomingMissileTargetRefreshIntervalSeconds;
    public float HomingMissileTurnRateDegreesPerSecond;
    public float HomingMissileCornerTurnRateMultiplier;
    public float HomingMissileWobbleAmplitudeDegrees;
    public float HomingMissileWobbleFrequencyHz;

    public static ServerGameSettingsState CreateDefaults()
    {
        float defaultSpawnPercent = ConfigurableAbilitySpawnPercentTotal / ConfigurableAbilityCount;
        ServerGameSettingsState defaults = new ServerGameSettingsState
        {
            MazeMinSize = 4,
            MazeMaxSize = 12,
            MazeWallRemovalPercent = 0.2f,

            PlayerMoveSpeed = 1.8f,
            PlayerProjectileSpeed = 2f,
            PlayerShootCooldown = 0.5f,

            AbilityInitialSpawnDelaySeconds = 1f,
            AbilityMinSpawnIntervalSeconds = 2f,
            AbilityMaxSpawnIntervalSeconds = 4f,
            AbilityMaxActivePickups = 10,
            AbilityBlockedTileRadius = 1,

            BombSpawnPercent = defaultSpawnPercent,
            BombProjectileSpeed = 2f,
            BombShardCount = 40,
            BombShardSpeed = 2f,
            BombShardSpawnRadius = 0.04f,

            MinigunSpawnPercent = defaultSpawnPercent,
            MinigunSpreadDegrees = 8f,
            MinigunBulletCount = 20,
            MinigunFiringDurationSeconds = 2.5f,
            MinigunBulletSpeed = 2.4f,
            MinigunChargeUpSeconds = 0.57f,
            MinigunClearAfterSeconds = 2f,

            LazerSpawnPercent = defaultSpawnPercent,
            LazerProjectedLength = 4f,
            LazerProjectileSpeed = 30f,
            LazerMaxDistance = 12f,

            HomingMissileSpawnPercent = defaultSpawnPercent,
            HomingMissileSpeed = 1.9f,
            HomingMissileHomingDelaySeconds = 3f,
            HomingMissileTargetRefreshIntervalSeconds = 0.15f,
            HomingMissileTurnRateDegreesPerSecond = 270f,
            HomingMissileCornerTurnRateMultiplier = 0.5f,
            HomingMissileWobbleAmplitudeDegrees = 50f,
            HomingMissileWobbleFrequencyHz = 3f
        };
        defaults.Clamp();
        return defaults;
    }

    public void Clamp()
    {
        MazeMinSize = Mathf.Clamp(MazeMinSize, 2, 32);
        MazeMaxSize = Mathf.Clamp(MazeMaxSize, MazeMinSize, 32);
        MazeWallRemovalPercent = Mathf.Clamp01(MazeWallRemovalPercent);

        PlayerMoveSpeed = Mathf.Clamp(PlayerMoveSpeed, 0.1f, 12f);
        PlayerProjectileSpeed = Mathf.Clamp(PlayerProjectileSpeed, 0.1f, 20f);
        PlayerShootCooldown = Mathf.Clamp(PlayerShootCooldown, 0.05f, 5f);

        AbilityInitialSpawnDelaySeconds = Mathf.Clamp(AbilityInitialSpawnDelaySeconds, 0f, 20f);
        AbilityMinSpawnIntervalSeconds = Mathf.Clamp(AbilityMinSpawnIntervalSeconds, 0.1f, 20f);
        AbilityMaxSpawnIntervalSeconds = Mathf.Clamp(AbilityMaxSpawnIntervalSeconds, AbilityMinSpawnIntervalSeconds, 30f);
        AbilityMaxActivePickups = Mathf.Clamp(AbilityMaxActivePickups, 0, 32);
        AbilityBlockedTileRadius = Mathf.Clamp(AbilityBlockedTileRadius, 0, 8);

        BombProjectileSpeed = Mathf.Clamp(BombProjectileSpeed, 0f, 20f);
        BombShardCount = Mathf.Clamp(BombShardCount, 1, 128);
        BombShardSpeed = Mathf.Clamp(BombShardSpeed, 0f, 20f);
        BombShardSpawnRadius = Mathf.Clamp(BombShardSpawnRadius, 0f, 2f);

        MinigunSpreadDegrees = Mathf.Clamp(MinigunSpreadDegrees, 0f, 90f);
        MinigunBulletCount = Mathf.Clamp(MinigunBulletCount, 0, 200);
        MinigunFiringDurationSeconds = Mathf.Clamp(MinigunFiringDurationSeconds, 0f, 30f);
        MinigunBulletSpeed = Mathf.Clamp(MinigunBulletSpeed, 0f, 20f);
        MinigunChargeUpSeconds = Mathf.Clamp(MinigunChargeUpSeconds, 0f, 10f);
        MinigunClearAfterSeconds = Mathf.Clamp(MinigunClearAfterSeconds, 0f, 20f);

        LazerProjectedLength = Mathf.Clamp(LazerProjectedLength, 0f, 64f);
        LazerProjectileSpeed = Mathf.Clamp(LazerProjectileSpeed, 0.1f, 80f);
        LazerMaxDistance = Mathf.Clamp(LazerMaxDistance, 0f, 64f);

        HomingMissileSpeed = Mathf.Clamp(HomingMissileSpeed, 0f, 20f);
        HomingMissileHomingDelaySeconds = Mathf.Clamp(HomingMissileHomingDelaySeconds, 0f, 20f);
        HomingMissileTargetRefreshIntervalSeconds = Mathf.Clamp(HomingMissileTargetRefreshIntervalSeconds, 0.02f, 5f);
        HomingMissileTurnRateDegreesPerSecond = Mathf.Clamp(HomingMissileTurnRateDegreesPerSecond, 0f, 1440f);
        HomingMissileCornerTurnRateMultiplier = Mathf.Clamp(HomingMissileCornerTurnRateMultiplier, 0.05f, 1f);
        HomingMissileWobbleAmplitudeDegrees = Mathf.Clamp(HomingMissileWobbleAmplitudeDegrees, 0f, 180f);
        HomingMissileWobbleFrequencyHz = Mathf.Clamp(HomingMissileWobbleFrequencyHz, 0f, 20f);

        NormalizeConfigurableSpawnPercents();
    }

    public void AdjustField(ServerSettingsFieldId fieldId, float delta)
    {
        switch (fieldId)
        {
            case ServerSettingsFieldId.MazeMinSize:
                MazeMinSize += Mathf.RoundToInt(delta);
                break;
            case ServerSettingsFieldId.MazeMaxSize:
                MazeMaxSize += Mathf.RoundToInt(delta);
                break;
            case ServerSettingsFieldId.MazeWallRemovalPercent:
                MazeWallRemovalPercent += delta;
                break;
            case ServerSettingsFieldId.PlayerMoveSpeed:
                PlayerMoveSpeed += delta;
                break;
            case ServerSettingsFieldId.PlayerProjectileSpeed:
                PlayerProjectileSpeed += delta;
                break;
            case ServerSettingsFieldId.PlayerShootCooldown:
                PlayerShootCooldown += delta;
                break;
            case ServerSettingsFieldId.AbilityInitialSpawnDelaySeconds:
                AbilityInitialSpawnDelaySeconds += delta;
                break;
            case ServerSettingsFieldId.AbilityMinSpawnIntervalSeconds:
                AbilityMinSpawnIntervalSeconds += delta;
                break;
            case ServerSettingsFieldId.AbilityMaxSpawnIntervalSeconds:
                AbilityMaxSpawnIntervalSeconds += delta;
                break;
            case ServerSettingsFieldId.AbilityMaxActivePickups:
                AbilityMaxActivePickups += Mathf.RoundToInt(delta);
                break;
            case ServerSettingsFieldId.AbilityBlockedTileRadius:
                AbilityBlockedTileRadius += Mathf.RoundToInt(delta);
                break;
            case ServerSettingsFieldId.BombSpawnPercent:
                AdjustConfigurableSpawnPercent(ConfigurableAbility.Bomb, delta);
                break;
            case ServerSettingsFieldId.BombProjectileSpeed:
                BombProjectileSpeed += delta;
                break;
            case ServerSettingsFieldId.BombShardCount:
                BombShardCount += Mathf.RoundToInt(delta);
                break;
            case ServerSettingsFieldId.BombShardSpeed:
                BombShardSpeed += delta;
                break;
            case ServerSettingsFieldId.BombShardSpawnRadius:
                BombShardSpawnRadius += delta;
                break;
            case ServerSettingsFieldId.MinigunSpawnPercent:
                AdjustConfigurableSpawnPercent(ConfigurableAbility.Minigun, delta);
                break;
            case ServerSettingsFieldId.MinigunSpreadDegrees:
                MinigunSpreadDegrees += delta;
                break;
            case ServerSettingsFieldId.MinigunBulletCount:
                MinigunBulletCount += Mathf.RoundToInt(delta);
                break;
            case ServerSettingsFieldId.MinigunFiringDurationSeconds:
                MinigunFiringDurationSeconds += delta;
                break;
            case ServerSettingsFieldId.MinigunBulletSpeed:
                MinigunBulletSpeed += delta;
                break;
            case ServerSettingsFieldId.MinigunChargeUpSeconds:
                MinigunChargeUpSeconds += delta;
                break;
            case ServerSettingsFieldId.MinigunClearAfterSeconds:
                MinigunClearAfterSeconds += delta;
                break;
            case ServerSettingsFieldId.LazerSpawnPercent:
                AdjustConfigurableSpawnPercent(ConfigurableAbility.Lazer, delta);
                break;
            case ServerSettingsFieldId.LazerProjectedLength:
                LazerProjectedLength += delta;
                break;
            case ServerSettingsFieldId.LazerProjectileSpeed:
                LazerProjectileSpeed += delta;
                break;
            case ServerSettingsFieldId.LazerMaxDistance:
                LazerMaxDistance += delta;
                break;
            case ServerSettingsFieldId.HomingMissileSpawnPercent:
                AdjustConfigurableSpawnPercent(ConfigurableAbility.HomingMissile, delta);
                break;
            case ServerSettingsFieldId.HomingMissileSpeed:
                HomingMissileSpeed += delta;
                break;
            case ServerSettingsFieldId.HomingMissileHomingDelaySeconds:
                HomingMissileHomingDelaySeconds += delta;
                break;
            case ServerSettingsFieldId.HomingMissileTargetRefreshIntervalSeconds:
                HomingMissileTargetRefreshIntervalSeconds += delta;
                break;
            case ServerSettingsFieldId.HomingMissileTurnRateDegreesPerSecond:
                HomingMissileTurnRateDegreesPerSecond += delta;
                break;
            case ServerSettingsFieldId.HomingMissileCornerTurnRateMultiplier:
                HomingMissileCornerTurnRateMultiplier += delta;
                break;
            case ServerSettingsFieldId.HomingMissileWobbleAmplitudeDegrees:
                HomingMissileWobbleAmplitudeDegrees += delta;
                break;
            case ServerSettingsFieldId.HomingMissileWobbleFrequencyHz:
                HomingMissileWobbleFrequencyHz += delta;
                break;
        }

        Clamp();
    }

    public bool TryGetConfiguredAbilitySpawnPercent(string abilityId, out float spawnPercent)
    {
        switch (abilityId)
        {
            case BombAbilityId:
                spawnPercent = BombSpawnPercent;
                return true;
            case MinigunAbilityId:
                spawnPercent = MinigunSpawnPercent;
                return true;
            case LazerAbilityId:
                spawnPercent = LazerSpawnPercent;
                return true;
            case HomingMissileAbilityId:
                spawnPercent = HomingMissileSpawnPercent;
                return true;
            case MegaBombAbilityId:
                spawnPercent = FixedMegaBombSpawnPercent;
                return true;
            default:
                spawnPercent = 0f;
                return false;
        }
    }

    public static float ConfigurableSpawnPercentToSharePercent(float spawnPercent)
    {
        if (ConfigurableAbilitySpawnPercentTotal <= 0.0001f)
        {
            return 0f;
        }

        return Mathf.Max(0f, spawnPercent) * (ConfigurableAbilitySpawnShareTotal / ConfigurableAbilitySpawnPercentTotal);
    }

    public static float SpawnShareDeltaToConfigurableSpawnPercentDelta(float shareDelta)
    {
        return shareDelta * (ConfigurableAbilitySpawnPercentTotal / ConfigurableAbilitySpawnShareTotal);
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref MazeMinSize);
        serializer.SerializeValue(ref MazeMaxSize);
        serializer.SerializeValue(ref MazeWallRemovalPercent);

        serializer.SerializeValue(ref PlayerMoveSpeed);
        serializer.SerializeValue(ref PlayerProjectileSpeed);
        serializer.SerializeValue(ref PlayerShootCooldown);

        serializer.SerializeValue(ref AbilityInitialSpawnDelaySeconds);
        serializer.SerializeValue(ref AbilityMinSpawnIntervalSeconds);
        serializer.SerializeValue(ref AbilityMaxSpawnIntervalSeconds);
        serializer.SerializeValue(ref AbilityMaxActivePickups);
        serializer.SerializeValue(ref AbilityBlockedTileRadius);

        serializer.SerializeValue(ref BombSpawnPercent);
        serializer.SerializeValue(ref BombProjectileSpeed);
        serializer.SerializeValue(ref BombShardCount);
        serializer.SerializeValue(ref BombShardSpeed);
        serializer.SerializeValue(ref BombShardSpawnRadius);

        serializer.SerializeValue(ref MinigunSpawnPercent);
        serializer.SerializeValue(ref MinigunSpreadDegrees);
        serializer.SerializeValue(ref MinigunBulletCount);
        serializer.SerializeValue(ref MinigunFiringDurationSeconds);
        serializer.SerializeValue(ref MinigunBulletSpeed);
        serializer.SerializeValue(ref MinigunChargeUpSeconds);
        serializer.SerializeValue(ref MinigunClearAfterSeconds);

        serializer.SerializeValue(ref LazerSpawnPercent);
        serializer.SerializeValue(ref LazerProjectedLength);
        serializer.SerializeValue(ref LazerProjectileSpeed);
        serializer.SerializeValue(ref LazerMaxDistance);

        serializer.SerializeValue(ref HomingMissileSpawnPercent);
        serializer.SerializeValue(ref HomingMissileSpeed);
        serializer.SerializeValue(ref HomingMissileHomingDelaySeconds);
        serializer.SerializeValue(ref HomingMissileTargetRefreshIntervalSeconds);
        serializer.SerializeValue(ref HomingMissileTurnRateDegreesPerSecond);
        serializer.SerializeValue(ref HomingMissileCornerTurnRateMultiplier);
        serializer.SerializeValue(ref HomingMissileWobbleAmplitudeDegrees);
        serializer.SerializeValue(ref HomingMissileWobbleFrequencyHz);
    }

    private void NormalizeConfigurableSpawnPercents()
    {
        float total = BombSpawnPercent + MinigunSpawnPercent + LazerSpawnPercent + HomingMissileSpawnPercent;
        if (total <= 0.0001f)
        {
            float evenPercent = ConfigurableAbilitySpawnPercentTotal / ConfigurableAbilityCount;
            BombSpawnPercent = evenPercent;
            MinigunSpawnPercent = evenPercent;
            LazerSpawnPercent = evenPercent;
            HomingMissileSpawnPercent = evenPercent;
            return;
        }

        float scale = ConfigurableAbilitySpawnPercentTotal / total;
        BombSpawnPercent = Mathf.Max(0f, BombSpawnPercent * scale);
        MinigunSpawnPercent = Mathf.Max(0f, MinigunSpawnPercent * scale);
        LazerSpawnPercent = Mathf.Max(0f, LazerSpawnPercent * scale);
        HomingMissileSpawnPercent = Mathf.Max(0f, HomingMissileSpawnPercent * scale);
    }

    private void AdjustConfigurableSpawnPercent(ConfigurableAbility targetAbility, float delta)
    {
        float[] values =
        {
            BombSpawnPercent,
            MinigunSpawnPercent,
            LazerSpawnPercent,
            HomingMissileSpawnPercent
        };

        int targetIndex = (int)targetAbility;
        float currentValue = Mathf.Max(0f, values[targetIndex]);
        float desiredValue = Mathf.Clamp(currentValue + delta, 0f, ConfigurableAbilitySpawnPercentTotal);
        float appliedDelta = desiredValue - currentValue;
        if (Mathf.Abs(appliedDelta) <= 0.0001f)
        {
            return;
        }

        values[targetIndex] = desiredValue;
        RedistributeOtherSpawnPercents(values, targetIndex, -appliedDelta);

        BombSpawnPercent = values[0];
        MinigunSpawnPercent = values[1];
        LazerSpawnPercent = values[2];
        HomingMissileSpawnPercent = values[3];
    }

    private static void RedistributeOtherSpawnPercents(float[] values, int targetIndex, float deltaForOthers)
    {
        if (values == null || values.Length < ConfigurableAbilityCount || Mathf.Abs(deltaForOthers) <= 0.0001f)
        {
            return;
        }

        float otherTotal = 0f;
        int otherCount = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (i == targetIndex)
            {
                continue;
            }

            values[i] = Mathf.Max(0f, values[i]);
            otherTotal += values[i];
            otherCount++;
        }

        if (otherCount <= 0)
        {
            return;
        }

        if (otherTotal <= 0.0001f)
        {
            float evenDelta = deltaForOthers / otherCount;
            for (int i = 0; i < values.Length; i++)
            {
                if (i == targetIndex)
                {
                    continue;
                }

                values[i] = Mathf.Max(0f, values[i] + evenDelta);
            }

            return;
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (i == targetIndex)
            {
                continue;
            }

            float ratio = values[i] / otherTotal;
            values[i] = Mathf.Max(0f, values[i] + deltaForOthers * ratio);
        }
    }

    private enum ConfigurableAbility
    {
        Bomb = 0,
        Minigun = 1,
        Lazer = 2,
        HomingMissile = 3
    }

    private const string BombAbilityId = "ability.bomb";
    private const string MinigunAbilityId = "ability.minigun";
    private const string LazerAbilityId = "ability.lazer";
    private const string HomingMissileAbilityId = "ability.homing_missile";
    private const string MegaBombAbilityId = "ability.megabomb";
}
