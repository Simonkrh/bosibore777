using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CameraShakeController : MonoBehaviour
{
    private struct ActiveShake
    {
        public float Amplitude;
        public float Duration;
        public float Frequency;
        public float Elapsed;
        public Vector2 NoiseSeed;
    }

    [SerializeField] private float maxCombinedOffset = 0.3f;

    private readonly List<ActiveShake> activeShakes = new List<ActiveShake>();
    private Vector3 lastAppliedOffset;

    public void Shake(float amplitude, float duration, float frequency)
    {
        if (amplitude <= 0f || duration <= 0f)
        {
            return;
        }

        activeShakes.Add(new ActiveShake
        {
            Amplitude = Mathf.Max(0f, amplitude),
            Duration = Mathf.Max(0.01f, duration),
            Frequency = Mathf.Max(0.01f, frequency),
            Elapsed = 0f,
            NoiseSeed = new Vector2(
                UnityEngine.Random.Range(-1000f, 1000f),
                UnityEngine.Random.Range(-1000f, 1000f))
        });
    }

    private void LateUpdate()
    {
        Vector3 baseLocalPosition = transform.localPosition - lastAppliedOffset;
        lastAppliedOffset = Vector3.zero;

        if (activeShakes.Count == 0)
        {
            transform.localPosition = baseLocalPosition;
            return;
        }

        float time = Time.unscaledTime;
        Vector2 combinedOffset = Vector2.zero;

        for (int i = activeShakes.Count - 1; i >= 0; i--)
        {
            ActiveShake shake = activeShakes[i];
            shake.Elapsed += Time.unscaledDeltaTime;

            float normalizedRemaining = 1f - Mathf.Clamp01(shake.Elapsed / shake.Duration);
            if (normalizedRemaining <= 0f)
            {
                activeShakes.RemoveAt(i);
                continue;
            }

            float strength = normalizedRemaining * normalizedRemaining;
            float sampleTime = time * shake.Frequency;
            float offsetX =
                (Mathf.PerlinNoise(shake.NoiseSeed.x, sampleTime) * 2f - 1f) *
                shake.Amplitude *
                strength;
            float offsetY =
                (Mathf.PerlinNoise(shake.NoiseSeed.y, sampleTime) * 2f - 1f) *
                shake.Amplitude *
                strength;
            combinedOffset += new Vector2(offsetX, offsetY);

            activeShakes[i] = shake;
        }

        float maxOffset = Mathf.Max(0f, maxCombinedOffset);
        if (combinedOffset.sqrMagnitude > maxOffset * maxOffset)
        {
            combinedOffset = combinedOffset.normalized * maxOffset;
        }

        lastAppliedOffset = new Vector3(combinedOffset.x, combinedOffset.y, 0f);
        transform.localPosition = baseLocalPosition + lastAppliedOffset;
    }

    private void OnDisable()
    {
        if (lastAppliedOffset != Vector3.zero)
        {
            transform.localPosition -= lastAppliedOffset;
            lastAppliedOffset = Vector3.zero;
        }

        activeShakes.Clear();
    }

    private void OnValidate()
    {
        maxCombinedOffset = Mathf.Max(0f, maxCombinedOffset);
    }
}
