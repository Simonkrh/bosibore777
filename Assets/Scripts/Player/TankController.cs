using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using System.Collections.Generic;

public class TankController : NetworkBehaviour
{
    public SpriteRenderer tankRenderer;

    [Tooltip("Assign the child Transform that handles rotation.")]
    public Transform rotationChild;

    [Header("Movement Settings")]
    public float moveSpeed = 1.8f;
    public float rotationStep = 10f;
    public float rotationInterval = 0.05f;

    [Header("Shooting Settings")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;
    public float shootCooldown = 0.5f;
    public float shootingOffsetDistance = 1.0f;
    [Tooltip("Layers treated as blocking walls for movement + shot visual raycasts. Leave empty to use layer named 'Wall'.")]
    [SerializeField] private LayerMask wallCollisionMask;

    [Header("Client Feel")]
    [Tooltip("Display a short-lived local visual instantly when a non-host client fires.")]
    [SerializeField] private bool showPredictedShotVisual = true;
    [SerializeField] private float predictedShotVisualLifetime = 0.12f;
    [Range(0f, 1f)]
    [SerializeField] private float predictedShotVisualAlpha = 1f;

    [Header("Shot Fairness")]
    [Tooltip("Compensate remote shooter latency by advancing projectile spawn using measured RTT.")]
    [SerializeField] private bool enableShotLatencyCompensation = true;
    [Range(0f, 1f)]
    [SerializeField] private float shotLatencyCompensationFactor = 1f;
    [SerializeField] private float maxShotLatencyCompensationSeconds = 0.12f;

    private Rigidbody2D rb;
    private float lastShotTime;
    private GameManager gameManager;
    private TankAbilityController abilityController;

    private float cachedMoveInput = 0f;
    private float cachedTurnInput = 0f;

    private NetworkTransform[] networkTransforms;
    private NetworkRigidbody2D networkRigidbody2D;
    private ContactFilter2D movementWallContactFilter;
    private bool movementWallContactFilterInitialized;
    private readonly RaycastHit2D[] movementWallHits = new RaycastHit2D[8];
    private readonly Dictionary<long, ShotVisualProjectile> activeShotVisuals = new Dictionary<long, ShotVisualProjectile>();
    private int nextLocalShotSequence = 0;
    private float serverLastShotTime = float.NegativeInfinity;
    private int wallMask;
    private float nextShotVisualPruneTime;
    private const float ShotVisualPruneInterval = 2f;

    // Client prediction state
    private int nextInputSequence = 0;
    private int lastReceivedServerInputSequence = -1;
    private readonly List<MovementInput> pendingInputs = new List<MovementInput>();

    private const int MaxPendingInputs = 128;
    private const float ReconciliationPositionThreshold = 0.04f;
    private const float ReconciliationRotationThreshold = 2.5f;
    private const float SoftReconciliationPositionFactor = 0.35f;
    private const float SoftReconciliationRotationFactor = 0.4f;
    private const float HardSnapPositionThreshold = 0.35f;
    private const float HardSnapRotationThreshold = 12f;
    private const float MovementWallCastSkin = 0.01f;
    private const float MovementWallBlockDotThreshold = 0.0001f;
    private const float ActiveControlPositionTolerance = 0.03f;
    private const float ActiveControlRotationTolerance = 1.5f;

    // Server-authoritative state replicated for non-owner interpolation
    private readonly NetworkVariable<Vector2> networkPosition = new NetworkVariable<Vector2>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<float> networkChildRotation = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<Color> tankColor = new NetworkVariable<Color>(
        Color.white,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;

        if (wallCollisionMask.value == 0)
        {
            wallCollisionMask = LayerMask.GetMask("Wall");
        }

        wallMask = wallCollisionMask.value;
        InitializeMovementWallContactFilter();
        abilityController = GetComponent<TankAbilityController>();
    }

    public override void OnNetworkSpawn()
    {
        tankColor.OnValueChanged += OnTankColorChanged;
        nextInputSequence = 0;
        lastReceivedServerInputSequence = -1;
        nextLocalShotSequence = 0;
        serverLastShotTime = float.NegativeInfinity;
        nextShotVisualPruneTime = 0f;
        pendingInputs.Clear();
        ClearAllShotVisuals();

        // This controller already implements prediction + reconciliation + remote interpolation.
        // Disable built-in transform/rigidbody sync to avoid conflicting authority paths.
        networkTransforms = GetComponentsInChildren<NetworkTransform>(true);
        for (int i = 0; i < networkTransforms.Length; i++)
        {
            networkTransforms[i].enabled = false;
        }

        networkRigidbody2D = GetComponent<NetworkRigidbody2D>();
        if (networkRigidbody2D != null)
        {
            networkRigidbody2D.enabled = false;
        }

        rb.isKinematic = true;

        if (IsServer)
        {
            gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("GameManager is not found in the scene!");
            }
        }

        SetColor(tankColor.Value);
    }

    private void OnDestroy()
    {
        tankColor.OnValueChanged -= OnTankColorChanged;
        ClearAllShotVisuals();
    }

    private void OnTankColorChanged(Color oldColor, Color newColor)
    {
        SetColor(newColor);
        if (IsOwner)
        {
            PlayerDisplayManager.Instance?.SetIconColor(OwnerClientId, newColor);
        }
    }

    public void SetColor(Color color)
    {
        if (tankRenderer != null)
        {
            tankRenderer.color = color;
        }
        else
        {
            Debug.LogWarning("TankRenderer is not assigned.");
        }

        if (abilityController == null)
        {
            abilityController = GetComponent<TankAbilityController>();
        }

        if (abilityController != null)
        {
            abilityController.ApplyModelOverrideColor(color);
        }
    }

    public void ServerSetColor(Color color)
    {
        if (IsServer)
        {
            tankColor.Value = color;
        }
        else
        {
            Debug.LogWarning("Only the server can set the tank color.");
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            HandleShooting();
            CacheCurrentInput();
            return;
        }

        // Drive remote interpolation at render cadence on pure clients.
        if (IsClient && !IsServer)
        {
            SmoothlyInterpolatePositionAndRotation();
        }

        if (IsClient && Time.unscaledTime >= nextShotVisualPruneTime)
        {
            nextShotVisualPruneTime = Time.unscaledTime + ShotVisualPruneInterval;
            PruneDestroyedShotVisuals();
        }
    }

    private void FixedUpdate()
    {
        if (IsOwner)
        {
            if (IsServer)
            {
                ProcessOwnerInputOnServer();
            }
            else
            {
                ProcessOwnerInputOnClient();
            }
        }

        if (IsServer)
        {
            networkPosition.Value = rb.position;
            if (rotationChild != null)
            {
                networkChildRotation.Value = rotationChild.eulerAngles.z;
            }
        }
    }

    #region Movement

    private void CacheCurrentInput()
    {
        cachedMoveInput = Input.GetAxisRaw("Vertical");
        cachedTurnInput = Input.GetAxisRaw("Horizontal");
    }

    private bool HasMovementInput()
    {
        return Mathf.Abs(cachedMoveInput) > 0.0f || Mathf.Abs(cachedTurnInput) > 0.0f;
    }

    private void ProcessOwnerInputOnServer()
    {
        if (!HasMovementInput())
        {
            return;
        }

        MovementInput inputData = new MovementInput
        {
            moveInput = cachedMoveInput,
            rotationInput = cachedTurnInput,
            deltaTime = Time.fixedDeltaTime,
            inputSequence = 0
        };

        ApplyMovement(inputData);
    }

    private void ProcessOwnerInputOnClient()
    {
        MovementInput inputData = new MovementInput
        {
            moveInput = cachedMoveInput,
            rotationInput = cachedTurnInput,
            deltaTime = Time.fixedDeltaTime,
            inputSequence = nextInputSequence++
        };

        if (HasMovementInput())
        {
            // Predict immediately on local client.
            ApplyMovement(inputData);
        }

        pendingInputs.Add(inputData);
        if (pendingInputs.Count > MaxPendingInputs)
        {
            pendingInputs.RemoveAt(0);
        }

        SendInputToServerRpc(inputData);
    }

    private void ApplyMovementInput(MovementInput input)
    {
        ApplyMovement(input);
    }

    private void ApplyMovement(MovementInput input)
    {
        float move = input.moveInput;
        float turn = input.rotationInput;
        float deltaTime = Time.fixedDeltaTime;

        Transform movementTransform = rotationChild != null ? rotationChild : transform;
        Vector2 requestedMoveVector = movementTransform.up * move * moveSpeed * deltaTime;
        Vector2 allowedMoveVector = GetWallBlockedMoveVector(requestedMoveVector);
        rb.MovePosition(rb.position + allowedMoveVector);

        if (turn != 0f && rotationChild != null)
        {
            float rotationSpeed = rotationStep / Mathf.Max(0.001f, rotationInterval);
            float rotationAmount = rotationSpeed * turn * deltaTime;
            rotationChild.Rotate(0f, 0f, -rotationAmount);
        }
    }

    private void InitializeMovementWallContactFilter()
    {
        movementWallContactFilter = new ContactFilter2D();
        movementWallContactFilter.NoFilter();
        movementWallContactFilter.useTriggers = false;
        movementWallContactFilter.useLayerMask = wallMask != 0;
        movementWallContactFilter.layerMask = wallMask;
        movementWallContactFilterInitialized = true;
    }

    private Vector2 GetWallBlockedMoveVector(Vector2 requestedMoveVector)
    {
        float moveDistance = requestedMoveVector.magnitude;
        if (moveDistance <= Mathf.Epsilon)
        {
            return Vector2.zero;
        }

        if (!movementWallContactFilterInitialized)
        {
            InitializeMovementWallContactFilter();
        }

        if (wallMask == 0)
        {
            return requestedMoveVector;
        }

        Vector2 moveDirection = requestedMoveVector / moveDistance;
        int hitCount = rb.Cast(
            moveDirection,
            movementWallContactFilter,
            movementWallHits,
            moveDistance + MovementWallCastSkin);

        if (hitCount <= 0)
        {
            return requestedMoveVector;
        }

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = movementWallHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            Vector2 wallNormal = hit.normal;
            if (wallNormal.sqrMagnitude <= Mathf.Epsilon)
            {
                continue;
            }

            if (Vector2.Dot(moveDirection, wallNormal) > MovementWallBlockDotThreshold)
            {
                continue;
            }

            // Gameplay rule: if wall contact does not separate the tank, movement fully stops.
            return Vector2.zero;
        }
        return requestedMoveVector;
    }

    private void SmoothlyInterpolatePositionAndRotation()
    {
        float lerpSpeed = 25f;
        rb.position = Vector2.Lerp(rb.position, networkPosition.Value, Time.deltaTime * lerpSpeed);

        if (rotationChild != null)
        {
            float targetRotation = networkChildRotation.Value;
            float currentRotation = rotationChild.eulerAngles.z;
            float newRotation = Mathf.LerpAngle(currentRotation, targetRotation, Time.deltaTime * lerpSpeed);
            rotationChild.rotation = Quaternion.Euler(0f, 0f, newRotation);
        }
    }

    #endregion

    #region Shooting

    private void HandleShooting()
    {
        if (Input.GetKeyDown(KeyCode.Space) && Time.time >= lastShotTime + shootCooldown)
        {
            int shotSequence = nextLocalShotSequence++;
            bool hasUsableAbility = abilityController != null && abilityController.HasAbility;
            bool abilityUsageInProgress = abilityController != null && abilityController.IsAbilityUsageActive;
            if (hasUsableAbility)
            {
                TryUseEquippedAbilityServerRpc(shotSequence);
            }
            else if (abilityUsageInProgress)
            {
                // Ability is still resolving in-world; block fallback normal shots.
                return;
            }
            else
            {
                if (!IsServer)
                {
                    SpawnPredictedShotVisual(shotSequence);
                }

                ShootServerRpc(shotSequence);
            }

            lastShotTime = Time.time;
        }
    }

    [ServerRpc]
    private void TryUseEquippedAbilityServerRpc(int shotSequence, ServerRpcParams serverRpcParams = default)
    {
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        if (abilityController == null)
        {
            abilityController = GetComponent<TankAbilityController>();
            if (abilityController == null)
            {
                return;
            }
        }

        if (!abilityController.TryUseEquippedAbility(this, shotSequence))
        {
            if (abilityController.IsAbilityUsageActive)
            {
                return;
            }

            ServerFireStandardShot(shotSequence, OwnerClientId);
        }
    }

    [ServerRpc]
    private void ShootServerRpc(int shotSequence, ServerRpcParams serverRpcParams = default)
    {
        ulong shooterClientId = serverRpcParams.Receive.SenderClientId;
        if (shooterClientId != OwnerClientId)
        {
            return;
        }

        ServerFireStandardShot(shotSequence, shooterClientId);
    }

    private void ServerFireStandardShot(int shotSequence, ulong shooterClientId)
    {
        if (!IsServer)
        {
            return;
        }

        if (abilityController == null)
        {
            abilityController = GetComponent<TankAbilityController>();
        }

        if (abilityController != null && abilityController.IsAbilityUsageActive)
        {
            return;
        }

        if (projectilePrefab == null)
        {
            Debug.LogError("Projectile prefab is not assigned!");
            return;
        }

        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer || !manager.IsListening)
        {
            Debug.LogError("[TankController] Cannot spawn projectile: NetworkManager is not ready.");
            return;
        }

        if (manager.NetworkConfig == null || manager.NetworkConfig.NetworkTransport == null)
        {
            Debug.LogError("[TankController] Cannot spawn projectile: NetworkTransport is not configured.");
            return;
        }

        if (Time.time < serverLastShotTime + shootCooldown)
        {
            return;
        }

        serverLastShotTime = Time.time;

        float latencyCompensationDistance = GetShotLatencyCompensationDistanceFromRttMs(
            manager.NetworkConfig.NetworkTransport.GetCurrentRtt(shooterClientId));

        ComputeProjectileSpawn(
            latencyCompensationDistance,
            out Vector2 spawnPosition2D,
            out Quaternion spawnRotation,
            out Vector2 shootDirection,
            out float projectileRadius);

        Vector3 spawnPosition = new Vector3(spawnPosition2D.x, spawnPosition2D.y, 0f);
        NetworkObject projectileNetObj = NetworkObject.InstantiateAndSpawn(
            projectilePrefab,
            manager,
            ownerClientId: Unity.Netcode.NetworkManager.ServerClientId,
            destroyWithScene: false,
            isPlayerObject: false,
            forceOverride: false,
            position: spawnPosition,
            rotation: spawnRotation
        );

        if (projectileNetObj == null)
        {
            Debug.LogError("[TankController] InstantiateAndSpawn returned null for projectile.");
            return;
        }

        GameObject projectile = projectileNetObj.gameObject;
        EnableTransformSyncComponents(projectile);
        projectile.layer = LayerMask.NameToLayer("Bullet");

        var projectileRb = projectile.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            projectileRb.interpolation = RigidbodyInterpolation2D.None;
            projectileRb.linearVelocity = shootDirection * projectileSpeed;
        }

        if (gameManager != null && gameManager.projectilesContainer != null)
        {
            projectile.transform.SetParent(gameManager.projectilesContainer.transform, true);
        }
        else
        {
            Debug.LogWarning("[TankController] ProjectilesContainer reference is missing in GameManager.");
        }

        var projectileComponent = projectile.GetComponent<Projectile>();
        if (projectileComponent == null)
        {
            Debug.LogError("[TankController] Projectile component is missing on projectile prefab.");
            Destroy(projectile);
            return;
        }

        float shooterUnlockRadius = GetShooterSelfHitUnlockRadius(projectileRadius);
        Vector2 shooterPosition = rb != null ? rb.position : (Vector2)transform.position;
        projectileComponent.ConfigureServerProjectile(
            shooterClientId,
            shotSequence,
            shooterPosition,
            shooterUnlockRadius,
            HandleAuthoritativeProjectileDestroyed);

        if (showPredictedShotVisual)
        {
            ClientRpcParams shooterOnlyRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { shooterClientId }
                }
            };

            ConfirmAuthoritativeShotSpawnClientRpc(shooterClientId, shotSequence, shooterOnlyRpcParams);
        }
    }

    private void ComputeProjectileSpawn(
        float additionalSpawnDistance,
        out Vector2 spawnPosition2D,
        out Quaternion spawnRotation,
        out Vector2 fireDirection,
        out float projectileRadius)
    {
        Transform firingTransform = rotationChild != null ? rotationChild : transform;
        fireDirection = firingTransform.up.normalized;
        spawnRotation = firingTransform.rotation;
        projectileRadius = GetProjectileRadius();

        float shooterForwardExtent = GetShooterForwardExtent(fireDirection);
        float minimumDistanceFromShooter = shooterForwardExtent + projectileRadius + 0.01f;
        float requestedDistance = Mathf.Max(shootingOffsetDistance, minimumDistanceFromShooter) + Mathf.Max(0f, additionalSpawnDistance);

        Vector2 firingOrigin = firingTransform.position;
        spawnPosition2D = firingOrigin + fireDirection * requestedDistance;
        if (wallMask == 0)
        {
            return;
        }

        float castDistance = requestedDistance + projectileRadius + 0.01f;
        RaycastHit2D wallHit = Physics2D.Raycast(firingOrigin, fireDirection, castDistance, wallMask);
        if (wallHit.collider == null)
        {
            return;
        }

        // Clamp spawn on the shooter's side of the wall so bullets never tunnel through it.
        float clampedDistance = Mathf.Max(0f, wallHit.distance - projectileRadius - 0.01f);
        spawnPosition2D = firingOrigin + fireDirection * clampedDistance;
    }

    private void EnableTransformSyncComponents(GameObject projectile)
    {
        NetworkRigidbody2D netRigidbody = projectile.GetComponent<NetworkRigidbody2D>();
        if (netRigidbody != null)
        {
            netRigidbody.enabled = false;
        }

        NetworkTransform[] transforms = projectile.GetComponentsInChildren<NetworkTransform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].enabled = true;
            transforms[i].Interpolate = false;
            transforms[i].PositionThreshold = 0.0001f;
        }
    }

    public bool TryComputeAbilityProjectileSpawn(
        float additionalSpawnDistance,
        out Vector2 spawnPosition2D,
        out Quaternion spawnRotation,
        out Vector2 fireDirection,
        out float projectileRadius)
    {
        if (!IsServer)
        {
            spawnPosition2D = default;
            spawnRotation = Quaternion.identity;
            fireDirection = Vector2.zero;
            projectileRadius = 0f;
            return false;
        }

        ComputeProjectileSpawn(
            additionalSpawnDistance,
            out spawnPosition2D,
            out spawnRotation,
            out fireDirection,
            out projectileRadius);

        return true;
    }

    public bool TrySpawnAbilityProjectile(
        GameObject projectileToSpawn,
        int shotSequence,
        Vector2 spawnPosition2D,
        Quaternion spawnRotation,
        Vector2 shootDirection,
        float launchSpeed,
        out NetworkObject spawnedProjectile)
    {
        spawnedProjectile = null;
        if (!IsServer || projectileToSpawn == null)
        {
            return false;
        }

        NetworkManager manager = NetworkManager;
        if (manager == null || !manager.IsServer || !manager.IsListening)
        {
            return false;
        }

        Vector3 spawnPosition = new Vector3(spawnPosition2D.x, spawnPosition2D.y, 0f);
        NetworkObject projectileNetObj = NetworkObject.InstantiateAndSpawn(
            projectileToSpawn,
            manager,
            ownerClientId: Unity.Netcode.NetworkManager.ServerClientId,
            destroyWithScene: false,
            isPlayerObject: false,
            forceOverride: false,
            position: spawnPosition,
            rotation: spawnRotation
        );

        if (projectileNetObj == null)
        {
            return false;
        }

        GameObject projectile = projectileNetObj.gameObject;
        EnableTransformSyncComponents(projectile);
        projectile.layer = LayerMask.NameToLayer("Bullet");

        var projectileRb = projectile.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            projectileRb.interpolation = RigidbodyInterpolation2D.None;
            projectileRb.linearVelocity = shootDirection * launchSpeed;
        }

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        if (gameManager != null && gameManager.projectilesContainer != null)
        {
            projectile.transform.SetParent(gameManager.projectilesContainer.transform, true);
        }

        var projectileComponent = projectile.GetComponent<Projectile>();
        if (projectileComponent != null)
        {
            float shooterUnlockRadius = GetShooterSelfHitUnlockRadius(GetProjectileRadius());
            Vector2 shooterPosition = rb != null ? rb.position : (Vector2)transform.position;
            projectileComponent.ConfigureServerProjectile(
                OwnerClientId,
                shotSequence,
                shooterPosition,
                shooterUnlockRadius,
                HandleAuthoritativeProjectileDestroyed);
        }

        spawnedProjectile = projectileNetObj;
        return true;
    }

    private void HandleAuthoritativeProjectileDestroyed(ulong shooterClientId, int shotSequence)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        DespawnShotVisualClientRpc(shooterClientId, shotSequence);
    }

    [ClientRpc]
    private void ConfirmAuthoritativeShotSpawnClientRpc(
        ulong shooterClientId,
        int shotSequence,
        ClientRpcParams clientRpcParams = default)
    {
        if (!showPredictedShotVisual || !IsClient || IsServer || !IsOwner || OwnerClientId != shooterClientId)
        {
            return;
        }

        // Local prediction is only a short bridge until authoritative projectile replication arrives.
        RemoveShotVisual(shooterClientId, shotSequence);
    }

    [ClientRpc]
    private void SpawnShotVisualClientRpc(
        ulong shooterClientId,
        int shotSequence,
        Vector2 spawnPosition,
        Vector2 shootDirection,
        float speed,
        float visualLifetime,
        int maxWallBounces,
        ClientRpcParams clientRpcParams = default)
    {
        if (!showPredictedShotVisual || !IsClient || IsServer)
        {
            return;
        }

        UpsertShotVisual(
            shooterClientId,
            shotSequence,
            spawnPosition,
            shootDirection,
            speed,
            visualLifetime,
            maxWallBounces,
            1f);
    }

    [ClientRpc]
    private void DespawnShotVisualClientRpc(
        ulong shooterClientId,
        int shotSequence,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient || IsServer)
        {
            return;
        }

        RemoveShotVisual(shooterClientId, shotSequence);
    }

    private void SpawnPredictedShotVisual(int shotSequence)
    {
        if (!showPredictedShotVisual || projectilePrefab == null || !IsOwner || IsServer)
        {
            return;
        }

        NetworkManager manager = NetworkManager;
        if (manager == null || manager.NetworkConfig == null || manager.NetworkConfig.NetworkTransport == null)
        {
            return;
        }

        SpriteRenderer sourceRenderer = projectilePrefab.GetComponentInChildren<SpriteRenderer>();
        if (sourceRenderer == null || sourceRenderer.sprite == null)
        {
            return;
        }

        float localCompensationDistance = GetShotLatencyCompensationDistanceFromRttMs(
            manager.NetworkConfig.NetworkTransport.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId));

        ComputeProjectileSpawn(
            localCompensationDistance,
            out Vector2 spawnPosition2D,
            out _,
            out Vector2 fireDirection,
            out _);

        float oneWaySeconds = GetOneWayLatencySecondsFromRttMs(
            manager.NetworkConfig.NetworkTransport.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId));
        float visualLifetime = Mathf.Max(predictedShotVisualLifetime, oneWaySeconds * 2f);
        visualLifetime = Mathf.Clamp(visualLifetime, 0.04f, 0.35f);

        UpsertShotVisual(
            OwnerClientId,
            shotSequence,
            spawnPosition2D,
            fireDirection,
            projectileSpeed,
            visualLifetime,
            GetProjectileMaxWallBounces(),
            predictedShotVisualAlpha);
    }

    private void UpsertShotVisual(
        ulong shooterClientId,
        int shotSequence,
        Vector2 spawnPosition,
        Vector2 direction,
        float speed,
        float visualLifetime,
        int maxWallBounces,
        float alphaMultiplier)
    {
        long shotKey = ComposeShotVisualKey(shooterClientId, shotSequence);
        ShotVisualProjectile existingVisual = null;
        if (activeShotVisuals.TryGetValue(shotKey, out ShotVisualProjectile activeVisual))
        {
            existingVisual = activeVisual;
        }

        if (existingVisual == null)
        {
            existingVisual = CreateShotVisual();
            if (existingVisual == null)
            {
                return;
            }

            activeShotVisuals[shotKey] = existingVisual;
        }

        existingVisual.Configure(
            spawnPosition,
            direction,
            speed,
            visualLifetime,
            maxWallBounces,
            wallMask,
            alphaMultiplier);
    }

    private ShotVisualProjectile CreateShotVisual()
    {
        SpriteRenderer sourceRenderer = projectilePrefab != null ? projectilePrefab.GetComponentInChildren<SpriteRenderer>() : null;
        if (sourceRenderer == null || sourceRenderer.sprite == null)
        {
            return null;
        }

        GameObject visualObject = new GameObject("ShotVisualProjectile");
        visualObject.transform.localScale = projectilePrefab.transform.localScale;

        SpriteRenderer visualRenderer = visualObject.AddComponent<SpriteRenderer>();
        visualRenderer.sprite = sourceRenderer.sprite;
        visualRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        visualRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        visualRenderer.sortingOrder = sourceRenderer.sortingOrder;
        visualRenderer.color = sourceRenderer.color;

        return visualObject.AddComponent<ShotVisualProjectile>();
    }

    private void RemoveShotVisual(ulong shooterClientId, int shotSequence)
    {
        long shotKey = ComposeShotVisualKey(shooterClientId, shotSequence);
        if (!activeShotVisuals.TryGetValue(shotKey, out ShotVisualProjectile visual))
        {
            return;
        }

        if (visual != null)
        {
            Destroy(visual.gameObject);
        }

        activeShotVisuals.Remove(shotKey);
    }

    private void ClearAllShotVisuals()
    {
        foreach (var kvp in activeShotVisuals)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value.gameObject);
            }
        }

        activeShotVisuals.Clear();
    }

    private void PruneDestroyedShotVisuals()
    {
        if (activeShotVisuals.Count == 0)
        {
            return;
        }

        List<long> deadKeys = null;
        foreach (var kvp in activeShotVisuals)
        {
            if (kvp.Value != null)
            {
                continue;
            }

            if (deadKeys == null)
            {
                deadKeys = new List<long>();
            }

            deadKeys.Add(kvp.Key);
        }

        if (deadKeys == null)
        {
            return;
        }

        for (int i = 0; i < deadKeys.Count; i++)
        {
            activeShotVisuals.Remove(deadKeys[i]);
        }
    }

    private static long ComposeShotVisualKey(ulong shooterClientId, int shotSequence)
    {
        return unchecked(((long)shooterClientId << 32) ^ (uint)shotSequence);
    }

    private float GetOneWayLatencySecondsFromRttMs(ulong rttMs)
    {
        float oneWaySeconds = (float)rttMs * 0.0005f;
        return Mathf.Clamp(oneWaySeconds, 0f, maxShotLatencyCompensationSeconds);
    }

    private float GetShotLatencyCompensationDistanceFromRttMs(ulong rttMs)
    {
        if (!enableShotLatencyCompensation || projectileSpeed <= 0f)
        {
            return 0f;
        }

        float oneWaySeconds = GetOneWayLatencySecondsFromRttMs(rttMs) * Mathf.Clamp01(shotLatencyCompensationFactor);
        return projectileSpeed * oneWaySeconds;
    }

    private float GetProjectileLifetime()
    {
        if (projectilePrefab == null)
        {
            return 10f;
        }

        Projectile projectile = projectilePrefab.GetComponent<Projectile>();
        return projectile != null ? projectile.lifetime : 10f;
    }

    private int GetProjectileMaxWallBounces()
    {
        if (projectilePrefab == null)
        {
            return -1;
        }

        Projectile projectile = projectilePrefab.GetComponent<Projectile>();
        return projectile != null ? projectile.maxWallBounces : -1;
    }

    private float GetProjectileRadius()
    {
        CircleCollider2D circleCollider = projectilePrefab != null ? projectilePrefab.GetComponent<CircleCollider2D>() : null;
        if (circleCollider != null)
        {
            Vector3 scale = projectilePrefab.transform.localScale;
            float scaleFactor = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            return Mathf.Max(0.005f, circleCollider.radius * scaleFactor);
        }

        return 0.05f;
    }

    private float GetShooterForwardExtent(Vector2 direction)
    {
        if (!TryGetCombinedSolidColliderBounds(out Bounds combinedBounds))
        {
            return 0.2f;
        }

        Vector2 extents = combinedBounds.extents;
        return Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y;
    }

    private float GetShooterSelfHitUnlockRadius(float projectileRadius)
    {
        if (!TryGetCombinedSolidColliderBounds(out Bounds combinedBounds))
        {
            return 0.25f + projectileRadius;
        }

        return combinedBounds.extents.magnitude + projectileRadius + 0.02f;
    }

    private bool TryGetCombinedSolidColliderBounds(out Bounds combinedBounds)
    {
        Collider2D[] colliders = GetComponentsInChildren<Collider2D>();
        bool foundBounds = false;
        combinedBounds = new Bounds(transform.position, Vector3.zero);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                continue;
            }

            if (!foundBounds)
            {
                combinedBounds = collider.bounds;
                foundBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(collider.bounds);
            }
        }

        return foundBounds;
    }

    #endregion

    #region Server RPCs & Reconciliation

    [ServerRpc]
    private void SendInputToServerRpc(MovementInput input, ServerRpcParams serverRpcParams = default)
    {
        // Safety: only allow the owner to submit movement for this object.
        if (serverRpcParams.Receive.SenderClientId != OwnerClientId)
        {
            return;
        }

        ApplyMovement(input);

        ServerState newState = new ServerState
        {
            position = rb.position,
            rotation = rotationChild != null ? rotationChild.eulerAngles.z : 0f,
            lastProcessedInput = input.inputSequence
        };

        ClientRpcParams ownerOnlyRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { serverRpcParams.Receive.SenderClientId }
            }
        };

        ReceiveServerStateClientRpc(newState, ownerOnlyRpcParams);
    }

    [ClientRpc(Delivery = RpcDelivery.Unreliable)]
    private void ReceiveServerStateClientRpc(ServerState state, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner || IsServer)
        {
            return;
        }

        if (state.lastProcessedInput <= lastReceivedServerInputSequence)
        {
            return;
        }

        lastReceivedServerInputSequence = state.lastProcessedInput;
        RemoveAcknowledgedInputs(state.lastProcessedInput);

        float positionError = Vector2.Distance(rb.position, state.position);
        float rotationError = rotationChild != null
            ? Mathf.Abs(Mathf.DeltaAngle(rotationChild.eulerAngles.z, state.rotation))
            : 0f;

        bool needsCorrection =
            positionError > ReconciliationPositionThreshold ||
            rotationError > ReconciliationRotationThreshold;

        if (!needsCorrection)
        {
            return;
        }

        bool activelyControlling =
            Mathf.Abs(cachedMoveInput) > 0.01f ||
            Mathf.Abs(cachedTurnInput) > 0.01f;

        if (activelyControlling &&
            positionError < ActiveControlPositionTolerance &&
            rotationError < ActiveControlRotationTolerance)
        {
            return;
        }

        if (positionError > HardSnapPositionThreshold || rotationError > HardSnapRotationThreshold)
        {
            rb.position = state.position;
        }
        else
        {
            rb.position = Vector2.Lerp(rb.position, state.position, SoftReconciliationPositionFactor);
        }

        if (rotationChild != null)
        {
            float correctedRotation = rotationError > HardSnapRotationThreshold
                ? state.rotation
                : Mathf.LerpAngle(rotationChild.eulerAngles.z, state.rotation, SoftReconciliationRotationFactor);
            rotationChild.rotation = Quaternion.Euler(0f, 0f, correctedRotation);
        }

        for (int i = 0; i < pendingInputs.Count; i++)
        {
            ApplyMovementInput(pendingInputs[i]);
        }
    }

    private void RemoveAcknowledgedInputs(int lastProcessedInputSequence)
    {
        int i = 0;
        while (i < pendingInputs.Count)
        {
            if (pendingInputs[i].inputSequence <= lastProcessedInputSequence)
            {
                pendingInputs.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }
    }

    #endregion
}
