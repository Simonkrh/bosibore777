using UnityEngine;

public class TankController : MonoBehaviour
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

    private void Update()
    {
        HandleShooting();
    }

    private void FixedUpdate()
    {
        HandleMovement();
        HandleRotation();
    }

    private void HandleMovement()
    {
        float moveInput = Input.GetAxisRaw("Vertical"); // W = 1, S = -1
        Vector2 moveVector = transform.up * moveInput * moveSpeed * Time.fixedDeltaTime;

        rb.MovePosition(rb.position + moveVector);
    }

    private void HandleRotation()
    {
        float rotationInput = Input.GetAxisRaw("Horizontal"); // A = -1, D = 1
        float rotation = rotationInput * rotationSpeed * Time.fixedDeltaTime;

        rb.MoveRotation(rb.rotation - rotation);
    }

    private void HandleShooting()
    {
        if (Input.GetKeyDown(KeyCode.Space) && Time.time >= lastShotTime + shootCooldown)
        {
            Shoot();
            lastShotTime = Time.time;
        }
    }

    private void Shoot()
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning("Projectile prefab is not assigned!");
            return;
        }

        Vector3 spawnPosition = transform.position + transform.up * shootingOffsetDistance;
        GameObject projectile = Instantiate(projectilePrefab, spawnPosition, transform.rotation);

        Rigidbody2D projectileRb = projectile.GetComponent<Rigidbody2D>();
        if (projectileRb != null)
        {
            projectileRb.velocity = transform.up * projectileSpeed;
        }
    }
}
