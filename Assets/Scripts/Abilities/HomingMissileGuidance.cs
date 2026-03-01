using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class HomingMissileGuidance : MonoBehaviour
{
    private const float MinDirectionSqrMagnitude = 0.0001f;

    private Rigidbody2D rb;
    private GameManager gameManager;
    private MazeGenerator mazeGenerator;
    private Transform currentTarget;
    private readonly List<Vector2> pathBuffer = new List<Vector2>(32);

    private float homingDelaySeconds = 3f;
    private float targetRefreshIntervalSeconds = 0.2f;
    private float turnRateDegreesPerSecond = 90f;
    private float fallbackSpeed = 1f;

    private float spawnTime;
    private float nextTargetRefreshTime;
    private bool isConfigured;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Configure(
        float homingDelay,
        float targetRefreshInterval,
        float turnRateDegreesPerSecondValue,
        float launchSpeed)
    {
        homingDelaySeconds = Mathf.Max(0f, homingDelay);
        targetRefreshIntervalSeconds = Mathf.Max(0.02f, targetRefreshInterval);
        turnRateDegreesPerSecond = Mathf.Max(0f, turnRateDegreesPerSecondValue);
        fallbackSpeed = Mathf.Max(0.01f, launchSpeed);

        spawnTime = Time.time;
        nextTargetRefreshTime = spawnTime;
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
                Vector2 steeringTarget = ResolveSteeringTarget((Vector2)currentTarget.position);
                currentVelocity = SteerTowardsTarget(currentVelocity, steeringTarget);
            }
        }

        if (currentVelocity.sqrMagnitude >= MinDirectionSqrMagnitude)
        {
            transform.up = currentVelocity.normalized;
        }
    }

    private Vector2 SteerTowardsTarget(Vector2 currentVelocity, Vector3 targetPosition)
    {
        Vector2 toTarget = (Vector2)targetPosition - rb.position;
        if (toTarget.sqrMagnitude < MinDirectionSqrMagnitude)
        {
            return currentVelocity;
        }

        Vector2 currentDirection = currentVelocity.normalized;
        Vector2 desiredDirection = toTarget.normalized;

        float maxTurnStep = turnRateDegreesPerSecond * Time.fixedDeltaTime;
        float angleToDesired = Vector2.SignedAngle(currentDirection, desiredDirection);
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

    private Vector2 ResolveSteeringTarget(Vector2 targetPosition)
    {
        if (!TryResolveMazeGenerator(out MazeGenerator resolvedMaze))
        {
            return targetPosition;
        }

        pathBuffer.Clear();
        if (!resolvedMaze.TryFindPath(rb.position, targetPosition, pathBuffer) || pathBuffer.Count == 0)
        {
            return targetPosition;
        }

        // Follow the next cell center in the path so the missile routes around walls.
        if (pathBuffer.Count > 1)
        {
            return pathBuffer[1];
        }

        return targetPosition;
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
