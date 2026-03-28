using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(GridLayoutGroup))]
public class ResponsiveGridSizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridLayoutGroup targetGrid;
    [SerializeField] private RectTransform targetRect;

    [Header("Sizing")]
    [Tooltip("Reference size used to compute child scale.")]
    [SerializeField] private Vector2 referenceCellSize = new Vector2(75f, 50f);
    [Tooltip("Cell width / height ratio.")]
    [SerializeField] private float cellAspectRatio = 1.5f;
    [SerializeField] private Vector2 minCellSize = Vector2.zero;
    [SerializeField] private Vector2 maxCellSize = new Vector2(300f, 200f);
    [SerializeField] private bool includeInactiveChildren = false;
    [SerializeField] private bool scaleChildrenToCell = true;

    [Header("Responsive Spacing")]
    [SerializeField] private bool useResponsiveSpacing = true;
    [Tooltip("Spacing used when the grid has only a few items.")]
    [SerializeField] private Vector2 expandedSpacing = new Vector2(150f, 0f);
    [Tooltip("Spacing used when the grid is densely populated.")]
    [SerializeField] private Vector2 compactSpacing = new Vector2(18f, 0f);
    [Min(1)]
    [SerializeField] private int expandedSpacingItemCount = 2;
    [Min(1)]
    [SerializeField] private int compactSpacingItemCount = 20;

    [Header("Flexible Constraint")]
    [Min(1)]
    [SerializeField] private int fallbackRowCount = 1;

    [Header("Responsive Rows")]
    [SerializeField] private bool useResponsiveRowCount = true;
    [Min(1)]
    [SerializeField] private int itemsPerRowBeforeWrapping = 8;
    [Min(1)]
    [SerializeField] private int maxResponsiveRowCount = 2;

    private void Awake()
    {
        CacheReferences();
        Recalculate();
    }

    private void OnEnable()
    {
        CacheReferences();
        Recalculate();
    }

    private void OnValidate()
    {
        cellAspectRatio = Mathf.Max(0.01f, cellAspectRatio);
        minCellSize.x = Mathf.Max(0f, minCellSize.x);
        minCellSize.y = Mathf.Max(0f, minCellSize.y);
        maxCellSize.x = Mathf.Max(minCellSize.x, maxCellSize.x);
        maxCellSize.y = Mathf.Max(minCellSize.y, maxCellSize.y);
        expandedSpacing.x = Mathf.Max(0f, expandedSpacing.x);
        expandedSpacing.y = Mathf.Max(0f, expandedSpacing.y);
        compactSpacing.x = Mathf.Max(0f, compactSpacing.x);
        compactSpacing.y = Mathf.Max(0f, compactSpacing.y);
        expandedSpacingItemCount = Mathf.Max(1, expandedSpacingItemCount);
        compactSpacingItemCount = Mathf.Max(expandedSpacingItemCount, compactSpacingItemCount);
        fallbackRowCount = Mathf.Max(1, fallbackRowCount);
        itemsPerRowBeforeWrapping = Mathf.Max(1, itemsPerRowBeforeWrapping);
        maxResponsiveRowCount = Mathf.Max(1, maxResponsiveRowCount);
        CacheReferences();
        Recalculate();
    }

    private void OnRectTransformDimensionsChange()
    {
        Recalculate();
    }

    private void OnTransformChildrenChanged()
    {
        Recalculate();
    }

    public void Recalculate()
    {
        if (targetGrid == null || targetRect == null)
        {
            return;
        }

        int childCount = GetChildCount();
        if (childCount <= 0)
        {
            return;
        }

        int rows = ResolveRowCount(childCount);
        int cols = ResolveColumnCount(childCount, rows);
        Vector2 resolvedSpacing = ResolveSpacing(childCount);

        if (targetGrid.constraint == GridLayoutGroup.Constraint.FixedRowCount && targetGrid.constraintCount != rows)
        {
            targetGrid.constraintCount = rows;
        }

        if ((targetGrid.spacing - resolvedSpacing).sqrMagnitude > 0.0001f)
        {
            targetGrid.spacing = resolvedSpacing;
        }

        float availableWidth = targetRect.rect.width - targetGrid.padding.horizontal - resolvedSpacing.x * Mathf.Max(0, cols - 1);
        float availableHeight = targetRect.rect.height - targetGrid.padding.vertical - resolvedSpacing.y * Mathf.Max(0, rows - 1);

        if (availableWidth <= 0f || availableHeight <= 0f)
        {
            return;
        }

        float maxCellWidth = availableWidth / cols;
        float maxCellHeight = availableHeight / rows;

        float safeAspect = Mathf.Max(0.01f, cellAspectRatio);
        float widthFromHeight = maxCellHeight * safeAspect;
        float targetWidth = Mathf.Min(maxCellWidth, widthFromHeight);
        float targetHeight = targetWidth / safeAspect;

        float minWidth = Mathf.Max(0f, minCellSize.x);
        float minHeight = Mathf.Max(0f, minCellSize.y);
        float maxWidth = Mathf.Max(minWidth, maxCellSize.x);
        float maxHeight = Mathf.Max(minHeight, maxCellSize.y);

        Vector2 newCellSize = new Vector2(
            Mathf.Clamp(targetWidth, minWidth, maxWidth),
            Mathf.Clamp(targetHeight, minHeight, maxHeight)
        );

        if ((targetGrid.cellSize - newCellSize).sqrMagnitude > 0.0001f)
        {
            targetGrid.cellSize = newCellSize;
        }

        if (scaleChildrenToCell)
        {
            ApplyChildScale(newCellSize);
        }

        LayoutRebuilder.MarkLayoutForRebuild(targetRect);
    }

    private void CacheReferences()
    {
        if (targetGrid == null)
        {
            targetGrid = GetComponent<GridLayoutGroup>();
        }

        if (targetRect == null)
        {
            targetRect = transform as RectTransform;
        }
    }

    private int GetChildCount()
    {
        if (includeInactiveChildren)
        {
            return transform.childCount;
        }

        int count = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            if (transform.GetChild(i).gameObject.activeInHierarchy)
            {
                count++;
            }
        }

        return count;
    }

    private int ResolveRowCount(int childCount)
    {
        if (useResponsiveRowCount && targetGrid.constraint != GridLayoutGroup.Constraint.FixedColumnCount)
        {
            int wrappedRows = Mathf.CeilToInt(childCount / (float)Mathf.Max(1, itemsPerRowBeforeWrapping));
            return Mathf.Clamp(wrappedRows, 1, Mathf.Max(1, maxResponsiveRowCount));
        }

        switch (targetGrid.constraint)
        {
            case GridLayoutGroup.Constraint.FixedRowCount:
                return Mathf.Max(1, targetGrid.constraintCount);
            case GridLayoutGroup.Constraint.FixedColumnCount:
            {
                int cols = Mathf.Max(1, targetGrid.constraintCount);
                return Mathf.Max(1, Mathf.CeilToInt(childCount / (float)cols));
            }
            default:
                return Mathf.Max(1, Mathf.Min(fallbackRowCount, childCount));
        }
    }

    private int ResolveColumnCount(int childCount, int rows)
    {
        if (targetGrid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
        {
            return Mathf.Max(1, targetGrid.constraintCount);
        }

        return Mathf.Max(1, Mathf.CeilToInt(childCount / (float)Mathf.Max(1, rows)));
    }

    private Vector2 ResolveSpacing(int childCount)
    {
        if (!useResponsiveSpacing)
        {
            return targetGrid != null ? targetGrid.spacing : Vector2.zero;
        }

        int expandedCount = Mathf.Max(1, expandedSpacingItemCount);
        int compactCount = Mathf.Max(expandedCount, compactSpacingItemCount);
        if (compactCount == expandedCount)
        {
            return compactSpacing;
        }

        float t = Mathf.InverseLerp(expandedCount, compactCount, Mathf.Max(1, childCount));
        t = Mathf.SmoothStep(0f, 1f, t);

        return new Vector2(
            Mathf.Lerp(expandedSpacing.x, compactSpacing.x, t),
            Mathf.Lerp(expandedSpacing.y, compactSpacing.y, t));
    }

    private void ApplyChildScale(Vector2 cellSize)
    {
        float referenceWidth = Mathf.Max(0.01f, referenceCellSize.x);
        float referenceHeight = Mathf.Max(0.01f, referenceCellSize.y);
        float scale = Mathf.Min(cellSize.x / referenceWidth, cellSize.y / referenceHeight);
        Vector3 targetScale = new Vector3(scale, scale, 1f);

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (includeInactiveChildren || child.gameObject.activeInHierarchy)
            {
                if ((child.localScale - targetScale).sqrMagnitude > 0.0001f)
                {
                    child.localScale = targetScale;
                }
            }
        }
    }
}
