using UnityEngine;

public static class TankDeathDebrisBurst
{
    [System.Serializable]
    public sealed class TrailSmokeConfig
    {
        private const string DefaultSmokeSpriteResourcePath = "Sprites/smoke";

        [Tooltip("Smoke sprite used by the debris trail. Leave empty to use Resources/Sprites/smoke.")]
        public Sprite smokeSprite;
        [Tooltip("Sorting layer used by the debris trail smoke.")]
        public string sortingLayerName = "Default";
        [Tooltip("Sorting order used by the debris trail smoke.")]
        public int sortingOrder = 110;
        [Tooltip("Seconds between trail smoke bursts while a debris piece is moving.")]
        public float spawnIntervalSeconds = 0.06f;
        [Tooltip("How far behind the moving debris piece each smoke burst spawns.")]
        public float backwardOffset = 0.04f;
        [Tooltip("Minimum debris speed required before the trail emits smoke.")]
        public float minSpeedForTrail = 0.16f;
        [Tooltip("Trail smoke color.")]
        public Color smokeColor = new Color(0.12f, 0.12f, 0.12f, 0.42f);
        [Tooltip("How many circles each trail smoke burst uses.")]
        public int circleCount = 3;
        [Tooltip("Lifetime range for each trail smoke circle.")]
        public Vector2 lifetimeRange = new Vector2(0.18f, 0.32f);
        [Tooltip("Starting size range for each trail smoke circle.")]
        public Vector2 startScaleRange = new Vector2(0.018f, 0.03f);
        [Tooltip("Growth multiplier range for each trail smoke circle.")]
        public Vector2 endScaleMultiplierRange = new Vector2(1.7f, 2.4f);
        [Tooltip("Random opacity multiplier range for each trail smoke circle.")]
        public Vector2 startOpacityMultiplierRange = new Vector2(0.45f, 0.8f);
        [Tooltip("If enabled, each trail burst spreads in all directions.")]
        public bool spreadInAllDirections = true;
        [Tooltip("Direction variation in degrees when radial spread is disabled.")]
        public float directionVariationDegrees = 45f;
        [Tooltip("Speed range for each trail smoke circle.")]
        public Vector2 speedRange = new Vector2(0.04f, 0.1f);
        [Tooltip("Spawn radius around the burst origin for each trail smoke circle.")]
        public Vector2 spawnRadiusRange = new Vector2(0f, 0.02f);
        [Tooltip("Angular velocity range for each trail smoke circle.")]
        public Vector2 angularVelocityRange = new Vector2(-18f, 18f);

        public Sprite ResolveSmokeSprite()
        {
            return smokeSprite != null ? smokeSprite : Resources.Load<Sprite>(DefaultSmokeSpriteResourcePath);
        }

        public void ClampInEditor()
        {
            sortingOrder = Mathf.Clamp(sortingOrder, -32768, 32767);
            spawnIntervalSeconds = Mathf.Max(0.01f, spawnIntervalSeconds);
            backwardOffset = Mathf.Max(0f, backwardOffset);
            minSpeedForTrail = Mathf.Max(0f, minSpeedForTrail);
            smokeColor.a = Mathf.Clamp01(smokeColor.a);
            circleCount = Mathf.Clamp(circleCount, 1, 16);
            lifetimeRange = ClampRange(lifetimeRange, 0.01f);
            startScaleRange = ClampRange(startScaleRange, 0.001f);
            endScaleMultiplierRange = ClampRange(endScaleMultiplierRange, 0.001f);
            startOpacityMultiplierRange = ClampRange(startOpacityMultiplierRange, 0f);
            directionVariationDegrees = Mathf.Clamp(directionVariationDegrees, 0f, 180f);
            speedRange = ClampRange(speedRange, 0f);
            spawnRadiusRange = ClampRange(spawnRadiusRange, 0f);
        }
    }

    [System.Serializable]
    public sealed class Config
    {
        [Tooltip("Possible tank part sprites that can spawn on death. Random selection uses replacement, so sprites can repeat.")]
        public Sprite[] partSprites;
        [Tooltip("Sorting layer used by the debris pieces.")]
        public string sortingLayerName = "Default";
        [Tooltip("Sorting order used by the debris pieces.")]
        public int sortingOrder = 122;
        [Tooltip("Minimum number of debris pieces spawned.")]
        public int minPartCount = 5;
        [Tooltip("Maximum number of debris pieces spawned.")]
        public int maxPartCount = 8;
        [Tooltip("How far from the death position each piece can initially spawn.")]
        public Vector2 spawnRadiusRange = new Vector2(0.01f, 0.07f);
        [Tooltip("Launch speed range for each piece.")]
        public Vector2 launchSpeedRange = new Vector2(0.6f, 1.5f);
        [Tooltip("Scale multiplier range applied to each piece.")]
        public Vector2 scaleRange = new Vector2(0.9f, 1.1f);
        [Tooltip("Initial angular velocity range for each piece.")]
        public Vector2 angularVelocityRange = new Vector2(-540f, 540f);
        [Tooltip("Minimum absolute spin speed given to each piece so every spawned part visibly rotates.")]
        public float minimumAbsoluteAngularVelocity = 120f;
        [Tooltip("How quickly pieces slow down.")]
        public float linearDragPerSecond = 3.5f;
        [Tooltip("How quickly spinning slows down.")]
        public float angularDragPerSecond = 720f;
        [Tooltip("Radius used to detect wall contact. Touching a wall starts the fade immediately.")]
        public float wallContactRadius = 0.025f;
        [Tooltip("Speed threshold under which a piece is considered settled.")]
        public float stopSpeedThreshold = 0.08f;
        [Tooltip("Angular speed threshold under which a piece is considered settled.")]
        public float stopAngularSpeedThreshold = 12f;
        [Tooltip("How long a piece waits after settling before it starts fading.")]
        public float fadeDelayAfterStop = 0.05f;
        [Tooltip("How long the fade-out lasts once the piece has settled.")]
        public float fadeDuration = 0.35f;
        [Tooltip("Safety lifetime in case a piece never quite settles.")]
        public float maxLifetimeSeconds = 4f;
        [Tooltip("Smoke trail settings used by moving debris pieces.")]
        public TrailSmokeConfig trailSmoke = new TrailSmokeConfig();

        public void ClampInEditor()
        {
            sortingOrder = Mathf.Clamp(sortingOrder, -32768, 32767);
            minPartCount = Mathf.Max(1, minPartCount);
            maxPartCount = Mathf.Max(minPartCount, maxPartCount);
            spawnRadiusRange = ClampRange(spawnRadiusRange, 0f);
            launchSpeedRange = ClampRange(launchSpeedRange, 0f);
            scaleRange = ClampRange(scaleRange, 0.001f);
            minimumAbsoluteAngularVelocity = Mathf.Max(0f, minimumAbsoluteAngularVelocity);
            linearDragPerSecond = Mathf.Max(0f, linearDragPerSecond);
            angularDragPerSecond = Mathf.Max(0f, angularDragPerSecond);
            wallContactRadius = Mathf.Max(0f, wallContactRadius);
            stopSpeedThreshold = Mathf.Max(0f, stopSpeedThreshold);
            stopAngularSpeedThreshold = Mathf.Max(0f, stopAngularSpeedThreshold);
            fadeDelayAfterStop = Mathf.Max(0f, fadeDelayAfterStop);
            fadeDuration = Mathf.Max(0.01f, fadeDuration);
            maxLifetimeSeconds = Mathf.Max(fadeDuration, maxLifetimeSeconds);
            trailSmoke?.ClampInEditor();
        }
    }

    public static void Spawn(Config config, Vector3 position, Color color, Vector2 facingDirection, int randomSeed)
    {
        if (config == null || config.partSprites == null || config.partSprites.Length == 0)
        {
            return;
        }

        Random.State previousRandomState = Random.state;
        Random.InitState(randomSeed);

        int minCount = Mathf.Max(1, config.minPartCount);
        int maxCount = Mathf.Max(minCount, config.maxPartCount);
        int debrisCount = Random.Range(minCount, maxCount + 1);
        Vector2 fallbackDirection = facingDirection.sqrMagnitude > 0.0001f
            ? facingDirection.normalized
            : Vector2.up;

        for (int i = 0; i < debrisCount; i++)
        {
            if (!TrySelectRandomSprite(config.partSprites, out Sprite selectedSprite) || selectedSprite == null)
            {
                continue;
            }

            Vector2 launchDirection = Random.insideUnitCircle;
            if (launchDirection.sqrMagnitude <= 0.0001f)
            {
                launchDirection = fallbackDirection;
            }

            launchDirection.Normalize();
            float spawnDistance = Random.Range(config.spawnRadiusRange.x, config.spawnRadiusRange.y);
            float launchSpeed = Random.Range(config.launchSpeedRange.x, config.launchSpeedRange.y);
            float angularVelocity = Random.Range(config.angularVelocityRange.x, config.angularVelocityRange.y);
            float maxAbsoluteAngularVelocity = Mathf.Max(
                Mathf.Abs(config.angularVelocityRange.x),
                Mathf.Abs(config.angularVelocityRange.y));
            float minimumAbsoluteAngularVelocity = Mathf.Min(
                Mathf.Max(0f, config.minimumAbsoluteAngularVelocity),
                maxAbsoluteAngularVelocity);
            if (Mathf.Abs(angularVelocity) < minimumAbsoluteAngularVelocity)
            {
                float spinDirection = angularVelocity < 0f
                    ? -1f
                    : angularVelocity > 0f
                        ? 1f
                        : (Random.value < 0.5f ? -1f : 1f);
                angularVelocity = spinDirection * minimumAbsoluteAngularVelocity;
            }

            float scale = Random.Range(config.scaleRange.x, config.scaleRange.y);

            GameObject pieceObject = new GameObject($"TankDeathDebris_{i:D2}");
            pieceObject.transform.position = position + (Vector3)(launchDirection * spawnDistance);
            pieceObject.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            TankDeathDebrisPiece piece = pieceObject.AddComponent<TankDeathDebrisPiece>();
            piece.Configure(
                selectedSprite,
                color,
                config.sortingLayerName,
                config.sortingOrder,
                scale,
                launchDirection * launchSpeed,
                angularVelocity,
                config.linearDragPerSecond,
                config.angularDragPerSecond,
                config.wallContactRadius,
                config.stopSpeedThreshold,
                config.stopAngularSpeedThreshold,
                config.fadeDelayAfterStop,
                config.fadeDuration,
                config.maxLifetimeSeconds,
                config.trailSmoke);
        }

        Random.state = previousRandomState;
    }

    private static bool TrySelectRandomSprite(Sprite[] sprites, out Sprite selectedSprite)
    {
        selectedSprite = null;
        if (sprites == null || sprites.Length == 0)
        {
            return false;
        }

        for (int attempt = 0; attempt < sprites.Length; attempt++)
        {
            Sprite candidate = sprites[Random.Range(0, sprites.Length)];
            if (candidate != null)
            {
                selectedSprite = candidate;
                return true;
            }
        }

        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null)
            {
                selectedSprite = sprites[i];
                return true;
            }
        }

        return false;
    }

    private static Vector2 ClampRange(Vector2 range, float minimumValue)
    {
        float min = Mathf.Max(minimumValue, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2(min, max);
    }
}
