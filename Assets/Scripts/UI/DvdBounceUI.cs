using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class DvdBounceUI : MonoBehaviour
{
    [Tooltip("Optional bounds rect. If left empty, the root canvas rect is used.")]
    [SerializeField] private RectTransform movementBounds;
    [Tooltip("Movement speed in UI units per second.")]
    [SerializeField] private Vector2 speed = new Vector2(320f, 220f);
    [Tooltip("Randomizes the starting travel direction when the object becomes active.")]
    [SerializeField] private bool randomizeStartDirection = true;
    [Tooltip("Extra padding from the edges of the movement bounds.")]
    [SerializeField] private float edgePadding = 0f;
    [Tooltip("If enabled, continues moving even when Time.timeScale is 0.")]
    [SerializeField] private bool useUnscaledTime = true;

    private RectTransform rectTransform;
    private Vector2 currentVelocity;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        ResetVelocity();
        ClampInsideBounds();
    }

    private void OnEnable()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (currentVelocity == Vector2.zero)
        {
            ResetVelocity();
        }

        ClampInsideBounds();
    }

    private void Update()
    {
        RectTransform bounds = ResolveMovementBounds();
        if (rectTransform == null || bounds == null)
        {
            return;
        }

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        Rect boundsRect = bounds.rect;
        Vector2 halfSize = GetHalfSize();
        float minX = boundsRect.xMin + halfSize.x + edgePadding;
        float maxX = boundsRect.xMax - halfSize.x - edgePadding;
        float minY = boundsRect.yMin + halfSize.y + edgePadding;
        float maxY = boundsRect.yMax - halfSize.y - edgePadding;

        if (minX > maxX || minY > maxY)
        {
            return;
        }

        Vector2 nextPosition = rectTransform.anchoredPosition + currentVelocity * deltaTime;
        if (nextPosition.x < minX)
        {
            nextPosition.x = minX + (minX - nextPosition.x);
            currentVelocity.x = Mathf.Abs(currentVelocity.x);
        }
        else if (nextPosition.x > maxX)
        {
            nextPosition.x = maxX - (nextPosition.x - maxX);
            currentVelocity.x = -Mathf.Abs(currentVelocity.x);
        }

        if (nextPosition.y < minY)
        {
            nextPosition.y = minY + (minY - nextPosition.y);
            currentVelocity.y = Mathf.Abs(currentVelocity.y);
        }
        else if (nextPosition.y > maxY)
        {
            nextPosition.y = maxY - (nextPosition.y - maxY);
            currentVelocity.y = -Mathf.Abs(currentVelocity.y);
        }

        rectTransform.anchoredPosition = nextPosition;
    }

    private RectTransform ResolveMovementBounds()
    {
        if (movementBounds != null)
        {
            return movementBounds;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.rootCanvas != null)
        {
            return canvas.rootCanvas.transform as RectTransform;
        }

        return rectTransform != null ? rectTransform.parent as RectTransform : null;
    }

    private Vector2 GetHalfSize()
    {
        Vector3 localScale = rectTransform != null ? rectTransform.localScale : Vector3.one;
        return new Vector2(
            rectTransform.rect.width * Mathf.Abs(localScale.x) * 0.5f,
            rectTransform.rect.height * Mathf.Abs(localScale.y) * 0.5f);
    }

    private void ResetVelocity()
    {
        float resolvedX = Mathf.Max(1f, Mathf.Abs(speed.x));
        float resolvedY = Mathf.Max(1f, Mathf.Abs(speed.y));
        currentVelocity = new Vector2(resolvedX, resolvedY);

        if (randomizeStartDirection)
        {
            currentVelocity.x *= Random.value < 0.5f ? -1f : 1f;
            currentVelocity.y *= Random.value < 0.5f ? -1f : 1f;
        }
    }

    private void ClampInsideBounds()
    {
        RectTransform bounds = ResolveMovementBounds();
        if (rectTransform == null || bounds == null)
        {
            return;
        }

        Rect boundsRect = bounds.rect;
        Vector2 halfSize = GetHalfSize();
        float minX = boundsRect.xMin + halfSize.x + edgePadding;
        float maxX = boundsRect.xMax - halfSize.x - edgePadding;
        float minY = boundsRect.yMin + halfSize.y + edgePadding;
        float maxY = boundsRect.yMax - halfSize.y - edgePadding;

        Vector2 clampedPosition = rectTransform.anchoredPosition;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, minX, maxX);
        clampedPosition.y = Mathf.Clamp(clampedPosition.y, minY, maxY);
        rectTransform.anchoredPosition = clampedPosition;
    }

    private void OnValidate()
    {
        speed.x = Mathf.Max(1f, Mathf.Abs(speed.x));
        speed.y = Mathf.Max(1f, Mathf.Abs(speed.y));
        edgePadding = Mathf.Max(0f, edgePadding);
    }
}
