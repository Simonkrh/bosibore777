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
    private NetworkVariable<Vector2> networkPosition = new NetworkVariable<Vector2>();
    private NetworkVariable<float> networkRotation = new NetworkVariable<float>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0; // Ensure no gravity affects the tank
    }

    private void Start()
    {
        if (IsOwner)
        {
            Debug.Log($"[Owner] Player {CustomNetworkManager.Singleton.LocalClientId} owns this tank.");
        }
    }

    private void Update()
    {
        if (IsOwner)
        {
            HandleShooting();
        }
    }

    private void FixedUpdate()
    {
        if (IsOwner)
        {
            HandleMovementAndRotation();
        }
        else
        {
            SyncNonOwnerPositionAndRotation();
        }
    }

    private void HandleMovementAndRotation()
    {
        // Local movement and rotation input
        float moveInput = Input.GetAxisRaw("Vertical");
        float rotationInput = Input.GetAxisRaw("Horizontal");

        Vector2 moveVector = transform.up * moveInput * moveSpeed * Time.fixedDeltaTime;
        float rotation = rotationInput * rotationSpeed * Time.fixedDeltaTime;

        // Update local Rigidbody
        rb.MovePosition(rb.position + moveVector);
        rb.MoveRotation(rb.rotation - rotation);

        // Update the server with new position and rotation
        UpdateMovementServerRpc(rb.position, rb.rotation);
    }

    private void SyncNonOwnerPositionAndRotation()
    {
        // Smoothly synchronize non-owner position and rotation
        rb.position = Vector2.Lerp(rb.position, networkPosition.Value, Time.fixedDeltaTime * 10f);
        rb.rotation = Mathf.LerpAngle(rb.rotation, networkRotation.Value, Time.fixedDeltaTime * 10f);
    }

    [ServerRpc]
    private void UpdateMovementServerRpc(Vector2 position, float rotation, ServerRpcParams rpcParams = default)
    {
        // Update position and rotation on the server
        networkPosition.Value = position;
        networkRotation.Value = rotation;

        // Optional: Enforce constraints or validation logic here if needed
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

        // Create projectile at shooting offset position
        Vector3 spawnPosition = transform.position + transform.up * shootingOffsetDistance;
        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, transform.rotation);

        // Set projectile velocity
        Rigidbody2D projectileRb = projectile.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            projectileRb.linearVelocity = transform.up * projectileSpeed;
        }

        // Spawn projectile for all clients
        NetworkObject projectileNetworkObject = projectile.GetComponent<NetworkObject>();
        if (projectileNetworkObject != null)
        {
            projectileNetworkObject.Spawn();
        }
    }
}
