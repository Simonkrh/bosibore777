using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Projectile))]
public class LazerProjectileBounce : NetworkBehaviour
{
    private const float VelocityEpsilon = 0.000001f;
    private const float MinDistanceEpsilon = 0.0001f;
    private const float SurfacePushEpsilon = 0.002f;
    private const int DefaultReflectionSafetyLimit = 24;
    [SerializeField] private float despawnDelayAfterPath = 0.5f;

    private Rigidbody2D rb;
    private Projectile projectile;
    private CircleCollider2D rootCircleCollider;
    private Vector2 currentDirection;
    private float currentSpeed;
    private float castRadius;
    private bool initializedKinematicMotion;
    private bool pathBuilt;
    private int currentPathIndex;
    private float configuredMaxDistance = -1f;
    private readonly List<Vector2> wallPathPoints = new List<Vector2>(32);
    private float traveledDistance;
    private float totalPathLength;
    private bool pathCompleted;
    private float pathCompletedAt = -1f;
    private float syncedCompletionDistance = -1f;

    private readonly NetworkVariable<float> netConfiguredMaxDistance =
        new(-1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netDirectionX =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netDirectionY =
        new(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netSpeed =
        new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> netMotionInitialized =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netDespawnDelayAfterPath =
        new(0.5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> netPathCompleted =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> netCompletionDistance =
        new(-1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool HasPath => pathBuilt && wallPathPoints.Count >= 2;
    public IReadOnlyList<Vector2> PathPoints => wallPathPoints;
    public float TraveledDistance => traveledDistance;
    public float CurrentSpeed => currentSpeed;
    public float TotalPathLength => totalPathLength;
    public bool PathCompleted => pathCompleted;
    public float PathCompletionElapsed => pathCompleted ? Mathf.Max(0f, Time.time - pathCompletedAt) : 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        projectile = GetComponent<Projectile>();
        rootCircleCollider = GetComponent<CircleCollider2D>();
        castRadius = ResolveWorldCastRadius();
    }

    public void Configure(float maxDistance)
    {
        configuredMaxDistance = Mathf.Max(0f, maxDistance);
        pathBuilt = false;
        currentPathIndex = 0;
        traveledDistance = 0f;
        totalPathLength = 0f;
        pathCompleted = false;
        pathCompletedAt = -1f;
        syncedCompletionDistance = -1f;

        if (IsServer)
        {
            netConfiguredMaxDistance.Value = configuredMaxDistance;
            netPathCompleted.Value = false;
            netCompletionDistance.Value = -1f;
        }
    }

    public void SetDespawnDelayAfterPath(float delaySeconds)
    {
        despawnDelayAfterPath = Mathf.Max(0f, delaySeconds);
        if (IsServer)
        {
            netDespawnDelayAfterPath.Value = despawnDelayAfterPath;
        }
    }

    private void FixedUpdate()
    {
        if (projectile == null || rb == null)
        {
            return;
        }

        if (!IsServer)
        {
            SyncStateFromServer();
        }

        if (pathCompleted)
        {
            if (IsServer && PathCompletionElapsed >= despawnDelayAfterPath)
            {
                projectile.ForceDestroy();
            }

            return;
        }

        if (!initializedKinematicMotion)
        {
            Vector2 startingVelocity = rb.linearVelocity;
            if (startingVelocity.sqrMagnitude <= VelocityEpsilon)
            {
                if (!netMotionInitialized.Value || netSpeed.Value <= VelocityEpsilon)
                {
                    return;
                }

                currentDirection = new Vector2(netDirectionX.Value, netDirectionY.Value).normalized;
                currentSpeed = netSpeed.Value;
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.linearVelocity = Vector2.zero;
                initializedKinematicMotion = true;
                return;
            }

            currentDirection = startingVelocity.normalized;
            currentSpeed = startingVelocity.magnitude;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
            initializedKinematicMotion = true;

            if (IsServer)
            {
                netDirectionX.Value = currentDirection.x;
                netDirectionY.Value = currentDirection.y;
                netSpeed.Value = currentSpeed;
                netMotionInitialized.Value = true;
            }
        }

        if (!pathBuilt)
        {
            float networkConfiguredMaxDistance = netConfiguredMaxDistance.Value;
            if (configuredMaxDistance < 0f && networkConfiguredMaxDistance >= 0f)
            {
                configuredMaxDistance = networkConfiguredMaxDistance;
            }

            float maxDistance = configuredMaxDistance >= 0f
                ? configuredMaxDistance
                : Mathf.Max(0f, currentSpeed * Mathf.Max(0f, projectile.lifetime));
            BuildWallPath(rb.position, currentDirection, maxDistance);
            pathBuilt = wallPathPoints.Count >= 2;
            totalPathLength = CalculateTotalPathLength();
            currentPathIndex = 0;
            if (!pathBuilt)
            {
                return;
            }
        }

        float stepDistance = Mathf.Max(0f, currentSpeed) * Time.fixedDeltaTime;
        if (stepDistance <= MinDistanceEpsilon)
        {
            return;
        }

        Vector2 position = rb.position;
        while (stepDistance > MinDistanceEpsilon && currentPathIndex < wallPathPoints.Count - 1)
        {
            Vector2 targetPoint = wallPathPoints[currentPathIndex + 1];
            Vector2 segmentDelta = targetPoint - position;
            float segmentLength = segmentDelta.magnitude;
            if (segmentLength <= MinDistanceEpsilon)
            {
                currentPathIndex++;
                continue;
            }

            Vector2 segmentDirection = segmentDelta / segmentLength;
            float moveDistance = Mathf.Min(stepDistance, segmentLength);

            if (TryFindClosestPlayerHit(position, segmentDirection, moveDistance, out PlayerController hitPlayer, out float playerHitDistance))
            {
                Vector2 impactPosition = position + segmentDirection * Mathf.Max(0f, playerHitDistance);
                rb.MovePosition(impactPosition);
                traveledDistance = Mathf.Min(totalPathLength, traveledDistance + Mathf.Max(0f, playerHitDistance));
                hitPlayer.Die(projectile.ShooterClientId);
                totalPathLength = traveledDistance;
                MarkPathCompleted();
                return;
            }

            position += segmentDirection * moveDistance;
            currentDirection = segmentDirection;
            stepDistance -= moveDistance;
            traveledDistance = Mathf.Min(totalPathLength, traveledDistance + Mathf.Max(0f, moveDistance));

            if (moveDistance >= segmentLength - MinDistanceEpsilon)
            {
                currentPathIndex++;
            }
        }

        rb.MovePosition(position);
        float rotationDegrees = Mathf.Atan2(currentDirection.y, currentDirection.x) * Mathf.Rad2Deg - 90f;
        rb.MoveRotation(rotationDegrees);

        if (currentPathIndex >= wallPathPoints.Count - 1 || traveledDistance >= totalPathLength - MinDistanceEpsilon)
        {
            traveledDistance = totalPathLength;
            MarkPathCompleted();
        }
    }

    private void BuildWallPath(Vector2 origin, Vector2 direction, float maxDistance)
    {
        wallPathPoints.Clear();
        wallPathPoints.Add(origin);

        Vector2 currentOrigin = origin;
        Vector2 reflectedDirection = direction.normalized;
        float remainingDistance = Mathf.Max(0f, maxDistance);
        int remainingReflections = ResolveRemainingReflections();
        int safetyIterations = Mathf.Max(4, DefaultReflectionSafetyLimit * 4);
        Collider2D previousReflectedWall = null;

        while (remainingDistance > MinDistanceEpsilon && safetyIterations-- > 0)
        {
            if (!TryFindClosestWallHit(
                    currentOrigin,
                    reflectedDirection,
                    remainingDistance,
                    previousReflectedWall,
                    out RaycastHit2D wallHit))
            {
                wallPathPoints.Add(currentOrigin + reflectedDirection * remainingDistance);
                return;
            }

            float traveled = Mathf.Max(0f, wallHit.distance);
            Vector2 hitCenterPoint = currentOrigin + reflectedDirection * traveled;
            wallPathPoints.Add(hitCenterPoint);

            if (remainingReflections <= 0)
            {
                return;
            }

            previousReflectedWall = wallHit.collider;
            remainingReflections--;
            remainingDistance -= traveled;
            if (remainingDistance <= MinDistanceEpsilon)
            {
                return;
            }

            Vector2 normal = wallHit.normal.sqrMagnitude > MinDistanceEpsilon
                ? wallHit.normal.normalized
                : -reflectedDirection;
            reflectedDirection = Vector2.Reflect(reflectedDirection, normal).normalized;
            currentOrigin = hitCenterPoint + normal * SurfacePushEpsilon + reflectedDirection * SurfacePushEpsilon;
            remainingDistance = Mathf.Max(0f, remainingDistance - (SurfacePushEpsilon * 2f));
        }

        if (remainingDistance > MinDistanceEpsilon)
        {
            wallPathPoints.Add(currentOrigin + reflectedDirection * remainingDistance);
        }
    }

    private int ResolveRemainingReflections()
    {
        int configuredBounces = projectile != null ? projectile.maxWallBounces : -1;
        if (configuredBounces < 0)
        {
            return Mathf.Max(0, DefaultReflectionSafetyLimit);
        }

        return Mathf.Clamp(configuredBounces, 0, Mathf.Max(0, DefaultReflectionSafetyLimit));
    }

    private bool TryFindClosestWallHit(
        Vector2 origin,
        Vector2 direction,
        float maxDistance,
        Collider2D previousReflectedWall,
        out RaycastHit2D closestHit)
    {
        closestHit = default;
        bool found = false;
        float closestDistance = float.PositiveInfinity;

        RaycastHit2D[] hits = Physics2D.CircleCastAll(
            origin,
            Mathf.Max(0.001f, castRadius),
            direction,
            maxDistance);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hitCollider = hits[i].collider;
            if (hitCollider == null || !hitCollider.CompareTag("Wall"))
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

    private bool TryFindClosestPlayerHit(
        Vector2 origin,
        Vector2 direction,
        float maxDistance,
        out PlayerController closestPlayer,
        out float closestHitDistance)
    {
        closestPlayer = null;
        closestHitDistance = float.PositiveInfinity;

        RaycastHit2D[] hits = Physics2D.CircleCastAll(
            origin,
            Mathf.Max(0.001f, castRadius),
            direction,
            maxDistance);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hitCollider = hits[i].collider;
            if (hitCollider == null || hitCollider.GetComponentInParent<Projectile>() != null)
            {
                continue;
            }

            PlayerController player = hitCollider.GetComponentInParent<PlayerController>();
            if (player == null)
            {
                continue;
            }

            float hitDistance = Mathf.Max(0f, hits[i].distance);
            if (found && hitDistance >= closestHitDistance)
            {
                continue;
            }

            found = true;
            closestPlayer = player;
            closestHitDistance = hitDistance;
        }

        return found;
    }

    private float ResolveWorldCastRadius()
    {
        if (rootCircleCollider == null)
        {
            return 0.03f;
        }

        Vector3 scale = transform.lossyScale;
        float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        return Mathf.Max(0.001f, rootCircleCollider.radius * maxScale);
    }

    private float CalculateTotalPathLength()
    {
        float total = 0f;
        if (wallPathPoints.Count < 2)
        {
            return total;
        }

        for (int i = 0; i < wallPathPoints.Count - 1; i++)
        {
            total += Vector2.Distance(wallPathPoints[i], wallPathPoints[i + 1]);
        }

        return total;
    }

    private void MarkPathCompleted()
    {
        if (pathCompleted)
        {
            return;
        }

        pathCompleted = true;
        pathCompletedAt = Time.time;
        syncedCompletionDistance = Mathf.Max(0f, totalPathLength);
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }

        if (IsServer)
        {
            netPathCompleted.Value = true;
            netCompletionDistance.Value = syncedCompletionDistance;
        }
    }

    private void SyncStateFromServer()
    {
        despawnDelayAfterPath = Mathf.Max(0f, netDespawnDelayAfterPath.Value);

        if (configuredMaxDistance < 0f && netConfiguredMaxDistance.Value >= 0f)
        {
            configuredMaxDistance = netConfiguredMaxDistance.Value;
        }

        if (!pathCompleted && netPathCompleted.Value)
        {
            pathCompleted = true;
            pathCompletedAt = Time.time;
            syncedCompletionDistance = Mathf.Max(0f, netCompletionDistance.Value);
            if (syncedCompletionDistance > MinDistanceEpsilon)
            {
                totalPathLength = syncedCompletionDistance;
                traveledDistance = Mathf.Min(traveledDistance, totalPathLength);
            }
        }
    }

    private void OnValidate()
    {
        despawnDelayAfterPath = Mathf.Max(0f, despawnDelayAfterPath);
    }
}
