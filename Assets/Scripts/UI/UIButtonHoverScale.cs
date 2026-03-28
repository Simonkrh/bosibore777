using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class UIButtonHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    [Tooltip("Optional target to scale. Defaults to this object's RectTransform.")]
    [SerializeField] private RectTransform scaleTarget;
    [Tooltip("Scale multiplier applied while the pointer is hovering.")]
    [SerializeField] private float hoverScaleMultiplier = 1.08f;
    [Tooltip("Extra scale multiplier used while the button is pressed.")]
    [SerializeField] private float pressedScaleMultiplier = 1.03f;
    [Tooltip("How quickly the scale eases toward the target value.")]
    [SerializeField] private float animationSpeed = 14f;
    [Tooltip("If enabled, the effect still animates while Time.timeScale is 0.")]
    [SerializeField] private bool useUnscaledTime = true;
    [Tooltip("If enabled, keyboard/controller selection also uses the hover scale.")]
    [SerializeField] private bool reactToSelection = true;

    private RectTransform runtimeTarget;
    private Selectable selectable;
    private Vector3 baseScale = Vector3.one;
    private bool isHovered;
    private bool isPressed;
    private bool isSelected;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        ResolveTarget();
        CacheBaseScale();
    }

    private void OnEnable()
    {
        if (selectable == null)
        {
            selectable = GetComponent<Selectable>();
        }

        ResolveTarget();
        CacheBaseScale();
        ApplyImmediateScale();
    }

    private void OnDisable()
    {
        if (runtimeTarget != null)
        {
            runtimeTarget.localScale = baseScale;
        }

        isHovered = false;
        isPressed = false;
        isSelected = false;
    }

    private void Update()
    {
        ResolveTarget();
        if (runtimeTarget == null)
        {
            return;
        }

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        Vector3 desiredScale = baseScale * ResolveTargetScaleMultiplier();
        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, animationSpeed) * deltaTime);
        runtimeTarget.localScale = Vector3.Lerp(runtimeTarget.localScale, desiredScale, t);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!CanReact())
        {
            return;
        }

        isHovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        isPressed = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanReact())
        {
            return;
        }

        isPressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPressed = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (!reactToSelection || !CanReact())
        {
            return;
        }

        isSelected = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        isSelected = false;
        isPressed = false;
    }

    private void ResolveTarget()
    {
        runtimeTarget = scaleTarget != null ? scaleTarget : transform as RectTransform;
    }

    private void CacheBaseScale()
    {
        if (runtimeTarget == null)
        {
            return;
        }

        baseScale = runtimeTarget.localScale;
        if (baseScale == Vector3.zero)
        {
            baseScale = Vector3.one;
            runtimeTarget.localScale = baseScale;
        }
    }

    private bool CanReact()
    {
        return selectable == null || selectable.IsInteractable();
    }

    private float ResolveTargetScaleMultiplier()
    {
        bool shouldHover = isHovered || (reactToSelection && isSelected);
        if (!shouldHover)
        {
            return 1f;
        }

        if (isPressed)
        {
            return Mathf.Max(1f, pressedScaleMultiplier);
        }

        return Mathf.Max(1f, hoverScaleMultiplier);
    }

    private void ApplyImmediateScale()
    {
        if (runtimeTarget == null)
        {
            return;
        }

        runtimeTarget.localScale = baseScale * ResolveTargetScaleMultiplier();
    }

    private void OnValidate()
    {
        hoverScaleMultiplier = Mathf.Max(1f, hoverScaleMultiplier);
        pressedScaleMultiplier = Mathf.Max(1f, pressedScaleMultiplier);
        animationSpeed = Mathf.Max(0.01f, animationSpeed);
    }
}
