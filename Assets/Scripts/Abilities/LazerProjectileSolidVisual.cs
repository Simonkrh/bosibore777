using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
[RequireComponent(typeof(LazerProjectileBounce))]
public class LazerProjectileSolidVisual : MonoBehaviour
{
    [SerializeField] private float beamWidth = 0.075f;
    [SerializeField] private float segmentLength = 0.14f;
    [SerializeField] private float beamLifetime = 0.5f;
    [SerializeField] private int sortingOrder = 99;
    [SerializeField] private bool hideProjectileSprite = true;
    [SerializeField] private int maxSegments = 256;

    private SpriteRenderer[] initialProjectileRenderers;
    private SpriteRenderer sourceColorRenderer;
    private readonly List<SpriteRenderer> segmentPool = new List<SpriteRenderer>(128);
    private LazerProjectileBounce bounce;
    private Sprite runtimeRectSprite;
    private Texture2D runtimeRectTexture;
    private Material rectMaterial;

    private const float MinDistanceEpsilon = 0.0001f;
    public float BeamLifetime => beamLifetime;

    private void Awake()
    {
        bounce = GetComponent<LazerProjectileBounce>();
        initialProjectileRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        sourceColorRenderer = ResolveSourceColorRenderer(initialProjectileRenderers);

        if (hideProjectileSprite && initialProjectileRenderers != null)
        {
            for (int i = 0; i < initialProjectileRenderers.Length; i++)
            {
                if (initialProjectileRenderers[i] != null)
                {
                    initialProjectileRenderers[i].enabled = false;
                }
            }
        }

        rectMaterial = CreateSpriteMaterial();
        runtimeRectSprite = CreateRectSprite(out runtimeRectTexture);
        if (rectMaterial == null || runtimeRectSprite == null)
        {
            enabled = false;
        }
    }

    private void LateUpdate()
    {
        DisableAllSegments();
        if (runtimeRectSprite == null || rectMaterial == null || bounce == null || !bounce.HasPath)
        {
            return;
        }

        IReadOnlyList<Vector2> pathPoints = bounce.PathPoints;
        if (pathPoints == null || pathPoints.Count < 2)
        {
            return;
        }

        float totalPathLength = Mathf.Max(0f, bounce.TotalPathLength);
        if (totalPathLength <= MinDistanceEpsilon)
        {
            return;
        }

        float headDistance = bounce.PathCompleted
            ? totalPathLength
            : Mathf.Clamp(bounce.TraveledDistance, 0f, totalPathLength);
        float visibleLength = Mathf.Max(0f, beamLifetime) * Mathf.Max(0f, bounce.CurrentSpeed);
        float tailDistance = Mathf.Max(0f, headDistance - visibleLength);
        if (bounce.PathCompleted)
        {
            if (beamLifetime <= MinDistanceEpsilon)
            {
                return;
            }

            float completionT = Mathf.Clamp01(bounce.PathCompletionElapsed / beamLifetime);
            tailDistance = Mathf.Lerp(tailDistance, headDistance, completionT);
        }

        if (headDistance - tailDistance <= MinDistanceEpsilon)
        {
            return;
        }

        if (sourceColorRenderer == null)
        {
            sourceColorRenderer = ResolveSourceColorRenderer(initialProjectileRenderers);
        }

        Color beamColor = sourceColorRenderer != null ? sourceColorRenderer.color : Color.white;
        Vector3 worldToLocalScale = GetWorldToLocalScaleCompensation();
        RenderPathRange(pathPoints, tailDistance, headDistance, beamColor, worldToLocalScale);
    }

    private void RenderPathRange(
        IReadOnlyList<Vector2> pathPoints,
        float fromDistance,
        float toDistance,
        Color beamColor,
        Vector3 worldToLocalScale)
    {
        int activeSegmentCount = 0;
        float cumulativeDistance = 0f;

        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            Vector2 a = pathPoints[i];
            Vector2 b = pathPoints[i + 1];
            Vector2 segmentDelta = b - a;
            float segmentWorldLength = segmentDelta.magnitude;
            if (segmentWorldLength <= MinDistanceEpsilon)
            {
                continue;
            }

            float segmentStartDistance = cumulativeDistance;
            float segmentEndDistance = cumulativeDistance + segmentWorldLength;
            float overlapStart = Mathf.Max(fromDistance, segmentStartDistance);
            float overlapEnd = Mathf.Min(toDistance, segmentEndDistance);
            if (overlapEnd - overlapStart <= MinDistanceEpsilon)
            {
                cumulativeDistance = segmentEndDistance;
                continue;
            }

            float localStartT = (overlapStart - segmentStartDistance) / segmentWorldLength;
            float localEndT = (overlapEnd - segmentStartDistance) / segmentWorldLength;
            Vector2 partStart = Vector2.Lerp(a, b, localStartT);
            Vector2 partEnd = Vector2.Lerp(a, b, localEndT);

            activeSegmentCount = RenderSolidStrip(
                partStart,
                partEnd,
                beamColor,
                worldToLocalScale,
                activeSegmentCount);
            if (activeSegmentCount >= maxSegments)
            {
                return;
            }

            cumulativeDistance = segmentEndDistance;
        }
    }

    private int RenderSolidStrip(
        Vector2 start,
        Vector2 end,
        Color beamColor,
        Vector3 worldToLocalScale,
        int activeSegmentCount)
    {
        Vector2 delta = end - start;
        float worldLength = delta.magnitude;
        if (worldLength <= MinDistanceEpsilon)
        {
            return activeSegmentCount;
        }

        Vector2 direction = delta / worldLength;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        float cursor = 0f;

        while (cursor < worldLength && activeSegmentCount < maxSegments)
        {
            float partLength = Mathf.Min(segmentLength, worldLength - cursor);
            if (partLength <= MinDistanceEpsilon)
            {
                break;
            }

            float centerDistance = cursor + partLength * 0.5f;
            Vector2 center = start + direction * centerDistance;

            SpriteRenderer segmentRenderer = GetSegmentRenderer(activeSegmentCount);
            segmentRenderer.transform.position = center;
            segmentRenderer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            segmentRenderer.transform.localScale = new Vector3(
                partLength * worldToLocalScale.x,
                beamWidth * worldToLocalScale.y,
                1f);
            segmentRenderer.color = beamColor;
            segmentRenderer.enabled = true;

            activeSegmentCount++;
            cursor += partLength;
        }

        return activeSegmentCount;
    }

    private SpriteRenderer GetSegmentRenderer(int index)
    {
        while (segmentPool.Count <= index)
        {
            GameObject segmentObject = new GameObject($"SolidBeamSegment_{segmentPool.Count:D3}");
            segmentObject.transform.SetParent(transform, false);
            SpriteRenderer segmentRenderer = segmentObject.AddComponent<SpriteRenderer>();
            segmentRenderer.sprite = runtimeRectSprite;
            segmentRenderer.material = rectMaterial;
            segmentRenderer.sortingOrder = sortingOrder;
            segmentRenderer.drawMode = SpriteDrawMode.Simple;
            segmentRenderer.enabled = false;
            segmentPool.Add(segmentRenderer);
        }

        return segmentPool[index];
    }

    private Vector3 GetWorldToLocalScaleCompensation()
    {
        Vector3 lossy = transform.lossyScale;
        float invX = 1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
        float invY = 1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
        return new Vector3(invX, invY, 1f);
    }

    private void DisableAllSegments()
    {
        for (int i = 0; i < segmentPool.Count; i++)
        {
            if (segmentPool[i] != null)
            {
                segmentPool[i].enabled = false;
            }
        }
    }

    private static Material CreateSpriteMaterial()
    {
        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader == null)
        {
            return null;
        }

        return new Material(spriteShader);
    }

    private static Sprite CreateRectSprite(out Texture2D texture)
    {
        texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);
        texture.hideFlags = HideFlags.HideAndDontSave;
        return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
    }

    private static SpriteRenderer ResolveSourceColorRenderer(SpriteRenderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                return renderers[i];
            }
        }

        return null;
    }

    private void OnDestroy()
    {
        if (runtimeRectSprite != null)
        {
            Destroy(runtimeRectSprite);
            runtimeRectSprite = null;
        }

        if (runtimeRectTexture != null)
        {
            Destroy(runtimeRectTexture);
            runtimeRectTexture = null;
        }

        if (rectMaterial != null)
        {
            Destroy(rectMaterial);
            rectMaterial = null;
        }
    }

    private void OnValidate()
    {
        beamWidth = Mathf.Max(0.001f, beamWidth);
        segmentLength = Mathf.Max(0.01f, segmentLength);
        beamLifetime = Mathf.Max(0f, beamLifetime);
        sortingOrder = Mathf.Clamp(sortingOrder, -32768, 32767);
        maxSegments = Mathf.Clamp(maxSegments, 1, 2048);
    }

    private void OnDisable()
    {
        DisableAllSegments();
    }
}
