using Unity.Netcode;
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
    private float rotationTimer = 0f; 

    // --- Client-Side Prediction ---
    private int nextInputSequence = 0;            // ID for the next input
    private List<MovementInput> pendingInputs = new List<MovementInput>();
    private int lastProcessedInput = 0;           // last input ID processed by server

    // --- Network sync for non-owner interpolation ---
    private NetworkVariable<Vector2> networkPosition = new NetworkVariable<Vector2>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<float> networkRotation = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<float> networkChildRotation = new NetworkVariable<float>(
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
        rb.gravityScale = 0; // 2D top-down, no gravity
    }

    public override void OnNetworkSpawn()
    {
        tankColor.OnValueChanged += OnTankColorChanged;

        // Only the server does physics simulation on the rigidbody. Clients = kinematic
        if (!IsServer)
        {
            rb.isKinematic = true;
        }

        // Cache GameManager reference
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

    // Callback for when the tank color changes
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
        // Handle shooting for the owner (both host or remote client)
        if (IsOwner)
        {
            HandleShooting();
        }

        // Non-owner doesn't process input or do movement logic
        if (!IsOwner)
            return;

        // HOST or DEDICATED SERVER + OWNER PATH
        if (IsServer && IsOwner)
        {
            HandleInput();
            return;
        }

        // REMOTE CLIENT PATH: (IsOwner && !IsServer)
        HandleInput();
    }

    /// <summary>
    /// Collect player inputs, immediately apply them (client-side prediction),
    /// then send them to the server for authority.
    /// </summary>
    private void HandleInput()
    {
        float moveInput = Input.GetAxisRaw("Vertical");
        float turnInput = 0f;

        // Only rotate at discrete intervals
        if (Input.GetKey(KeyCode.A))
        {
            rotationTimer += Time.deltaTime;
            if (rotationTimer >= rotationInterval)
            {
                turnInput = -1f;
                rotationTimer = 0f;
            }
        }
        else if (Input.GetKey(KeyCode.D))
        {
            rotationTimer += Time.deltaTime;
            if (rotationTimer >= rotationInterval)
            {
                turnInput = 1f;
                rotationTimer = 0f;
            }
        }
        else
        {
            rotationTimer = rotationInterval; 
        }

        // If we have any movement (forward/back or rotation)
        if (Mathf.Abs(turnInput) > 0.0f || Mathf.Abs(moveInput) > 0.0f)
        {
            MovementInput inputData = new MovementInput
            {
                moveInput = moveInput,
                rotationInput = turnInput,
                inputSequence = nextInputSequence++
            };

            // 1) Immediately apply for client-side prediction
            ApplyMovementInput(inputData);

            // 2) Store this input so we can re-apply if the server corrects us
            pendingInputs.Add(inputData);

            // 3) Send this input to the server
            SendInputToServerRpc(inputData);
        }
    }

    private void FixedUpdate()
    {
        if (IsServer)
        {
            // Update authoritative position and rotation
            networkPosition.Value = rb.position;
            if (rotationChild != null)
            {
                networkChildRotation.Value = rotationChild.eulerAngles.z;
            }
        }
        else
        {
            // Non-owner clients smoothly interpolate
            if (!IsOwner)
            {
                SmoothlyInterpolatePositionAndRotation();
            }
        }
    }

    #region Movement

    /// <summary>
    /// Apply movement input on the client side for prediction.
    /// This modifies our local, temporary position/rotation.
    /// </summary>
    private void ApplyMovementInput(MovementInput input)
    {
        float move = input.moveInput;
        float turn = input.rotationInput;

        Vector2 moveVector = rotationChild.up * move * moveSpeed * Time.fixedDeltaTime;
        rb.position = rb.position + moveVector;

        // Handle rotation with fixed step
        if (turn != 0f && rotationChild != null)
        {
            float rotationAmount = rotationStep * turn;
            rotationChild.Rotate(0f, 0f, -rotationAmount);
        }
    }

    /// <summary>
    /// Only the server should modify its own authoritative Rigidbody2D
    /// and then replicate state back out to clients.
    /// </summary>
    private void ApplyMovementOnServer(MovementInput input)
    {
        float move = input.moveInput;
        float turn = input.rotationInput;

        // Handle movement
        Vector2 moveVector = rotationChild.up * move * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + moveVector);

        // Handle rotation with fixed step
        if (turn != 0f && rotationChild != null)
        {
            float rotationAmount = rotationStep * turn;
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
            // 1) Grab local/predicted position & rotation on the client
            Vector3 localSpawnPos = (rotationChild != null) 
                ? rotationChild.position + rotationChild.up * shootingOffsetDistance 
                : transform.position + transform.up * shootingOffsetDistance;

            Quaternion localSpawnRot = (rotationChild != null)
                ? rotationChild.rotation
                : transform.rotation;

            // 2) Send to server to spawn the real projectile from this transform
            ShootServerRpc(localSpawnPos, localSpawnRot);

            lastShotTime = Time.time;
        }
    }

    // 3) Modified ServerRpc that accepts client-provided position & rotation
    [ServerRpc]
    private void ShootServerRpc(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        if (projectilePrefab == null)
        {
            Debug.LogError("Projectile prefab is not assigned!");
            return;
        }


        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, spawnRotation);
        projectile.layer = LayerMask.NameToLayer("Bullet");

        var projectileRb = projectile.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            Vector2 shootDirection = spawnRotation * Vector2.up; 
            projectileRb.linearVelocity = shootDirection * projectileSpeed;
        }

        NetworkObject projectileNetObj = projectile.GetComponent<NetworkObject>();
        if (projectileNetObj != null)
        {
            // Spawn the NetworkObject
            projectileNetObj.Spawn();

            // Set the parent to ProjectilesContainer for easy management
            if (gameManager != null && gameManager.projectilesContainer != null)
            {
                projectile.transform.SetParent(gameManager.projectilesContainer.transform);
            }
            else
            {
                Debug.LogWarning("[TankController] ProjectilesContainer reference is missing in GameManager.");
            }

            var projectileComponent = projectile.GetComponent<Projectile>();
            if (projectileComponent != null)
            {
                projectileComponent.SetShooterId(OwnerClientId);
            }
        }
    }

    #endregion

    #region Server RPCs & Reconciliation

    [ServerRpc]
    private void SendInputToServerRpc(MovementInput input, ServerRpcParams serverRpcParams = default)
    {
        // 1) Apply on server
        ApplyMovementOnServer(input);
        lastProcessedInput = input.inputSequence;

        // 2) Build new authoritative ServerState
        ServerState newState = new ServerState
        {
            position = rb.position,
            rotation = rotationChild != null ? rotationChild.eulerAngles.z : 0f,
            lastProcessedInput = lastProcessedInput
        };

        // 3) Send back to *all* clients (but only the owner will use it)
        ReceiveServerStateClientRpc(newState);
    }

    [ClientRpc]
    private void ReceiveServerStateClientRpc(ServerState state)
    {
        // Only the owning client needs reconciliation
        if (!IsOwner || IsServer)
            return;

        // Correct our position/rotation to the authoritative state
        rb.position = state.position;
        if (rotationChild != null)
        {
            rotationChild.rotation = Quaternion.Euler(0, 0, state.rotation);
        }

        // Remove all inputs up to the last processed by the server
        int i = 0;
        while (i < pendingInputs.Count)
        {
            if (pendingInputs[i].inputSequence <= state.lastProcessedInput)
            {
                pendingInputs.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }

        // Re-apply all unacknowledged inputs so that we "catch up"
        for (int j = 0; j < pendingInputs.Count; j++)
        {
            ApplyMovementInput(pendingInputs[j]);
        }
    }

    #endregion
}
