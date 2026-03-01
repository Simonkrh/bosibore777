using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class HomingMissileGuidance : MonoBehaviour
{
    private const float MinDirectionSqrMagnitude = 0.0001f;
    private const float TwoPi = Mathf.PI * 2f;

    private Rigidbody2D rb;
    private GameManager gameManager;
    private MazeGenerator mazeGenerator;
    private Transform currentTarget;
    private readonly List<Vector2> pathBuffer = new List<Vector2>(32);

    private float homingDelaySeconds = 3f;
    private float targetRefreshIntervalSeconds = 0.2f;
    private float turnRateDegreesPerSecond = 90f;
    private float cornerTurnRateMultiplier = 0.65f;
    private float fallbackSpeed = 1f;
    private float wobbleAmplitudeDegrees = 12f;
    private float wobbleFrequencyHz = 5f;
    private float wobbleBaselineStrength = 0.15f;
    private float wobbleBuildUpPerSecond = 2.5f;
    private float wobbleDecayPerSecond = 1f;
    private int pathLookaheadNodes = 4;
    private float wallHugOffset = 0.22f;
    private float wallHugProbeRadius = 0.05f;
    private float lineOfSightProbeRadius = 0.06f;
    private int wallMask;

    private float spawnTime;
    private float nextTargetRefreshTime;
    private float wobbleStrength;
    private float wobblePhaseRadians;
    private bool isConfigured;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Configure(
        float homingDelay,
        float targetRefreshInterval,
        float turnRateDegreesPerSecondValue,
        float cornerTurnRateMultiplierValue,
        float launchSpeed,
        float wobbleAmplitudeDegreesValue,
        float wobbleFrequencyHzValue,
        float wobbleBaselineStrengthValue,
        float wobbleBuildUpPerSecondValue,
        float wobbleDecayPerSecondValue,
        int pathLookaheadNodesValue,
        float wallHugOffsetValue,
        float wallHugProbeRadiusValue,
        float lineOfSightProbeRadiusValue)
    {
        homingDelaySeconds = Mathf.Max(0f, homingDelay);
        targetRefreshIntervalSeconds = Mathf.Max(0.02f, targetRefreshInterval);
        turnRateDegreesPerSecond = Mathf.Max(0f, turnRateDegreesPerSecondValue);
        cornerTurnRateMultiplier = Mathf.Clamp(cornerTurnRateMultiplierValue, 0.05f, 1f);
        fallbackSpeed = Mathf.Max(0.01f, launchSpeed);
        wobbleAmplitudeDegrees = Mathf.Max(0f, wobbleAmplitudeDegreesValue);
        wobbleFrequencyHz = Mathf.Max(0f, wobbleFrequencyHzValue);
        wobbleBaselineStrength = Mathf.Clamp01(wobbleBaselineStrengthValue);
        wobbleBuildUpPerSecond = Mathf.Max(0f, wobbleBuildUpPerSecondValue);
        wobbleDecayPerSecond = Mathf.Max(0f, wobbleDecayPerSecondValue);
        pathLookaheadNodes = Mathf.Max(1, pathLookaheadNodesValue);
        wallHugOffset = Mathf.Max(0f, wallHugOffsetValue);
        wallHugProbeRadius = Mathf.Max(0f, wallHugProbeRadiusValue);
        lineOfSightProbeRadius = Mathf.Max(0f, lineOfSightProbeRadiusValue);
        wallMask = LayerMask.GetMask("Wall");

        spawnTime = Time.time;
        nextTargetRefreshTime = spawnTime;
        wobbleStrength = wobbleBaselineStrength;
        wobblePhaseRadians = Random.Range(0f, TwoPi);
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

        Vector2 currentVelocity = rb.linearVelocity;
        if (currentVelocity.sqrMagnitude < MinDirectionSqrMagnitude)
        {
            currentVelocity = (Vector2)transform.up * fallbackSpeed;
            rb.linearVelocity = currentVelocity;
        }

        if (Time.time >= spawnTime + homingDelaySeconds)
        {
            RefreshTargetIfNeeded();
            if (currentTarget != null)
            {
                Vector2 steeringTarget = ResolveSteeringTarget((Vector2)currentTarget.position, out float turnRateScale);
                currentVelocity = SteerTowardsTarget(currentVelocity, steeringTarget, turnRateScale);
            }
        }

        if (currentVelocity.sqrMagnitude >= MinDirectionSqrMagnitude)
        {
            transform.up = currentVelocity.normalized;
        }
    }

    private Vector2 SteerTowardsTarget(Vector2 currentVelocity, Vector3 targetPosition, float turnRateScale)
    {
        Vector2 toTarget = (Vector2)targetPosition - rb.position;
        if (toTarget.sqrMagnitude < MinDirectionSqrMagnitude)
        {
            return currentVelocity;
        }

        Vector2 currentDirection = currentVelocity.normalized;
        Vector2 desiredDirection = toTarget.normalized;

        float effectiveTurnRate = turnRateDegreesPerSecond * Mathf.Clamp(turnRateScale, 0.05f, 1f);
        float maxTurnStep = effectiveTurnRate * Time.fixedDeltaTime;
        float rawAngleToDesired = Vector2.SignedAngle(currentDirection, desiredDirection);
        float normalizedTurnDemand = maxTurnStep > Mathf.Epsilon
            ? Mathf.Clamp01(Mathf.Abs(rawAngleToDesired) / maxTurnStep)
            : 0f;

        wobbleStrength = Mathf.Max(
            wobbleBaselineStrength,
            wobbleStrength + (normalizedTurnDemand * wobbleBuildUpPerSecond - wobbleDecayPerSecond) * Time.fixedDeltaTime);
        wobbleStrength = Mathf.Clamp01(wobbleStrength);

        wobblePhaseRadians += wobbleFrequencyHz * TwoPi * Time.fixedDeltaTime;
        if (wobblePhaseRadians > TwoPi)
        {
            wobblePhaseRadians -= TwoPi;
        }

        float wobbleOffsetDegrees = Mathf.Sin(wobblePhaseRadians) * wobbleAmplitudeDegrees * wobbleStrength;
        Vector2 wobbledDesiredDirection =
            (Quaternion.Euler(0f, 0f, wobbleOffsetDegrees) * desiredDirection).normalized;

        float angleToDesired = Vector2.SignedAngle(currentDirection, wobbledDesiredDirection);
        float clampedTurn = Mathf.Clamp(angleToDesired, -maxTurnStep, maxTurnStep);
        Vector2 steeredDirection = (Quaternion.Euler(0f, 0f, clampedTurn) * currentDirection).normalized;

        float speed = currentVelocity.magnitude;
        if (speed < 0.01f)
        {
            speed = fallbackSpeed;
        }
        Vector2 steeredVelocity = steeredDirection * speed;
        rb.linearVelocity = steeredVelocity;
        return steeredVelocity;
    }

    private Vector2 ResolveSteeringTarget(Vector2 targetPosition, out float turnRateScale)
    {
        turnRateScale = 1f;
        if (!TryResolveMazeGenerator(out MazeGenerator resolvedMaze))
        {
            return targetPosition;
        }

        pathBuffer.Clear();
        if (!resolvedMaze.TryFindPath(rb.position, targetPosition, pathBuffer) || pathBuffer.Count == 0)
        {
            return targetPosition;
        }

        if (pathBuffer.Count == 1)
        {
            return pathBuffer[0];
        }

        turnRateScale = GetCornerTurnRateScale();

        int maxIndex = GetStraightCorridorLookaheadLimit();
        int chosenIndex = 1;

        for (int i = maxIndex; i >= 1; i--)
        {
            if (HasLineOfSight(rb.position, pathBuffer[i]))
            {
                chosenIndex = i;
                break;
            }
        }

        return ApplyWallHugBias(pathBuffer[chosenIndex], chosenIndex, targetPosition);
    }

    private float GetCornerTurnRateScale()
    {
        if (pathBuffer.Count < 3)
        {
            return 1f;
        }

        Vector2 firstStep = pathBuffer[1] - pathBuffer[0];
        Vector2 secondStep = pathBuffer[2] - pathBuffer[1];
        if (firstStep.sqrMagnitude <= MinDirectionSqrMagnitude || secondStep.sqrMagnitude <= MinDirectionSqrMagnitude)
        {
            return 1f;
        }

        float alignment = Vector2.Dot(firstStep.normalized, secondStep.normalized);
        float cornerSharpness = Mathf.Clamp01(1f - Mathf.Clamp(alignment, -1f, 1f));
        return Mathf.Lerp(1f, cornerTurnRateMultiplier, cornerSharpness);
    }

    private bool HasLineOfSight(Vector2 from, Vector2 to)
    {
        if (wallMask == 0)
        {
            return true;
        }

        Vector2 delta = to - from;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return true;
        }

        Vector2 direction = delta / distance;
        if (lineOfSightProbeRadius <= 0f)
        {
            RaycastHit2D hit = Physics2D.Raycast(from, direction, distance, wallMask);
            return hit.collider == null;
        }

        RaycastHit2D circleHit = Physics2D.CircleCast(from, lineOfSightProbeRadius, direction, distance, wallMask);
        return circleHit.collider == null;
    }

    private int GetStraightCorridorLookaheadLimit()
    {
        int maxRequested = Mathf.Min(pathBuffer.Count - 1, pathLookaheadNodes);
        if (maxRequested <= 1)
        {
            return 1;
        }

        Vector2 firstStep = pathBuffer[1] - pathBuffer[0];
        if (firstStep.sqrMagnitude <= MinDirectionSqrMagnitude)
        {
            return 1;
        }

        Vector2 corridorDirection = firstStep.normalized;
        int straightLimit = 1;

        for (int i = 2; i <= maxRequested; i++)
        {
            Vector2 step = pathBuffer[i] - pathBuffer[i - 1];
            if (step.sqrMagnitude <= MinDirectionSqrMagnitude)
            {
                break;
            }

            if (Vector2.Dot(step.normalized, corridorDirection) < 0.99f)
            {
                break;
            }

            straightLimit = i;
        }

        return straightLimit;
    }

    private Vector2 ApplyWallHugBias(Vector2 baseTarget, int pathIndex, Vector2 targetPosition)
    {
        if (wallHugOffset <= 0f)
        {
            return baseTarget;
        }

        Vector2 segmentDirection = Vector2.zero;
        if (pathIndex > 0)
        {
            segmentDirection = pathBuffer[pathIndex] - pathBuffer[pathIndex - 1];
        }
        else if (pathBuffer.Count > 1)
        {
            segmentDirection = pathBuffer[1] - pathBuffer[0];
        }
        else
        {
            segmentDirection = baseTarget - rb.position;
        }

        if (segmentDirection.sqrMagnitude <= MinDirectionSqrMagnitude)
        {
            return baseTarget;
        }

        Vector2 tangent = segmentDirection.normalized;
        Vector2 normal = new Vector2(-tangent.y, tangent.x);
        float sideSign = Mathf.Sign(Vector2.Dot(targetPosition - baseTarget, normal));
        if (Mathf.Abs(sideSign) < 0.001f)
        {
            return baseTarget;
        }

        const int offsetSteps = 4;
        for (int step = offsetSteps; step >= 1; step--)
        {
            float scale = (float)step / offsetSteps;
            Vector2 candidate = baseTarget + normal * (wallHugOffset * sideSign * scale);
            if (IsPositionBlocked(candidate))
            {
                continue;
            }

            if (HasLineOfSight(rb.position, candidate))
            {
                return candidate;
            }
        }

        return baseTarget;
    }

    private bool IsPositionBlocked(Vector2 point)
    {
        if (wallMask == 0)
        {
            return false;
        }

        if (wallHugProbeRadius <= 0f)
        {
            return Physics2D.OverlapPoint(point, wallMask) != null;
        }

        return Physics2D.OverlapCircle(point, wallHugProbeRadius, wallMask) != null;
    }

    private void RefreshTargetIfNeeded()
    {
        if (Time.time < nextTargetRefreshTime && IsTargetValid(currentTarget))
        {
            return;
        }

        currentTarget = FindNearestTarget();
        nextTargetRefreshTime = Time.time + targetRefreshIntervalSeconds;
    }

    private bool IsTargetValid(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        NetworkObject targetNetworkObject = target.GetComponent<NetworkObject>();
        return targetNetworkObject != null && targetNetworkObject.IsSpawned;
    }

    private bool TryResolveMazeGenerator(out MazeGenerator resolvedMaze)
    {
        if (mazeGenerator == null)
        {
            if (gameManager == null)
            {
                gameManager = Object.FindFirstObjectByType<GameManager>();
            }

            if (gameManager != null && gameManager.mazeGenerator != null)
            {
                mazeGenerator = gameManager.mazeGenerator;
            }

            if (mazeGenerator == null)
            {
                mazeGenerator = Object.FindFirstObjectByType<MazeGenerator>();
            }
        }

        resolvedMaze = mazeGenerator;
        return resolvedMaze != null;
    }

    private Transform FindNearestTarget()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening)
        {
            return null;
        }

        if (gameManager == null)
        {
            gameManager = Object.FindFirstObjectByType<GameManager>();
        }

        if (gameManager == null)
        {
            return null;
        }

        Vector2 origin = rb != null ? rb.position : (Vector2)transform.position;
        float bestDistanceSqr = float.MaxValue;
        Transform closestTarget = null;

        for (int i = 0; i < manager.ConnectedClientsList.Count; i++)
        {
            ulong clientId = manager.ConnectedClientsList[i].ClientId;

            if (!gameManager.TryGetPlayerObject(clientId, out GameObject playerObject) || playerObject == null)
            {
                continue;
            }

            NetworkObject targetNetworkObject = playerObject.GetComponent<NetworkObject>();
            if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
            {
                continue;
            }

            float distanceSqr = ((Vector2)playerObject.transform.position - origin).sqrMagnitude;
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                closestTarget = playerObject.transform;
            }
        }

        return closestTarget;
    }
}
