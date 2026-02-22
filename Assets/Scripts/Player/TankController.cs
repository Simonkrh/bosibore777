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

    private Rigidbody2D rb;
    private float lastShotTime;
    private GameManager gameManager;

    private float cachedMoveInput = 0f;
    private float cachedTurnInput = 0f;

    private NetworkTransform[] networkTransforms;
    private NetworkRigidbody2D networkRigidbody2D;

    // Client prediction state
    private int nextInputSequence = 0;
    private readonly List<MovementInput> pendingInputs = new List<MovementInput>();

    private const int MaxPendingInputs = 128;
    private const float ReconciliationPositionThreshold = 0.06f;
    private const float ReconciliationRotationThreshold = 3.0f;

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
    }

    public override void OnNetworkSpawn()
    {
        tankColor.OnValueChanged += OnTankColorChanged;

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

        rb.isKinematic = !IsServer && !IsOwner;

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
        if (!IsOwner)
        {
            return;
        }

        HandleShooting();
        CacheCurrentInput();
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
        else if (!IsOwner)
        {
            SmoothlyInterpolatePositionAndRotation();
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
        if (!HasMovementInput())
        {
            return;
        }

        MovementInput inputData = new MovementInput
        {
            moveInput = cachedMoveInput,
            rotationInput = cachedTurnInput,
            deltaTime = Time.fixedDeltaTime,
            inputSequence = nextInputSequence++
        };

        // Predict immediately on local client.
        ApplyMovement(inputData);

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
        float deltaTime = input.deltaTime > 0f ? input.deltaTime : Time.fixedDeltaTime;

        Transform movementTransform = rotationChild != null ? rotationChild : transform;
        Vector2 moveVector = movementTransform.up * move * moveSpeed * deltaTime;
        rb.MovePosition(rb.position + moveVector);

        if (turn != 0f && rotationChild != null)
        {
            float rotationSpeed = rotationStep / Mathf.Max(0.001f, rotationInterval);
            float rotationAmount = rotationSpeed * turn * deltaTime;
            rotationChild.Rotate(0f, 0f, -rotationAmount);
        }
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
            ShootServerRpc();
            lastShotTime = Time.time;
        }
    }

    [ServerRpc]
    private void ShootServerRpc()
    {
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

        Transform firingTransform = rotationChild != null ? rotationChild : transform;
        Vector2 fireDirection = firingTransform.up.normalized;
        float projectileRadius = GetProjectileRadius();

        float shooterForwardExtent = GetShooterForwardExtent(fireDirection);
        float minimumDistanceFromShooter = shooterForwardExtent + projectileRadius + 0.01f;
        float requestedDistance = Mathf.Max(shootingOffsetDistance, minimumDistanceFromShooter);

        Vector2 firingOrigin = firingTransform.position;
        Vector2 spawnPosition2D = firingOrigin + fireDirection * requestedDistance;
        int wallMask = LayerMask.GetMask("Wall");

        if (wallMask != 0)
        {
            float castDistance = requestedDistance + projectileRadius + 0.01f;
            RaycastHit2D wallHit = Physics2D.Raycast(firingOrigin, fireDirection, castDistance, wallMask);
            if (wallHit.collider != null)
            {
                // Clamp spawn on the shooter's side of the wall so bullets never tunnel through it.
                float clampedDistance = Mathf.Max(0f, wallHit.distance - projectileRadius - 0.01f);
                spawnPosition2D = firingOrigin + fireDirection * clampedDistance;
            }
        }

        Vector3 spawnPosition = new Vector3(spawnPosition2D.x, spawnPosition2D.y, 0f);
        Quaternion spawnRotation = firingTransform.rotation;

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
        projectile.layer = LayerMask.NameToLayer("Bullet");

        var projectileRb = projectile.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            Vector2 shootDirection = spawnRotation * Vector2.up;
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
        if (projectileComponent != null)
        {
            float shooterUnlockRadius = GetShooterSelfHitUnlockRadius(projectileRadius);
            Vector2 shooterPosition = rb != null ? rb.position : (Vector2)transform.position;
            projectileComponent.ConfigureShooter(OwnerClientId, shooterPosition, shooterUnlockRadius);
        }
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

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
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

        rb.position = state.position;
        if (rotationChild != null)
        {
            rotationChild.rotation = Quaternion.Euler(0f, 0f, state.rotation);
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
