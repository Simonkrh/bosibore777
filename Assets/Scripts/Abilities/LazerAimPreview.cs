using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class LazerAimPreview : MonoBehaviour
{
    [Header("Beam")]
    [SerializeField] private float beamWidth = 0.055f;
    [SerializeField] private float beamAlpha = 0.65f;
    [SerializeField] private int sortingOrder = 120;
    [SerializeField] private int previewReflectionSafetyLimit = 24;

    [Header("Flicker")]
    [SerializeField] private float minRectLength = 0.04f;
    [SerializeField] private float maxRectLength = 0.35f;
    [SerializeField] private float minGapLength = 0.01f;
    [SerializeField] private float maxGapLength = 0.14f;
    [SerializeField] private int maxRectsPerFrame = 200;

    private TankController ownerTank;
    private readonly List<Vector3> pathPoints = new List<Vector3>(32);
    private readonly List<SpriteRenderer> rectPool = new List<SpriteRenderer>(64);
    private Sprite runtimeRectSprite;
    private Texture2D runtimeRectTexture;
    private Material rectMaterial;

    private const float MinDistanceEpsilon = 0.0001f;
    private const float SurfacePushEpsilon = 0.002f;

    private void Awake()
    {
        rectMaterial = CreateSpriteMaterial();
        runtimeRectSprite = CreateRectSprite(out runtimeRectTexture);
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

    private void LateUpdate()
    {
        DisableAllRects();

        if (!TryResolveOwnerTank() || !TryResolveLazerConfig(
                out GameObject projectilePrefab,
                out float previewLength,
                out float projectileMaxDistance,
                out float spawnOffset))
        {
            return;
        }

        float clampedPreviewLength = Mathf.Max(0f, previewLength);
        float clampedProjectileMaxDistance = Mathf.Max(0f, projectileMaxDistance);
        float predictionDistance = clampedProjectileMaxDistance > 0f
            ? Mathf.Min(clampedPreviewLength, clampedProjectileMaxDistance)
            : clampedPreviewLength;
        if (predictionDistance <= 0f)
        {
            return;
        }

        if (!ownerTank.TryComputeAbilityProjectilePreviewSpawn(
                projectilePrefab,
                spawnOffset,
                out Vector2 spawnPosition,
                out _,
                out Vector2 fireDirection,
                out float projectileRadius))
        {
            return;
        }

        ComputePredictedImpactPoint(
            projectilePrefab,
            spawnPosition,
            fireDirection,
            projectileRadius,
            predictionDistance,
            pathPoints);

        if (pathPoints.Count < 2)
        {
            return;
        }

        Color tankColor = ownerTank.tankColor.Value;
        Color beamColor = new Color(tankColor.r, tankColor.g, tankColor.b, Mathf.Clamp01(beamAlpha));
        RenderFlickerPath(pathPoints, beamColor);
    }

    private bool TryResolveOwnerTank()
    {
        if (ownerTank != null)
        {
            return true;
        }

        ownerTank = GetComponentInParent<TankController>();
        return ownerTank != null;
    }

    private static bool TryResolveLazerConfig(
        out GameObject projectilePrefab,
        out float previewLength,
        out float projectileMaxDistance,
        out float spawnOffset)
    {
        return LazerAbilityBehavior.TryGetRuntimeConfig(
            out projectilePrefab,
            out previewLength,
            out projectileMaxDistance,
            out spawnOffset);
    }

    private Vector2 ComputePredictedImpactPoint(
        GameObject projectilePrefab,
        Vector2 origin,
        Vector2 direction,
        float projectileRadius,
        float maxDistance,
        List<Vector3> outPath)
    {
        outPath.Clear();
        outPath.Add(origin);

        Vector2 currentOrigin = origin;
        Vector2 currentDirection = direction.normalized;
        float remainingDistance = Mathf.Max(0f, maxDistance);
        int remainingReflections = ResolveRemainingReflections(projectilePrefab);
        int safetyIterations = Mathf.Max(4, previewReflectionSafetyLimit * 4);
        Collider2D previousReflectedWall = null;

        while (remainingDistance > MinDistanceEpsilon && safetyIterations-- > 0)
        {
            if (!TryFindClosestBlockingHit(
                    currentOrigin,
                    currentDirection,
                    projectileRadius,
                    remainingDistance,
                    previousReflectedWall,
                    out RaycastHit2D closestHit))
            {
                Vector2 terminalPoint = currentOrigin + currentDirection * remainingDistance;
                outPath.Add(terminalPoint);
                return terminalPoint;
            }

            float traveled = Mathf.Max(0f, closestHit.distance);
            Vector2 hitCenterPoint = currentOrigin + currentDirection * traveled;
            Vector2 hitVisualPoint = hitCenterPoint + currentDirection * Mathf.Max(0f, projectileRadius);
            outPath.Add(hitVisualPoint);

            if (!IsWallCollider(closestHit.collider) || remainingReflections <= 0)
            {
                return hitVisualPoint;
            }

            previousReflectedWall = closestHit.collider;
            remainingReflections--;
            remainingDistance -= traveled;
            if (remainingDistance <= MinDistanceEpsilon)
            {
                return hitVisualPoint;
            }

            Vector2 normal = closestHit.normal.sqrMagnitude > MinDistanceEpsilon
                ? closestHit.normal.normalized
                : -currentDirection;
            currentDirection = Vector2.Reflect(currentDirection, normal).normalized;
            currentOrigin = hitCenterPoint + normal * SurfacePushEpsilon + currentDirection * SurfacePushEpsilon;
            remainingDistance = Mathf.Max(0f, remainingDistance - (SurfacePushEpsilon * 2f));
        }

        Vector2 fallbackPoint = currentOrigin + currentDirection * remainingDistance;
        outPath.Add(fallbackPoint);
        return fallbackPoint;
    }

    private int ResolveRemainingReflections(GameObject projectilePrefab)
    {
        Projectile projectile = projectilePrefab != null ? projectilePrefab.GetComponent<Projectile>() : null;
        int configuredBounces = projectile != null ? projectile.maxWallBounces : -1;
        if (configuredBounces < 0)
        {
            return Mathf.Max(0, previewReflectionSafetyLimit);
        }

        return Mathf.Clamp(configuredBounces, 0, Mathf.Max(0, previewReflectionSafetyLimit));
    }

    private bool TryFindClosestBlockingHit(
        Vector2 origin,
        Vector2 direction,
        float radius,
        float maxDistance,
        Collider2D previousReflectedWall,
        out RaycastHit2D closestHit)
    {
        closestHit = default;
        bool found = false;
        float closestDistance = float.PositiveInfinity;

        RaycastHit2D[] hits = Physics2D.CircleCastAll(
            origin,
            Mathf.Max(0.001f, radius),
            direction,
            maxDistance);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hitCollider = hits[i].collider;
            if (!IsBlockingCollider(hitCollider))
            {
                continue;
            }

            float hitDistance = Mathf.Max(0f, hits[i].distance);
            if (previousReflectedWall != null &&
                hitCollider == previousReflectedWall &&
                hitDistance <= SurfacePushEpsilon * 2f)
            {
                continue;
            }

            if (found && hitDistance >= closestDistance)
            {
                continue;
            }

            closestDistance = hitDistance;
            closestHit = hits[i];
            found = true;
        }

        return found;
    }

    private static bool IsWallCollider(Collider2D collider)
    {
        return collider != null && collider.CompareTag("Wall");
    }

    private bool IsBlockingCollider(Collider2D collider)
    {
        if (collider == null)
        {
            return false;
        }

        if (collider.GetComponentInParent<Projectile>() != null)
        {
            return false;
        }

        if (collider.GetComponentInParent<PlayerController>() != null)
        {
            return true;
        }

        if (collider.CompareTag("Wall"))
        {
            return true;
        }

        return !collider.isTrigger;
    }

    private void RenderFlickerPath(List<Vector3> points, Color beamColor)
    {
        if (runtimeRectSprite == null || rectMaterial == null)
        {
            return;
        }

        int activeRectCount = 0;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 segmentStart = points[i];
            Vector2 segmentEnd = points[i + 1];
            Vector2 segmentDelta = segmentEnd - segmentStart;
            float segmentLength = segmentDelta.magnitude;
            if (segmentLength <= MinDistanceEpsilon)
            {
                continue;
            }

            Vector2 segmentDirection = segmentDelta / segmentLength;
            float cursor = 0f;
            while (cursor < segmentLength && activeRectCount < maxRectsPerFrame)
            {
                float rectLength = Random.Range(minRectLength, maxRectLength);
                float clampedRectLength = Mathf.Min(rectLength, segmentLength - cursor);
                if (clampedRectLength <= MinDistanceEpsilon)
                {
                    break;
                }

                float rectCenterDistance = cursor + clampedRectLength * 0.5f;
                Vector2 rectCenter = segmentStart + segmentDirection * rectCenterDistance;
                float angle = Mathf.Atan2(segmentDirection.y, segmentDirection.x) * Mathf.Rad2Deg;

                SpriteRenderer rectRenderer = GetRectRenderer(activeRectCount);
                rectRenderer.transform.position = rectCenter;
                rectRenderer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                rectRenderer.transform.localScale = new Vector3(clampedRectLength, beamWidth, 1f);
                rectRenderer.color = beamColor;
                rectRenderer.enabled = true;
                activeRectCount++;

                float gapLength = Random.Range(minGapLength, maxGapLength);
                cursor += clampedRectLength + gapLength;
            }

            if (activeRectCount >= maxRectsPerFrame)
            {
                break;
            }
        }
    }

    private SpriteRenderer GetRectRenderer(int index)
    {
        while (rectPool.Count <= index)
        {
            GameObject rectObject = new GameObject($"PreviewRect_{rectPool.Count:D3}");
            rectObject.transform.SetParent(transform, false);
            SpriteRenderer rectRenderer = rectObject.AddComponent<SpriteRenderer>();
            rectRenderer.sprite = runtimeRectSprite;
            rectRenderer.material = rectMaterial;
            rectRenderer.sortingOrder = sortingOrder;
            rectRenderer.drawMode = SpriteDrawMode.Simple;
            rectRenderer.enabled = false;
            rectPool.Add(rectRenderer);
        }

        return rectPool[index];
    }

    private void DisableAllRects()
    {
        for (int i = 0; i < rectPool.Count; i++)
        {
            if (rectPool[i] != null)
            {
                rectPool[i].enabled = false;
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

    private void OnValidate()
    {
        beamWidth = Mathf.Max(0.001f, beamWidth);
        beamAlpha = Mathf.Clamp01(beamAlpha);
        previewReflectionSafetyLimit = Mathf.Max(0, previewReflectionSafetyLimit);

        minRectLength = Mathf.Max(0.001f, minRectLength);
        maxRectLength = Mathf.Max(minRectLength, maxRectLength);
        minGapLength = Mathf.Max(0f, minGapLength);
        maxGapLength = Mathf.Max(minGapLength, maxGapLength);
        maxRectsPerFrame = Mathf.Clamp(maxRectsPerFrame, 1, 1024);
    }
}
