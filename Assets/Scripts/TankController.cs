using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

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
        if (!IsServer)
        {
            rb.isKinematic = true;
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
            // 1) Read input
            float moveInput = Input.GetAxisRaw("Vertical");
            float turnInput = Input.GetAxisRaw("Horizontal");

            // 2) Send input to the server function 
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
            float moveInput = Input.GetAxisRaw("Vertical");
            float turnInput = Input.GetAxisRaw("Horizontal");

            MovementInput newInput = new MovementInput
            {
                moveInput = moveInput,
                rotationInput = turnInput,
                inputSequence = nextInputSequence++
            };

            // Immediate local prediction
            ApplyMovementInput(newInput);

            // Add to pending
            pendingInputs.Add(newInput);

            // Send to server for authoritative movement
            SendInputToServerRpc(newInput);
        }
    }

    private void FixedUpdate()
    {
        // The server updates the authoritative position & rotation
        if (IsServer)
        {
            networkPosition.Value = rb.position;
            networkRotation.Value = rb.rotation;
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

    private void ApplyMovementInput(MovementInput input)
    {
        float move = input.moveInput;
        float turn = input.rotationInput;

        Vector2 moveVector = transform.up * move * moveSpeed * Time.fixedDeltaTime;
        float rotation = turn * rotationSpeed * Time.fixedDeltaTime;

        rb.position += moveVector;
        rb.rotation -= rotation;
    }

    private void ApplyMovementOnServer(MovementInput input)
    {
        float move = input.moveInput;
        float turn = input.rotationInput;

        Vector2 moveVector = transform.up * move * moveSpeed * Time.fixedDeltaTime;
        float rotation = turn * rotationSpeed * Time.fixedDeltaTime;

        rb.MovePosition(rb.position + moveVector);
        rb.MoveRotation(rb.rotation - rotation);
    }

    private void SmoothlyInterpolatePositionAndRotation()
    {
        float lerpSpeed = 25f;
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

    [ServerRpc]
    private void SendInputToServerRpc(MovementInput input, ServerRpcParams serverRpcParams = default)
    {
        ApplyMovementOnServer(input);
        lastProcessedInput = input.inputSequence;

        ServerState newState = new ServerState
        {
            position = rb.position,
            rotation = rb.rotation,
            lastProcessedInput = lastProcessedInput
        };

        ReceiveServerStateClientRpc(newState);
    }

    [ClientRpc]
    private void ReceiveServerStateClientRpc(ServerState state)
    {
        if (!IsOwner || IsServer) 
            return;

        rb.position = state.position;
        rb.rotation = state.rotation;

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

        for (int j = 0; j < pendingInputs.Count; j++)
        {
            ApplyMovementInput(pendingInputs[j]);
        }
    }

    #endregion
}
