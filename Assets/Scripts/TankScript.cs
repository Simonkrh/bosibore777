using Unity.Netcode;
using UnityEngine;

public class TankController : NetworkBehaviour
{
    public float moveSpeed = 1.8f; // Speed of the tank's movement
    public float rotationSpeed = 300f; // Speed of the tank's rotation in degrees per second
    public GameObject projectilePrefab; // Prefab of the projectile
    public float projectileSpeed = 10f; // Speed of the projectile
    public float shootCooldown = 0.5f; // Cooldown time between shots
    public float startShootCooldown = 0.0f;
    public float shootingOffsetDistance = 1.0f; // Shooting offset in front of the tank

    private Rigidbody2D rb;
    private float lastShotTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0;
    }

    private void Update()
    {
        if (!IsOwner) return;
        HandleShooting();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        
        HandleMovement();
        HandleRotation();
    }

    private void HandleMovement()
    {
        float moveInput = Input.GetAxisRaw("Vertical"); // W = 1, S = -1
        Vector2 moveVector = transform.up * moveInput * moveSpeed * Time.fixedDeltaTime;

        if (moveInput != 0)
        {
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

        rb.MovePosition(rb.position + moveVector);
    }

    private void HandleRotation()
    {
        float rotationInput = Input.GetAxisRaw("Horizontal"); // A = -1, D = 1
        float rotation = rotationInput * rotationSpeed * Time.fixedDeltaTime;

        if (rotationInput != 0)
        {
            rb.constraints = RigidbodyConstraints2D.None;
        }

        rb.MoveRotation(rb.rotation - rotation);
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
        Vector3 spawnPosition = transform.position + transform.up * 1.0f;
        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, transform.rotation);
        projectile.GetComponent<Rigidbody2D>().velocity = transform.up * projectileSpeed;

        // Spawn the projectile across all clients
        projectile.GetComponent<NetworkObject>().Spawn();
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Wall"))
        {
            rb.velocity = Vector2.zero; // Stop all motion
        }
    }
}
