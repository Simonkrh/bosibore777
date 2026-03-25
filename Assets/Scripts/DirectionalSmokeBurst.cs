using UnityEngine;

public static class DirectionalSmokeBurst
{
    [System.Serializable]
    public sealed class Config
    {
        private const string DefaultSmokeSpriteResourcePath = "Sprites/smoke";

        [Tooltip("Sprite used for each smoke circle in the directional burst.")]
        public Sprite smokeSprite;
        [Tooltip("Tint color used for all circles in this directional smoke burst.")]
        public Color smokeColor = new Color(0.084905684f, 0.084505185f, 0.084505185f, 0.55f);
        [Tooltip("Sorting layer used by this smoke burst.")]
        public string sortingLayerName = "Default";
        [Tooltip("Sorting order used by this smoke burst.")]
        public int sortingOrder = 58;
        [Tooltip("How many circles are spawned in the burst.")]
        public int circleCount = 15;
        [Tooltip("How long each smoke circle lives.")]
        public Vector2 lifetimeRange = new Vector2(0.3f, 0.55f);
        [Tooltip("Starting size range for each smoke circle.")]
        public Vector2 startScaleRange = new Vector2(0.017f, 0.02f);
        [Tooltip("Growth multiplier range for each smoke circle.")]
        public Vector2 endScaleMultiplierRange = new Vector2(2.2f, 3.3f);
        [Tooltip("Random opacity multiplier range for each smoke circle.")]
        public Vector2 startOpacityMultiplierRange = new Vector2(0.4f, 0.7f);
        [Tooltip("Spread angle in degrees around the facing direction.")]
        public float directionVariationDegrees = 55f;
        [Tooltip("Speed range for each smoke circle.")]
        public Vector2 speedRange = new Vector2(0.5f, 0.5f);
        [Tooltip("Spawn radius around the burst origin for each smoke circle.")]
        public Vector2 spawnRadiusRange = new Vector2(0f, 0.05f);
        [Tooltip("Angular velocity range for each smoke circle.")]
        public Vector2 angularVelocityRange = new Vector2(-30f, 30f);

        public bool TryCreateSettings(Vector3 position, Vector2 facingDirection, out Settings settings)
        {
            Sprite resolvedSprite = smokeSprite != null ? smokeSprite : Resources.Load<Sprite>(DefaultSmokeSpriteResourcePath);
            if (resolvedSprite == null)
            {
                settings = default;
                return false;
            }

            Vector2 resolvedDirection = facingDirection.sqrMagnitude > 0.0001f
                ? facingDirection.normalized
                : Vector2.up;

            settings = new Settings(
                position,
                resolvedDirection,
                resolvedSprite,
                smokeColor,
                string.IsNullOrWhiteSpace(sortingLayerName) ? "Default" : sortingLayerName,
                Mathf.Clamp(sortingOrder, -32768, 32767),
                Mathf.Clamp(circleCount, 1, 64),
                ClampRange(lifetimeRange, 0.01f),
                ClampRange(startScaleRange, 0.001f),
                ClampRange(endScaleMultiplierRange, 0.001f),
                ClampRange(startOpacityMultiplierRange, 0f),
                Mathf.Clamp(directionVariationDegrees, 0f, 180f),
                ClampRange(speedRange, 0f),
                ClampRange(spawnRadiusRange, 0f),
                angularVelocityRange);
            return true;
        }

        public void ClampInEditor()
        {
            sortingOrder = Mathf.Clamp(sortingOrder, -32768, 32767);
            smokeColor.a = Mathf.Clamp01(smokeColor.a);
            circleCount = Mathf.Clamp(circleCount, 1, 64);
            directionVariationDegrees = Mathf.Clamp(directionVariationDegrees, 0f, 180f);
            lifetimeRange = ClampRange(lifetimeRange, 0.01f);
            startScaleRange = ClampRange(startScaleRange, 0.001f);
            endScaleMultiplierRange = ClampRange(endScaleMultiplierRange, 0.001f);
            startOpacityMultiplierRange = ClampRange(startOpacityMultiplierRange, 0f);
            speedRange = ClampRange(speedRange, 0f);
            spawnRadiusRange = ClampRange(spawnRadiusRange, 0f);
        }
    }

    public readonly struct Settings
    {
        public Settings(
            Vector3 position,
            Vector2 direction,
            Sprite sprite,
            Color color,
            string layerName,
            int order,
            int circles,
            Vector2 lifetime,
            Vector2 startScale,
            Vector2 endScaleMultiplier,
            Vector2 opacityRange,
            float variationDegrees,
            Vector2 speed,
            Vector2 spawnRadius,
            Vector2 angularVelocity)
        {
            Position = position;
            Direction = direction;
            Sprite = sprite;
            Color = color;
            SortingLayerName = layerName;
            SortingOrder = order;
            CircleCount = circles;
            LifetimeRange = lifetime;
            StartScaleRange = startScale;
            EndScaleMultiplierRange = endScaleMultiplier;
            StartOpacityMultiplierRange = opacityRange;
            DirectionVariationDegrees = variationDegrees;
            SpeedRange = speed;
            SpawnRadiusRange = spawnRadius;
            AngularVelocityRange = angularVelocity;
        }

        public Vector3 Position { get; }
        public Vector2 Direction { get; }
        public Sprite Sprite { get; }
        public Color Color { get; }
        public string SortingLayerName { get; }
        public int SortingOrder { get; }
        public int CircleCount { get; }
        public Vector2 LifetimeRange { get; }
        public Vector2 StartScaleRange { get; }
        public Vector2 EndScaleMultiplierRange { get; }
        public Vector2 StartOpacityMultiplierRange { get; }
        public float DirectionVariationDegrees { get; }
        public Vector2 SpeedRange { get; }
        public Vector2 SpawnRadiusRange { get; }
        public Vector2 AngularVelocityRange { get; }
    }

    public static void Spawn(Settings settings, string objectName)
    {
        GameObject burstObject = new GameObject(objectName);
        burstObject.SetActive(false);
        burstObject.transform.position = settings.Position;
        burstObject.transform.rotation = Quaternion.identity;

        SmokeEffect smokeEffect = burstObject.AddComponent<SmokeEffect>();
        smokeEffect.ConfigureBurst(
            settings.Sprite,
            settings.Color,
            settings.Color,
            1f,
            settings.SortingLayerName,
            settings.SortingOrder,
            settings.CircleCount,
            settings.LifetimeRange,
            settings.StartScaleRange,
            settings.EndScaleMultiplierRange,
            settings.StartOpacityMultiplierRange,
            false,
            settings.Direction,
            settings.DirectionVariationDegrees,
            settings.SpeedRange,
            settings.SpawnRadiusRange,
            settings.AngularVelocityRange,
            true,
            false);

        burstObject.SetActive(true);
        smokeEffect.Play();
    }

    private static Vector2 ClampRange(Vector2 range, float minimumValue)
    {
        float min = Mathf.Max(minimumValue, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2(min, max);
    }
}
