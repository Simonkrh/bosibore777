using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controls a tank with client-side prediction for remote clients,
/// and no double movement for the host.
/// </summary>
public class TankController : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 1.8f;
    public float rotationSpeed = 300f;

    [Header("Shooting Settings")]
    public GameObject projectilePrefab;
    public float projectileSpeed = 10f;
    public float shootCooldown = 0.5f;
    public float shootingOffsetDistance = 1.0f;

    private Rigidbody2D rb;
    private float lastShotTime;

    // --- Client-Side Prediction ---
    private int nextInputSequence = 0;  // ID for the next input
    private List<MovementInput> pendingInputs = new List<MovementInput>();
    private int lastProcessedInput = 0; // last input ID processed by server

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

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0; // 2D top-down, no gravity
    }

    public override void OnNetworkSpawn()
    {
        // Only the server does physics simulation on the rigidbody. Clients = kinematic
        // so we don't fight the server's authoritative updates.
        if (!IsServer)
        {
            rb.isKinematic = true;
        }
    }

    private void Update()
    {
        // Handle shooting for the owner (both host and remote client)
        if (IsOwner)
        {
            HandleShooting();
        }

        // If we're not the owner, do nothing else here; just let interpolation handle movement visually.
        if (!IsOwner)
        {
            return;
        }

        // HOST PATH: (IsOwner && IsServer)
        if (IsServer && IsOwner)
        {
            // 1) Read input
            float moveInput = Input.GetAxisRaw("Vertical");
            float turnInput = Input.GetAxisRaw("Horizontal");

            // 2) Send input to the server function (the host is the server, but no local prediction!)
            MovementInput inputData = new MovementInput
            {
                moveInput = moveInput,
                rotationInput = turnInput,
                inputSequence = nextInputSequence++
            };
            SendInputToServerRpc(inputData);
            return;
        }

        // REMOTE CLIENT PATH: (IsOwner && !IsServer)
        {
            // 1) Gather raw input
            float moveInput = Input.GetAxisRaw("Vertical");
            float turnInput = Input.GetAxisRaw("Horizontal");

            // 2) Build a MovementInput struct with a sequence number
            MovementInput newInput = new MovementInput
            {
                moveInput = moveInput,
                rotationInput = turnInput,
                inputSequence = nextInputSequence++
            };

            // 3) Immediately predict local movement (so it feels responsive)
            ApplyMovementInput(newInput);

            // 4) Add to pending
            pendingInputs.Add(newInput);

            // 5) Send input to server for authoritative movement
            SendInputToServerRpc(newInput);
        }
    }

    private void FixedUpdate()
    {
        // The server updates the authoritative position & rotation here.
        if (IsServer)
        {
            networkPosition.Value = rb.position;
            networkRotation.Value = rb.rotation;
        }
        else
        {
            // Non-owner clients smoothly interpolate from their local position to the server's position.
            if (!IsOwner)
            {
                SmoothlyInterpolatePositionAndRotation();
            }
        }
    }

    #region Movement

    /// <summary>
    /// Called on the remote client to move the tank immediately
    /// (local prediction) or during reconciliation.
    /// </summary>
    private void ApplyMovementInput(MovementInput input)
    {
        float move = input.moveInput;
        float turn = input.rotationInput;

        // We do local movement using direct position changes
        // If you have complicated physics, you might do a separate "predicted" body.
        Vector2 moveVector = transform.up * move * moveSpeed * Time.fixedDeltaTime;
        float rotation = turn * rotationSpeed * Time.fixedDeltaTime;

        rb.position += moveVector;
        rb.rotation -= rotation;
    }

    /// <summary>
    /// Called on the server (or host as server) to perform the authoritative movement.
    /// </summary>
    private void ApplyMovementOnServer(MovementInput input)
    {
        float move = input.moveInput;
        float turn = input.rotationInput;

        // We do server-authoritative movement with MovePosition / MoveRotation
        Vector2 moveVector = transform.up * move * moveSpeed * Time.fixedDeltaTime;
        float rotation = turn * rotationSpeed * Time.fixedDeltaTime;

        rb.MovePosition(rb.position + moveVector);
        rb.MoveRotation(rb.rotation - rotation);
    }

    /// <summary>
    /// For non-owner tanks, smoothly interpolate from local position to
    /// the authoritative position stored in network variables.
    /// Increase or decrease the '25f' to find a sweet spot.
    /// </summary>
    private void SmoothlyInterpolatePositionAndRotation()
    {
        // A higher multiplier means it snaps faster to the server's position,
        // resulting in less apparent lag but more risk of jitter if the updates are large.
        float lerpSpeed = 25f;

        // Use Time.deltaTime (not Time.fixedDeltaTime) so it interpolates smoothly each rendered frame.
        rb.position = Vector2.Lerp(rb.position, networkPosition.Value, Time.deltaTime * lerpSpeed);
        rb.rotation = Mathf.LerpAngle(rb.rotation, networkRotation.Value, Time.deltaTime * lerpSpeed);
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

        Vector3 spawnPosition = transform.position + transform.up * shootingOffsetDistance;
        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, transform.rotation);

        var projectileRb = projectile.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            projectileRb.linearVelocity = transform.up * projectileSpeed;
        }

        NetworkObject projectileNetObj = projectile.GetComponent<NetworkObject>();
        if (projectileNetObj != null)
        {
            projectileNetObj.Spawn();
        }
    }

    #endregion

    #region Server RPCs & Reconciliation

    /// <summary>
    /// Client/Host -> Server: "Here is my new input (move/turn + sequence ID)"
    /// </summary>
    [ServerRpc]
    private void SendInputToServerRpc(MovementInput input, ServerRpcParams serverRpcParams = default)
    {
        // (A) Server applies the authoritative movement
        ApplyMovementOnServer(input);

        // (B) Update last processed input
        lastProcessedInput = input.inputSequence;

        // (C) Return authoritative state for reconciliation
        ServerState newState = new ServerState
        {
            position = rb.position,
            rotation = rb.rotation,
            lastProcessedInput = lastProcessedInput
        };

        // Broadcast so the client can reconcile
        ReceiveServerStateClientRpc(newState);
    }

    /// <summary>
    /// Server -> Client: "Here's the authoritative position/rotation after input #XYZ."
    /// The owner client reconciles to fix prediction errors.
    /// </summary>
    [ClientRpc]
    private void ReceiveServerStateClientRpc(ServerState state)
    {
        // Only the owner needs reconciliation.
        // The server/host doesn't need it (no local prediction).
        if (!IsOwner || IsServer) 
            return;

        // 1) Snap to the server's authoritative position
        rb.position = state.position;
        rb.rotation = state.rotation;

        // 2) Remove all inputs that the server already processed
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

        // 3) Replay any unacknowledged inputs
        for (int j = 0; j < pendingInputs.Count; j++)
        {
            ApplyMovementInput(pendingInputs[j]);
        }
    }

    #endregion
}
