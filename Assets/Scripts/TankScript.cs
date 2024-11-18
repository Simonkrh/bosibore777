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

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0;
    }

    private void Start()
    {
        if (IsOwner)
        {
            Debug.Log($"[Owner] Player {NetworkManager.Singleton.LocalClientId} owns this tank.");
        }
    }

    private void Update()
    {
        if (!IsOwner) return; // Only the owner handles input locally.

        HandleShooting();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return; // Only the owner sends movement to the server.

        float moveInput = Input.GetAxisRaw("Vertical");
        float rotationInput = Input.GetAxisRaw("Horizontal");

        if (moveInput != 0 || rotationInput != 0)
        {
            UpdateMovementServerRpc(moveInput, rotationInput);
        }
    }

    [ServerRpc]
    private void UpdateMovementServerRpc(float moveInput, float rotationInput, ServerRpcParams rpcParams = default)
    {
        Debug.Log($"[Server] Received movement from Client {rpcParams.Receive.SenderClientId}: MoveInput: {moveInput}, RotationInput: {rotationInput}");

        // Only process movement for the correct owner
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Server] Ignored movement from Client {rpcParams.Receive.SenderClientId} (not the owner)");
            return;
        }

        // Process movement
        Vector2 moveVector = transform.up * moveInput * moveSpeed * Time.fixedDeltaTime;
        float rotation = rotationInput * rotationSpeed * Time.fixedDeltaTime;

        // Update Rigidbody position and rotation
        rb.MovePosition(rb.position + moveVector);
        rb.MoveRotation(rb.rotation - rotation);

        // Sync with clients
        UpdateMovementClientRpc(rb.position, rb.rotation);
    }


    [ClientRpc]
    private void UpdateMovementClientRpc(Vector2 position, float rotation)
    {
        if (IsOwner) return; // Skip the owner, as they already process movement locally.

        // Synchronize position and rotation for non-owners
        rb.position = position;
        rb.rotation = rotation;
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

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Wall"))
        {
            rb.linearVelocity = Vector2.zero; // Stop all motion
        }
    }
}
