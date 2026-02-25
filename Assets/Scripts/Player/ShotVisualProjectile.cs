using UnityEngine;

public class ShotVisualProjectile : MonoBehaviour
{
    private Vector2 direction;
    private float speed;
    private float lifetime;
    private int maxWallBounces;
    private int wallBounceCount;
    private int wallMask;
    private float elapsed;
    private bool configured;
    private SpriteRenderer spriteRenderer;
    private Color baseColor = Color.white;

    private const float CastSkin = 0.0015f;
    private const int MaxBounceChecksPerFrame = 3;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
        }
    }

    public void Configure(
        Vector2 startPosition,
        Vector2 moveDirection,
        float moveSpeed,
        float maxLifetime,
        int maxBounces,
        int wallsLayerMask,
        float alphaMultiplier)
    {
        transform.position = startPosition;

        direction = moveDirection.sqrMagnitude > 0.0001f ? moveDirection.normalized : Vector2.up;
        speed = Mathf.Max(0f, moveSpeed);
        lifetime = Mathf.Max(0.03f, maxLifetime);
        maxWallBounces = maxBounces;
        wallMask = wallsLayerMask;
        wallBounceCount = 0;
        elapsed = 0f;
        configured = true;

        if (spriteRenderer != null)
        {
            Color color = baseColor;
            color.a *= Mathf.Clamp01(alphaMultiplier);
            spriteRenderer.color = color;
        }

        transform.up = direction;
    }

    private void Update()
    {
        if (!configured)
        {
            return;
        }

        float delta = Time.deltaTime;
        elapsed += delta;
        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        float remainingDistance = speed * delta;
        Vector2 currentPosition = transform.position;
        int bounceChecks = 0;

        while (remainingDistance > 0f && bounceChecks < MaxBounceChecksPerFrame)
        {
            bounceChecks++;

            if (wallMask == 0)
            {
                currentPosition += direction * remainingDistance;
                remainingDistance = 0f;
                continue;
            }

            RaycastHit2D hit = Physics2D.Raycast(currentPosition, direction, remainingDistance + CastSkin, wallMask);
            if (hit.collider == null)
            {
                currentPosition += direction * remainingDistance;
                remainingDistance = 0f;
                continue;
            }

            float travelDistance = Mathf.Max(0f, hit.distance - CastSkin);
            currentPosition += direction * travelDistance;
            remainingDistance -= travelDistance;

            direction = Vector2.Reflect(direction, hit.normal).normalized;
            wallBounceCount++;
            if (maxWallBounces >= 0 && wallBounceCount > maxWallBounces)
            {
                Destroy(gameObject);
                return;
            }

            currentPosition += direction * CastSkin;
        }

        transform.position = currentPosition;
        transform.up = direction;

    }
}
