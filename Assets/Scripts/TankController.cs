using Unity.Netcode;
using UnityEngine;

public class TankController : NetworkBehaviour
{
    public float moveSpeed = 1.8f; // Speed of the tank's movement
    public float rotationSpeed = 300f; // Speed of the tank's rotation in degrees per second
    public GameObject projectilePrefab; // Prefab of the projectile
    public float projectileSpeed = 10f; // Speed of the projectile
    public float shootCooldown = 0.5f; // Cooldown time between shots
    public float shootingOffsetDistance = 1.0f; // Shooting offset in front of the tank

    private Rigidbody2D rb;
    private float lastShotTime;

    // Network variables for position and rotation
    private NetworkVariable<Vector2> networkPosition = new NetworkVariable<Vector2>(
        writePerm: NetworkVariableWritePermission.Server);
    private NetworkVariable<float> networkRotation = new NetworkVariable<float>(
        writePerm: NetworkVariableWritePermission.Server);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0; // Ensure no gravity affects the tank
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            Debug.Log($"[OnNetworkSpawn] Client {NetworkManager.Singleton.LocalClientId} is the owner of this tank.");
        }
        else
        {
            Debug.Log($"[OnNetworkSpawn] This tank is owned by Client {OwnerClientId}, Local Client: {NetworkManager.Singleton.LocalClientId}");
        }

        if (!IsServer)
        {
            rb.isKinematic = true; // Prevent physics simulation on clients
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            HandleShooting();
            HandleMovementInput();
        }
    }

    private void FixedUpdate()
    {
        if (IsServer)
        {
            UpdateNetworkVariables();
        }
        else if (!IsOwner)
        {
            SmoothlyInterpolatePositionAndRotation();
        }
    }

    private void HandleMovementInput()
    {
        float moveInput = Input.GetAxisRaw("Vertical");
        float rotationInput = Input.GetAxisRaw("Horizontal");

        // Request the server to handle movement
        RequestMovementServerRpc(moveInput, rotationInput);
    }

    [ServerRpc]
    private void RequestMovementServerRpc(float moveInput, float rotationInput, ServerRpcParams rpcParams = default)
    {
        HandleMovementAndRotation(moveInput, rotationInput);

        // Update the network variables to ensure sync
        UpdateNetworkVariables();
    }

    private void HandleMovementAndRotation(float moveInput, float rotationInput)
    {
        Vector2 moveVector = transform.up * moveInput * moveSpeed * Time.fixedDeltaTime;
        float rotation = rotationInput * rotationSpeed * Time.fixedDeltaTime;

        rb.MovePosition(rb.position + moveVector);
        rb.MoveRotation(rb.rotation - rotation);
    }

    private void UpdateNetworkVariables()
    {
        networkPosition.Value = rb.position;
        networkRotation.Value = rb.rotation;
    }

    private void SmoothlyInterpolatePositionAndRotation()
    {
        rb.position = Vector2.Lerp(rb.position, networkPosition.Value, Time.fixedDeltaTime * 10f);
        rb.rotation = Mathf.LerpAngle(rb.rotation, networkRotation.Value, Time.fixedDeltaTime * 10f);
    }

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

        Rigidbody2D projectileRb = projectile.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            projectileRb.linearVelocity = transform.up * projectileSpeed;
        }

        NetworkObject projectileNetworkObject = projectile.GetComponent<NetworkObject>();
        if (projectileNetworkObject != null)
        {
            projectileNetworkObject.Spawn();
        }
    }
}
