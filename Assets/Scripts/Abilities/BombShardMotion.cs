using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class BombShardMotion : MonoBehaviour
{
    private const float MinDirectionSqr = 0.000001f;

    private Rigidbody2D rb;
    private int wallMask;
    private float baseSpeed;
    private float spinSpeed;
    private float overWallSpeedMultiplier = 0.15f;
    private float overWallProbeRadius = 0.04f;
    private float overWallSpeedTransitionPerSecond = 15f;
    private Vector2 moveDirection = Vector2.up;
    private bool isConfigured;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        wallMask = LayerMask.GetMask("Wall");
    }

    public void Configure(
        float baseSpeedValue,
        float spinSpeedDegreesPerSecond,
        float overWallSpeedMultiplierValue,
        float overWallProbeRadiusValue,
        float overWallSpeedTransitionPerSecondValue)
    {
        baseSpeed = Mathf.Max(0f, baseSpeedValue);
        spinSpeed = spinSpeedDegreesPerSecond;
        overWallSpeedMultiplier = Mathf.Clamp01(overWallSpeedMultiplierValue);
        overWallProbeRadius = Mathf.Max(0f, overWallProbeRadiusValue);
        overWallSpeedTransitionPerSecond = Mathf.Max(0f, overWallSpeedTransitionPerSecondValue);

        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        if (rb != null && rb.linearVelocity.sqrMagnitude > MinDirectionSqr)
        {
            moveDirection = rb.linearVelocity.normalized;
        }

        if (rb != null)
        {
            rb.constraints &= ~RigidbodyConstraints2D.FreezeRotation;
        }

        isConfigured = true;
    }

    private void FixedUpdate()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && !manager.IsServer)
        {
            return;
        }

        if (!isConfigured || rb == null)
        {
            return;
        }

        if (rb.linearVelocity.sqrMagnitude > MinDirectionSqr)
        {
            moveDirection = rb.linearVelocity.normalized;
        }

        bool isOverWall = IsOverWall();
        float targetSpeed = baseSpeed * (isOverWall ? overWallSpeedMultiplier : 1f);
        float currentSpeed = rb.linearVelocity.magnitude;
        float maxDelta = overWallSpeedTransitionPerSecond * Time.fixedDeltaTime;
        float nextSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, maxDelta);

        rb.linearVelocity = moveDirection * nextSpeed;
        rb.angularVelocity = spinSpeed;
    }

    private bool IsOverWall()
    {
        if (wallMask == 0)
        {
            return false;
        }

        Vector2 position = rb.position;
        if (overWallProbeRadius <= 0f)
        {
            return Physics2D.OverlapPoint(position, wallMask) != null;
        }

        return Physics2D.OverlapCircle(position, overWallProbeRadius, wallMask) != null;
    }
}
