using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class MissileTrailSmoke : NetworkBehaviour
{
    private static readonly Color DefaultUntargetedSmokeColor = new Color(0.12f, 0.12f, 0.12f, 1f);

    [Header("Smoke Sprite")]
    [Tooltip("Sprite used for each smoke puff burst.")]
    [SerializeField] private Sprite smokeSprite;
    [Tooltip("Sorting layer used by the smoke bursts.")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Sorting order used by the smoke bursts.")]
    [SerializeField] private int sortingOrder = 55;

    [Header("Spawn")]
    [Tooltip("Seconds between smoke burst spawns.")]
    [SerializeField] private float spawnIntervalSeconds = 0.06f;
    [Tooltip("How far behind the missile each smoke burst is spawned.")]
    [SerializeField] private float backwardOffset = 0.22f;
    [Tooltip("Alpha applied to all trail smoke circles.")]
    [SerializeField] private float smokeAlpha = 0.55f;

    [Header("Color")]
    [Tooltip("Smoke color used before the missile has a target.")]
    [SerializeField] private Color untargetedSmokeColor = DefaultUntargetedSmokeColor;
    [Tooltip("When targeting, chance that each spawned circle uses the target color. The rest use the untargeted smoke color.")]
    [SerializeField, Range(0f, 1f)] private float targetColorWeight = 0.7f;

    [Header("Burst")]
    [Tooltip("How many circles each trail burst uses.")]
    [SerializeField] private int circleCount = 4;
    [Tooltip("Lifetime range for each smoke circle.")]
    [SerializeField] private Vector2 lifetimeRange = new Vector2(0.2f, 0.45f);
    [Tooltip("Starting size range for each smoke circle.")]
    [SerializeField] private Vector2 startScaleRange = new Vector2(0.03f, 0.06f);
    [Tooltip("Growth multiplier range for each smoke circle.")]
    [SerializeField] private Vector2 endScaleMultiplierRange = new Vector2(1.8f, 2.8f);
    [Tooltip("Random opacity multiplier range for each smoke circle.")]
    [SerializeField] private Vector2 startOpacityMultiplierRange = new Vector2(0.45f, 0.8f);

    [Header("Motion")]
    [Tooltip("If enabled, each trail burst spreads in all directions.")]
    [SerializeField] private bool spreadInAllDirections = true;
    [Tooltip("Directional drift when radial spread is disabled.")]
    [SerializeField] private Vector2 baseDirection = Vector2.zero;
    [Tooltip("Direction variation in degrees when not using full radial spread.")]
    [SerializeField] private float directionVariationDegrees = 45f;
    [Tooltip("Speed range for each smoke circle.")]
    [SerializeField] private Vector2 speedRange = new Vector2(0.03f, 0.08f);
    [Tooltip("Spawn radius range around the burst origin.")]
    [SerializeField] private Vector2 spawnRadiusRange = new Vector2(0f, 0.02f);
    [Tooltip("Angular velocity range for each smoke circle.")]
    [SerializeField] private Vector2 angularVelocityRange = new Vector2(-18f, 18f);

    [Header("Despawn Burst")]
    [Tooltip("How many circles are spawned when the missile despawns.")]
    [SerializeField] private int despawnCircleCount = 10;
    [Tooltip("How long each despawn smoke circle lives.")]
    [SerializeField] private Vector2 despawnLifetimeRange = new Vector2(0.3f, 0.55f);
    [Tooltip("Starting size range for each despawn smoke circle.")]
    [SerializeField] private Vector2 despawnStartScaleRange = new Vector2(0.05f, 0.09f);
    [Tooltip("Growth multiplier range for each despawn smoke circle.")]
    [SerializeField] private Vector2 despawnEndScaleMultiplierRange = new Vector2(2.2f, 3.3f);
    [Tooltip("Random opacity multiplier range for each despawn smoke circle.")]
    [SerializeField] private Vector2 despawnStartOpacityMultiplierRange = new Vector2(0.65f, 1f);
    [Tooltip("Spread angle in degrees around the missile's facing direction when it despawns.")]
    [SerializeField] private float despawnDirectionVariationDegrees = 55f;
    [Tooltip("Speed range for each despawn smoke circle.")]
    [SerializeField] private Vector2 despawnSpeedRange = new Vector2(0.08f, 0.18f);
    [Tooltip("Spawn radius around the despawn point for each smoke circle.")]
    [SerializeField] private Vector2 despawnSpawnRadiusRange = new Vector2(0f, 0.03f);
    [Tooltip("Angular velocity range for each despawn smoke circle.")]
    [SerializeField] private Vector2 despawnAngularVelocityRange = new Vector2(-30f, 30f);

    private readonly NetworkVariable<Color> currentTargetSmokeColor = new NetworkVariable<Color>(
        new Color(0.12f, 0.12f, 0.12f, 0.55f),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> hasTargetColor = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float nextSpawnTime;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            SetNoTargetColorServer();
        }
    }

    public void SetTargetColorServer(Color color)
    {
        if (!IsServer)
        {
            return;
        }

        color.a = Mathf.Clamp01(smokeAlpha);
        currentTargetSmokeColor.Value = color;
        hasTargetColor.Value = true;
    }

    public void SetNoTargetColorServer()
    {
        if (!IsServer)
        {
            return;
        }

        hasTargetColor.Value = false;
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (spawnIntervalSeconds <= 0f)
        {
            return;
        }

        float now = Time.time;
        if (now < nextSpawnTime)
        {
            return;
        }

        nextSpawnTime = now + spawnIntervalSeconds;
        SpawnTrailBurst();
    }

    private void SpawnTrailBurst()
    {
        if (smokeSprite == null)
        {
            return;
        }

        Vector3 spawnPosition = transform.position - transform.up * backwardOffset;
        GameObject burstObject = new GameObject("MissileTrailSmokeBurst");
        burstObject.SetActive(false);
        burstObject.transform.position = spawnPosition;
        burstObject.transform.rotation = Quaternion.identity;

        SmokeEffect smokeEffect = burstObject.AddComponent<SmokeEffect>();
        Color untargetedColor = GetUntargetedSmokeColor();
        Color targetColor = hasTargetColor.Value
            ? currentTargetSmokeColor.Value
            : untargetedColor;
        float targetCircleWeight = hasTargetColor.Value
            ? Mathf.Clamp01(targetColorWeight)
            : 0f;
        smokeEffect.ConfigureBurst(
            smokeSprite,
            targetColor,
            untargetedColor,
            targetCircleWeight,
            sortingLayerName,
            sortingOrder,
            circleCount,
            lifetimeRange,
            startScaleRange,
            endScaleMultiplierRange,
            startOpacityMultiplierRange,
            spreadInAllDirections,
            baseDirection,
            directionVariationDegrees,
            speedRange,
            spawnRadiusRange,
            angularVelocityRange,
            true,
            false);

        burstObject.SetActive(true);
        smokeEffect.Play();
    }

    public void PlayDespawnBurst(Vector2 facingDirection)
    {
        if (smokeSprite == null)
        {
            return;
        }

        Vector2 resolvedDirection = facingDirection.sqrMagnitude > 0.0001f
            ? facingDirection.normalized
            : (Vector2)transform.up;
        Color burstColor = hasTargetColor.Value
            ? currentTargetSmokeColor.Value
            : GetUntargetedSmokeColor();

        GameObject burstObject = new GameObject("MissileDespawnSmokeBurst");
        burstObject.SetActive(false);
        burstObject.transform.position = transform.position;
        burstObject.transform.rotation = Quaternion.identity;

        SmokeEffect smokeEffect = burstObject.AddComponent<SmokeEffect>();
        smokeEffect.ConfigureBurst(
            smokeSprite,
            burstColor,
            burstColor,
            1f,
            sortingLayerName,
            sortingOrder,
            despawnCircleCount,
            despawnLifetimeRange,
            despawnStartScaleRange,
            despawnEndScaleMultiplierRange,
            despawnStartOpacityMultiplierRange,
            false,
            resolvedDirection,
            despawnDirectionVariationDegrees,
            despawnSpeedRange,
            despawnSpawnRadiusRange,
            despawnAngularVelocityRange,
            true,
            false);

        burstObject.SetActive(true);
        smokeEffect.Play();
    }

    private Color GetUntargetedSmokeColor()
    {
        Color color = untargetedSmokeColor;
        color.a = Mathf.Clamp01(smokeAlpha);
        return color;
    }

    private void OnValidate()
    {
        sortingOrder = Mathf.Clamp(sortingOrder, -32768, 32767);
        spawnIntervalSeconds = Mathf.Max(0.01f, spawnIntervalSeconds);
        backwardOffset = Mathf.Max(0f, backwardOffset);
        smokeAlpha = Mathf.Clamp01(smokeAlpha);
        targetColorWeight = Mathf.Clamp01(targetColorWeight);
        untargetedSmokeColor.a = 1f;
        circleCount = Mathf.Clamp(circleCount, 1, 32);
        despawnCircleCount = Mathf.Clamp(despawnCircleCount, 1, 64);
        directionVariationDegrees = Mathf.Clamp(directionVariationDegrees, 0f, 180f);
        despawnDirectionVariationDegrees = Mathf.Clamp(despawnDirectionVariationDegrees, 0f, 180f);
        lifetimeRange = ClampRange(lifetimeRange, 0.01f);
        startScaleRange = ClampRange(startScaleRange, 0.001f);
        endScaleMultiplierRange = ClampRange(endScaleMultiplierRange, 0.001f);
        startOpacityMultiplierRange = ClampRange(startOpacityMultiplierRange, 0f);
        speedRange = ClampRange(speedRange, 0f);
        spawnRadiusRange = ClampRange(spawnRadiusRange, 0f);
        despawnLifetimeRange = ClampRange(despawnLifetimeRange, 0.01f);
        despawnStartScaleRange = ClampRange(despawnStartScaleRange, 0.001f);
        despawnEndScaleMultiplierRange = ClampRange(despawnEndScaleMultiplierRange, 0.001f);
        despawnStartOpacityMultiplierRange = ClampRange(despawnStartOpacityMultiplierRange, 0f);
        despawnSpeedRange = ClampRange(despawnSpeedRange, 0f);
        despawnSpawnRadiusRange = ClampRange(despawnSpawnRadiusRange, 0f);
    }

    private static Vector2 ClampRange(Vector2 range, float minimumValue)
    {
        float min = Mathf.Max(minimumValue, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2(min, max);
    }
}
