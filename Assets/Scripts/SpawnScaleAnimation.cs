using UnityEngine;

[DisallowMultipleComponent]
public class SpawnScaleAnimation : MonoBehaviour
{
    [Tooltip("Transform to animate. If left empty, this GameObject's transform is used.")]
    [SerializeField] private Transform target;
    [Tooltip("If enabled, the animation starts automatically when this GameObject becomes active.")]
    [SerializeField] private bool playOnEnable;
    [Tooltip("If enabled, animation keeps running even when Time.timeScale is 0 or slowed down.")]
    [SerializeField] private bool useUnscaledTime;

    [Header("Scale")]
    [Tooltip("Scale multiplier applied at the start of the animation.")]
    [SerializeField] private float startScaleMultiplier = 0.2f;
    [Tooltip("Scale multiplier reached at the overshoot peak before settling.")]
    [SerializeField] private float peakScaleMultiplier = 1.18f;
    [Tooltip("Final settled scale multiplier after the overshoot.")]
    [SerializeField] private float settleScaleMultiplier = 1f;

    [Header("Timing")]
    [Tooltip("Time in seconds to grow from the start scale to the peak scale.")]
    [SerializeField] private float growDuration = 0.14f;
    [Tooltip("Time in seconds to shrink from the peak scale to the final settled scale.")]
    [SerializeField] private float settleDuration = 0.1f;
    [Tooltip("Curve used during the grow phase.")]
    [SerializeField] private AnimationCurve growCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(1f, 1f));
    [Tooltip("Curve used during the settle phase.")]
    [SerializeField] private AnimationCurve settleCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(1f, 1f));

    private Transform runtimeTarget;
    private Vector3 baseScale = Vector3.one;
    private float phaseElapsed;
    private bool isPlaying;
    private bool isSettling;

    private void Awake()
    {
        runtimeTarget = ResolveTarget();
        if (runtimeTarget != null)
        {
            baseScale = runtimeTarget.localScale;
        }
    }

    private void OnEnable()
    {
        if (playOnEnable)
        {
            Play();
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        runtimeTarget = ResolveTarget();
        if (runtimeTarget != null)
        {
            baseScale = runtimeTarget.localScale;
        }
    }

    public void Play()
    {
        runtimeTarget = ResolveTarget();
        if (runtimeTarget == null)
        {
            return;
        }

        baseScale = runtimeTarget.localScale;
        phaseElapsed = 0f;
        isSettling = false;
        isPlaying = true;
        ApplyScale(startScaleMultiplier);
        enabled = true;
    }

    private void Update()
    {
        if (!isPlaying || runtimeTarget == null)
        {
            return;
        }

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        phaseElapsed += deltaTime;
        if (!isSettling)
        {
            float duration = Mathf.Max(0.0001f, growDuration);
            float t = Mathf.Clamp01(phaseElapsed / duration);
            float eased = EvaluateCurve(growCurve, t);
            float scaleMultiplier = Mathf.LerpUnclamped(startScaleMultiplier, peakScaleMultiplier, eased);
            ApplyScale(scaleMultiplier);
            if (t >= 1f)
            {
                phaseElapsed = 0f;
                isSettling = true;
            }

            return;
        }

        float settleTime = Mathf.Max(0.0001f, settleDuration);
        float settleT = Mathf.Clamp01(phaseElapsed / settleTime);
        float settleEased = EvaluateCurve(settleCurve, settleT);
        float settleMultiplier = Mathf.LerpUnclamped(peakScaleMultiplier, settleScaleMultiplier, settleEased);
        ApplyScale(settleMultiplier);
        if (settleT >= 1f)
        {
            ApplyScale(settleScaleMultiplier);
            isPlaying = false;
            enabled = false;
        }
    }

    private Transform ResolveTarget()
    {
        return target != null ? target : transform;
    }

    private void ApplyScale(float multiplier)
    {
        if (runtimeTarget == null)
        {
            return;
        }

        runtimeTarget.localScale = baseScale * multiplier;
    }

    private static float EvaluateCurve(AnimationCurve curve, float time)
    {
        if (curve == null || curve.length == 0)
        {
            return time;
        }

        return curve.Evaluate(time);
    }

    private void OnDisable()
    {
        isPlaying = false;
        isSettling = false;
    }

    private void OnValidate()
    {
        startScaleMultiplier = Mathf.Max(0f, startScaleMultiplier);
        peakScaleMultiplier = Mathf.Max(0f, peakScaleMultiplier);
        settleScaleMultiplier = Mathf.Max(0f, settleScaleMultiplier);
        growDuration = Mathf.Max(0.0001f, growDuration);
        settleDuration = Mathf.Max(0.0001f, settleDuration);
    }
}
