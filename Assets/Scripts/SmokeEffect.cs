using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SmokeEffect : MonoBehaviour
{
    private const float MinDirectionSqrMagnitude = 0.0001f;

    private sealed class SmokeCircle
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public Vector2 StartLocalPosition;
        public Vector2 Velocity;
        public float Lifetime;
        public float Age;
        public float StartScale;
        public float EndScale;
        public float StartAlphaMultiplier;
        public float RotationDegrees;
        public float AngularVelocity;
        public bool IsActive;
    }

    [Header("Playback")]
    [Tooltip("If enabled, the effect automatically starts when this GameObject becomes active.")]
    [SerializeField] private bool playOnEnable = true;
    [Tooltip("If enabled, destroys this GameObject after all smoke circles have finished. Only use this on a dedicated effect object, not on gameplay objects like AbilityPickup.")]
    [SerializeField] private bool destroyWhenFinished;
    [Tooltip("If enabled, smoke animation keeps running even when Time.timeScale is 0 or slowed down.")]
    [SerializeField] private bool useUnscaledTime;
    [Tooltip("How many individual smoke circles are spawned each time the effect plays.")]
    [SerializeField] private int circleCount = 6;

    [Header("Sprite")]
    [Tooltip("Use a soft white circle sprite with transparent edges so the smoke can be tinted to any color.")]
    [SerializeField] private Sprite smokeSprite;
    [Tooltip("Sorting layer used by all smoke circles.")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Sorting order used by all smoke circles. Higher values render in front of lower ones on the same sorting layer.")]
    [SerializeField] private int sortingOrder = 110;
    [Tooltip("Base color of the smoke before per-circle opacity variation and fade are applied.")]
    [SerializeField] private Color smokeColor = new Color(1f, 1f, 1f, 0.7f);

    [Header("Lifetime")]
    [Tooltip("How long each circle lives. Lower values make the smoke fade and finish faster.")]
    [SerializeField] private Vector2 lifetimeRange = new Vector2(0.45f, 0.8f);
    [Tooltip("Controls how the smoke fades over its lifetime. Left side is the start, right side is the end.")]
    [SerializeField] private AnimationCurve alphaOverLifetime = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(1f, 0f));
    [Tooltip("Controls how quickly circles grow from their start size to their end size over their lifetime.")]
    [SerializeField] private AnimationCurve scaleOverLifetime = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(1f, 1f));

    [Header("Scale")]
    [Tooltip("How big each circle starts.")]
    [SerializeField] private Vector2 startScaleRange = new Vector2(0.1f, 0.18f);
    [Tooltip("How much larger each circle becomes by the end of its lifetime.")]
    [SerializeField] private Vector2 endScaleMultiplierRange = new Vector2(1.6f, 2.6f);
    [Tooltip("Random opacity multiplier applied when each circle spawns. Lower values make some circles start fainter.")]
    [SerializeField] private Vector2 startOpacityMultiplierRange = new Vector2(0.75f, 1f);

    [Header("Motion")]
    [Tooltip("If enabled, circles spread in random directions instead of following the base direction. When this is off, leaving baseDirection at 0,0 also gives radial spread.")]
    [SerializeField] private bool spreadInAllDirections;
    [Tooltip("Main movement direction. If this is left at 0,0 the smoke will spread in all directions.")]
    [SerializeField] private Vector2 baseDirection = Vector2.up;
    [Tooltip("How much each circle can deviate from the base direction in degrees.")]
    [SerializeField, Range(0f, 180f)] private float directionVariationDegrees = 30f;
    [Tooltip("Movement speed range for the smoke circles.")]
    [SerializeField] private Vector2 speedRange = new Vector2(0.15f, 0.4f);
    [Tooltip("How far from the effect origin circles can spawn initially.")]
    [SerializeField] private Vector2 spawnRadiusRange = new Vector2(0f, 0.08f);
    [Tooltip("Random spin speed range in degrees per second for each circle.")]
    [SerializeField] private Vector2 angularVelocityRange = new Vector2(-30f, 30f);

    private readonly List<SmokeCircle> circlePool = new List<SmokeCircle>(8);
    private bool isPlaying;

    private void Awake()
    {
        EnsurePool();
        ApplySharedRendererSettings();
        DisableAllCircles();
    }

    private void OnEnable()
    {
        if (playOnEnable && !isPlaying)
        {
            Play();
            return;
        }

        if (!isPlaying)
        {
            enabled = false;
        }
    }

    public void Configure(Color color, Vector2 direction)
    {
        smokeColor = color;
        baseDirection = direction.sqrMagnitude > MinDirectionSqrMagnitude
            ? direction.normalized
            : Vector2.zero;

        RefreshActiveCircles();
    }

    public void SetColor(Color color)
    {
        smokeColor = color;
        RefreshActiveCircles();
    }

    public void SetDirection(Vector2 direction)
    {
        baseDirection = direction.sqrMagnitude > MinDirectionSqrMagnitude
            ? direction.normalized
            : Vector2.zero;
    }

    public void SetSpreadInAllDirections(bool value)
    {
        spreadInAllDirections = value;
    }

    public void ConfigureBurst(
        Sprite sprite,
        Color color,
        string layerName,
        int order,
        int circles,
        Vector2 lifetime,
        Vector2 startScale,
        Vector2 endScaleMultiplier,
        Vector2 opacityRange,
        bool radialSpread,
        Vector2 direction,
        float variationDegrees,
        Vector2 speed,
        Vector2 spawnRadius,
        Vector2 angularVelocity,
        bool destroyAfterFinish,
        bool useUnscaled)
    {
        playOnEnable = false;
        destroyWhenFinished = destroyAfterFinish;
        useUnscaledTime = useUnscaled;
        circleCount = Mathf.Clamp(circles, 1, 128);

        smokeSprite = sprite;
        sortingLayerName = string.IsNullOrWhiteSpace(layerName) ? "Default" : layerName;
        sortingOrder = Mathf.Clamp(order, -32768, 32767);
        smokeColor = color;

        lifetimeRange = ClampRange(lifetime, 0.01f);
        startScaleRange = ClampRange(startScale, 0.001f);
        endScaleMultiplierRange = ClampRange(endScaleMultiplier, 0.001f);
        startOpacityMultiplierRange = ClampRange(opacityRange, 0f);

        spreadInAllDirections = radialSpread;
        baseDirection = direction.sqrMagnitude > MinDirectionSqrMagnitude
            ? direction.normalized
            : Vector2.zero;
        directionVariationDegrees = Mathf.Clamp(variationDegrees, 0f, 180f);
        speedRange = ClampRange(speed, 0f);
        spawnRadiusRange = ClampRange(spawnRadius, 0f);
        angularVelocityRange = angularVelocity;

        EnsurePool();
        ApplySharedRendererSettings();
        RefreshActiveCircles();
    }

    public void Play()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        EnsurePool();
        ApplySharedRendererSettings();
        EmitCircles();
        isPlaying = true;
        enabled = true;

        if (smokeSprite == null)
        {
            Debug.LogWarning(
                $"SmokeEffect on '{name}' needs a smoke sprite. Assign a soft white circle sprite with transparent edges.",
                this);
        }
    }

    public void Stop()
    {
        isPlaying = false;
        DisableAllCircles();
        enabled = false;
    }

    private void Update()
    {
        if (!isPlaying)
        {
            return;
        }

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        bool hasActiveCircles = false;
        for (int i = 0; i < circlePool.Count; i++)
        {
            SmokeCircle circle = circlePool[i];
            if (circle == null || !circle.IsActive)
            {
                continue;
            }

            circle.Age += deltaTime;
            float normalizedAge = circle.Lifetime > 0.0001f
                ? Mathf.Clamp01(circle.Age / circle.Lifetime)
                : 1f;

            Vector2 localPosition = circle.StartLocalPosition + circle.Velocity * circle.Age;
            circle.Transform.localPosition = localPosition;

            circle.RotationDegrees += circle.AngularVelocity * deltaTime;
            circle.Transform.localRotation = Quaternion.Euler(0f, 0f, circle.RotationDegrees);

            ApplyCircleVisual(circle, normalizedAge);
            if (normalizedAge >= 1f)
            {
                circle.IsActive = false;
                circle.Renderer.enabled = false;
                continue;
            }

            hasActiveCircles = true;
        }

        if (!hasActiveCircles)
        {
            FinishPlayback();
        }
    }

    private void EmitCircles()
    {
        DisableAllCircles();

        for (int i = 0; i < circleCount; i++)
        {
            SmokeCircle circle = circlePool[i];
            circle.StartLocalPosition = SampleSpawnOffset();
            circle.Velocity = SampleVelocity();
            circle.Lifetime = SampleRange(lifetimeRange, 0.01f);
            circle.Age = 0f;
            circle.StartScale = SampleRange(startScaleRange, 0.001f);
            circle.EndScale = circle.StartScale * SampleRange(endScaleMultiplierRange, 0.001f);
            circle.StartAlphaMultiplier = SampleRange(startOpacityMultiplierRange, 0f);
            circle.RotationDegrees = Random.Range(0f, 360f);
            circle.AngularVelocity = Random.Range(
                Mathf.Min(angularVelocityRange.x, angularVelocityRange.y),
                Mathf.Max(angularVelocityRange.x, angularVelocityRange.y));
            circle.IsActive = true;

            circle.Transform.localPosition = circle.StartLocalPosition;
            circle.Transform.localRotation = Quaternion.Euler(0f, 0f, circle.RotationDegrees);

            ApplyCircleVisual(circle, 0f);
        }
    }

    private void ApplyCircleVisual(SmokeCircle circle, float normalizedAge)
    {
        float scaleT = EvaluateCurve(scaleOverLifetime, normalizedAge, normalizedAge);
        float scale = Mathf.LerpUnclamped(circle.StartScale, circle.EndScale, scaleT);
        circle.Transform.localScale = new Vector3(scale, scale, 1f);

        float alphaT = EvaluateCurve(alphaOverLifetime, normalizedAge, 1f - normalizedAge);
        Color color = smokeColor;
        color.a *= circle.StartAlphaMultiplier * Mathf.Clamp01(alphaT);
        circle.Renderer.color = color;
        circle.Renderer.enabled = smokeSprite != null && color.a > 0.0001f;
    }

    private void RefreshActiveCircles()
    {
        for (int i = 0; i < circlePool.Count; i++)
        {
            SmokeCircle circle = circlePool[i];
            if (circle == null || !circle.IsActive)
            {
                continue;
            }

            float normalizedAge = circle.Lifetime > 0.0001f
                ? Mathf.Clamp01(circle.Age / circle.Lifetime)
                : 1f;
            ApplyCircleVisual(circle, normalizedAge);
        }
    }

    private void EnsurePool()
    {
        while (circlePool.Count < circleCount)
        {
            int index = circlePool.Count;
            GameObject circleObject = new GameObject($"SmokeCircle_{index:D2}");
            circleObject.transform.SetParent(transform, false);

            SpriteRenderer renderer = circleObject.AddComponent<SpriteRenderer>();
            renderer.enabled = false;

            circlePool.Add(new SmokeCircle
            {
                Transform = circleObject.transform,
                Renderer = renderer
            });
        }
    }

    private void ApplySharedRendererSettings()
    {
        string resolvedSortingLayerName = string.IsNullOrWhiteSpace(sortingLayerName)
            ? "Default"
            : sortingLayerName;

        for (int i = 0; i < circlePool.Count; i++)
        {
            SmokeCircle circle = circlePool[i];
            if (circle == null || circle.Renderer == null)
            {
                continue;
            }

            circle.Renderer.sprite = smokeSprite;
            circle.Renderer.sortingLayerName = resolvedSortingLayerName;
            circle.Renderer.sortingOrder = sortingOrder;
        }
    }

    private void DisableAllCircles()
    {
        for (int i = 0; i < circlePool.Count; i++)
        {
            SmokeCircle circle = circlePool[i];
            if (circle == null)
            {
                continue;
            }

            circle.IsActive = false;
            if (circle.Renderer != null)
            {
                circle.Renderer.enabled = false;
            }
        }
    }

    private void FinishPlayback()
    {
        isPlaying = false;
        DisableAllCircles();

        if (destroyWhenFinished)
        {
            Destroy(gameObject);
            return;
        }

        enabled = false;
    }

    private Vector2 SampleVelocity()
    {
        bool useRadialSpread = spreadInAllDirections || baseDirection.sqrMagnitude <= MinDirectionSqrMagnitude;
        Vector2 moveDirection;
        if (useRadialSpread)
        {
            float randomAngle = Random.Range(0f, 360f);
            moveDirection = Quaternion.Euler(0f, 0f, randomAngle) * Vector2.right;
        }
        else
        {
            float directionOffset = Random.Range(-directionVariationDegrees, directionVariationDegrees);
            moveDirection = Quaternion.Euler(0f, 0f, directionOffset) * baseDirection.normalized;
        }

        float speed = SampleRange(speedRange, 0f);
        return moveDirection.normalized * speed;
    }

    private Vector2 SampleSpawnOffset()
    {
        float radius = SampleRange(spawnRadiusRange, 0f);
        float angle = Random.Range(0f, 360f);
        return Quaternion.Euler(0f, 0f, angle) * Vector2.right * radius;
    }

    private static float SampleRange(Vector2 range, float minimumValue)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        return Mathf.Max(minimumValue, Random.Range(min, max));
    }

    private static float EvaluateCurve(AnimationCurve curve, float time, float fallbackValue)
    {
        if (curve == null || curve.length == 0)
        {
            return fallbackValue;
        }

        return curve.Evaluate(time);
    }

    private void OnDisable()
    {
        isPlaying = false;
        DisableAllCircles();
    }

    private void OnValidate()
    {
        circleCount = Mathf.Clamp(circleCount, 1, 128);
        sortingOrder = Mathf.Clamp(sortingOrder, -32768, 32767);
        directionVariationDegrees = Mathf.Clamp(directionVariationDegrees, 0f, 180f);
        lifetimeRange = ClampRange(lifetimeRange, 0.01f);
        startScaleRange = ClampRange(startScaleRange, 0.001f);
        endScaleMultiplierRange = ClampRange(endScaleMultiplierRange, 0.001f);
        startOpacityMultiplierRange = ClampRange(startOpacityMultiplierRange, 0f);
        speedRange = ClampRange(speedRange, 0f);
        spawnRadiusRange = ClampRange(spawnRadiusRange, 0f);

        if (!Application.isPlaying)
        {
            return;
        }

        EnsurePool();
        ApplySharedRendererSettings();
        RefreshActiveCircles();
    }

    private static Vector2 ClampRange(Vector2 range, float minimumValue)
    {
        float min = Mathf.Max(minimumValue, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return new Vector2(min, max);
    }
}
