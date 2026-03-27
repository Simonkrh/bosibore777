using UnityEngine;

[DisallowMultipleComponent]
public class TankDeathDebrisPiece : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Vector2 velocity;
    private float angularVelocity;
    private float linearDragPerSecond;
    private float angularDragPerSecond;
    private float wallContactRadius;
    private float stopSpeedThreshold;
    private float stopAngularSpeedThreshold;
    private float fadeDelayAfterStop;
    private float fadeDuration;
    private float maxLifetimeSeconds;
    private float aliveTime;
    private float settledTime;
    private float fadeElapsedTime;
    private float nextTrailSmokeTime;
    private int wallCollisionMask;
    private bool hasTouchedWall;
    private Color baseColor = Color.white;
    private TankDeathDebrisBurst.TrailSmokeConfig trailSmokeConfig;

    public void Configure(
        Sprite sprite,
        Color color,
        string sortingLayerName,
        int sortingOrder,
        float scale,
        Vector2 initialVelocity,
        float initialAngularVelocity,
        float linearDrag,
        float angularDrag,
        float contactRadius,
        float stopSpeed,
        float stopAngularSpeed,
        float fadeDelay,
        float fadeOutDuration,
        float safetyLifetime,
        TankDeathDebrisBurst.TrailSmokeConfig trailConfig)
    {
        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.color = color;
        spriteRenderer.sortingLayerName = string.IsNullOrWhiteSpace(sortingLayerName) ? "Default" : sortingLayerName;
        spriteRenderer.sortingOrder = sortingOrder;

        transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);
        baseColor = color;
        velocity = initialVelocity;
        angularVelocity = initialAngularVelocity;
        linearDragPerSecond = Mathf.Max(0f, linearDrag);
        angularDragPerSecond = Mathf.Max(0f, angularDrag);
        wallContactRadius = Mathf.Max(0f, contactRadius);
        stopSpeedThreshold = Mathf.Max(0f, stopSpeed);
        stopAngularSpeedThreshold = Mathf.Max(0f, stopAngularSpeed);
        fadeDelayAfterStop = Mathf.Max(0f, fadeDelay);
        fadeDuration = Mathf.Max(0.01f, fadeOutDuration);
        maxLifetimeSeconds = Mathf.Max(fadeDuration, safetyLifetime);
        trailSmokeConfig = trailConfig;
        nextTrailSmokeTime = Time.time;
        wallCollisionMask = LayerMask.GetMask("Wall");
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        aliveTime += deltaTime;

        if (!hasTouchedWall && TryMoveAndDetectWallContact(deltaTime))
        {
            hasTouchedWall = true;
            velocity = Vector2.zero;
            angularVelocity = 0f;
        }
        else if (!hasTouchedWall && Mathf.Abs(angularVelocity) > 0.001f)
        {
            transform.Rotate(0f, 0f, angularVelocity * deltaTime);
        }

        if (!hasTouchedWall)
        {
            velocity = Vector2.MoveTowards(velocity, Vector2.zero, linearDragPerSecond * deltaTime);
            angularVelocity = Mathf.MoveTowards(angularVelocity, 0f, angularDragPerSecond * deltaTime);
        }

        TrySpawnTrailSmoke();

        bool isSettled = velocity.sqrMagnitude <= stopSpeedThreshold * stopSpeedThreshold &&
                         Mathf.Abs(angularVelocity) <= stopAngularSpeedThreshold;
        if (isSettled)
        {
            settledTime += deltaTime;
        }
        else
        {
            settledTime = 0f;
        }

        bool shouldFade = hasTouchedWall || settledTime >= fadeDelayAfterStop || aliveTime >= maxLifetimeSeconds;
        if (!shouldFade)
        {
            return;
        }

        fadeElapsedTime += deltaTime;
        float normalizedFade = Mathf.Clamp01(fadeElapsedTime / fadeDuration);
        if (spriteRenderer != null)
        {
            Color color = baseColor;
            color.a = baseColor.a * (1f - normalizedFade);
            spriteRenderer.color = color;
        }

        if (normalizedFade >= 1f)
        {
            Destroy(gameObject);
        }
    }

    private bool TryMoveAndDetectWallContact(float deltaTime)
    {
        Vector2 startPosition = transform.position;
        Vector2 movementDelta = velocity * deltaTime;

        if (wallCollisionMask == 0 || wallContactRadius <= 0f)
        {
            transform.position = startPosition + movementDelta;
            return false;
        }

        if (movementDelta.sqrMagnitude > 0.000001f)
        {
            RaycastHit2D wallHit = Physics2D.CircleCast(
                startPosition,
                wallContactRadius,
                movementDelta.normalized,
                movementDelta.magnitude,
                wallCollisionMask);
            if (wallHit.collider != null)
            {
                transform.position = wallHit.centroid;
                return true;
            }
        }

        Vector2 endPosition = startPosition + movementDelta;
        transform.position = endPosition;
        return Physics2D.OverlapCircle(endPosition, wallContactRadius, wallCollisionMask) != null;
    }

    private void TrySpawnTrailSmoke()
    {
        if (trailSmokeConfig == null || Time.time < nextTrailSmokeTime)
        {
            return;
        }

        float speed = velocity.magnitude;
        if (speed < trailSmokeConfig.minSpeedForTrail)
        {
            return;
        }

        nextTrailSmokeTime = Time.time + Mathf.Max(0.01f, trailSmokeConfig.spawnIntervalSeconds);

        Vector2 backwardDirection = speed > 0.0001f ? -velocity.normalized : Vector2.zero;
        Vector3 spawnPosition = transform.position + (Vector3)(backwardDirection * trailSmokeConfig.backwardOffset);
        Sprite smokeSprite = trailSmokeConfig.ResolveSmokeSprite();
        if (smokeSprite == null)
        {
            return;
        }

        GameObject burstObject = new GameObject("TankDeathDebrisTrailSmoke");
        burstObject.SetActive(false);
        burstObject.transform.position = spawnPosition;
        burstObject.transform.rotation = Quaternion.identity;

        SmokeEffect smokeEffect = burstObject.AddComponent<SmokeEffect>();
        Color trailColor = trailSmokeConfig.smokeColor;
        smokeEffect.ConfigureBurst(
            smokeSprite,
            trailColor,
            trailColor,
            1f,
            trailSmokeConfig.sortingLayerName,
            trailSmokeConfig.sortingOrder,
            trailSmokeConfig.circleCount,
            trailSmokeConfig.lifetimeRange,
            trailSmokeConfig.startScaleRange,
            trailSmokeConfig.endScaleMultiplierRange,
            trailSmokeConfig.startOpacityMultiplierRange,
            trailSmokeConfig.spreadInAllDirections,
            backwardDirection,
            trailSmokeConfig.directionVariationDegrees,
            trailSmokeConfig.speedRange,
            trailSmokeConfig.spawnRadiusRange,
            trailSmokeConfig.angularVelocityRange,
            true,
            false);

        burstObject.SetActive(true);
        smokeEffect.Play();
    }
}
