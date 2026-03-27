using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    private ulong lastAttackerId = 0;
    private GameManager gameManager;

    [Header("Death Smoke")]
    [Tooltip("Smoke burst played when this player dies.")]
    [SerializeField]
    private DirectionalSmokeBurst.Config deathSmoke = new DirectionalSmokeBurst.Config
    {
        smokeColor = new Color(0.1f, 0.1f, 0.1f, 0.8f),
        sortingOrder = 120,
        circleCount = 22,
        lifetimeRange = new Vector2(0.45f, 0.8f),
        startScaleRange = new Vector2(0.03f, 0.055f),
        endScaleMultiplierRange = new Vector2(2.8f, 4.2f),
        startOpacityMultiplierRange = new Vector2(0.65f, 0.9f),
        directionVariationDegrees = 180f,
        speedRange = new Vector2(0.7f, 1.45f),
        spawnRadiusRange = new Vector2(0f, 0.09f),
        angularVelocityRange = new Vector2(-45f, 45f)
    };

    [Header("Death Debris")]
    [Tooltip("Debris pieces spawned when this player dies.")]
    [SerializeField] private TankDeathDebrisBurst.Config deathDebris = new TankDeathDebrisBurst.Config();

    [Header("Debug")]
    [Tooltip("When enabled, this player cannot die. Server-authoritative.")]
    [SerializeField] private bool godMode;

    public bool GodMode => godMode;

    public void SetGodModeServer(bool enabled)
    {
        if (!IsServer)
        {
            return;
        }

        godMode = enabled;
    }

    public void Die(ulong killerId)
    {
        if (!IsServer || !IsSpawned) return;  // Only the server kills spawned players
        if (godMode) return;

        Vector3 deathPosition = transform.position;
        Vector2 facingDirection = ResolveFacingDirection();
        Color deathColor = ResolveDeathColor();
        int deathEffectSeed = Random.Range(int.MinValue, int.MaxValue);

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        if (gameManager != null)
        {
            gameManager.PlayPlayerDieSoundServer(transform.position);
            gameManager.PlayerDied(OwnerClientId, killerId);
        }

        PlayDeathEffectsClientRpc(deathPosition, facingDirection, deathColor, deathEffectSeed);

        // Despawn the player object
        NetworkObject.Despawn(true);
    }

    [ClientRpc]
    private void PlayDeathEffectsClientRpc(Vector3 deathPosition, Vector2 facingDirection, Color deathColor, int deathEffectSeed)
    {
        PlayDeathEffectsLocal(deathPosition, facingDirection, deathColor, deathEffectSeed);
    }

    private void PlayDeathEffectsLocal(Vector3 deathPosition, Vector2 facingDirection, Color deathColor, int deathEffectSeed)
    {
        TryPlayDeathSmoke(deathPosition, facingDirection);
        TankDeathDebrisBurst.Spawn(deathDebris, deathPosition, deathColor, facingDirection, deathEffectSeed);
    }

    private void TryPlayDeathSmoke(Vector3 deathPosition, Vector2 facingDirection)
    {
        if (deathSmoke == null)
        {
            return;
        }

        if (deathSmoke.TryCreateSettings(deathPosition, facingDirection, out DirectionalSmokeBurst.Settings settings))
        {
            DirectionalSmokeBurst.Spawn(settings, "PlayerDeathSmokeBurst");
        }
    }

    private Vector2 ResolveFacingDirection()
    {
        Vector2 facingDirection = Vector2.up;
        TankController tankController = GetComponent<TankController>();
        if (tankController == null)
        {
            return facingDirection;
        }

        Transform facingTransform = tankController.rotationChild != null ? tankController.rotationChild : tankController.transform;
        if (facingTransform != null)
        {
            facingDirection = facingTransform.up;
        }

        return facingDirection;
    }

    private Color ResolveDeathColor()
    {
        TankController tankController = GetComponent<TankController>();
        if (tankController != null)
        {
            return tankController.tankColor.Value;
        }

        SpriteRenderer spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        return spriteRenderer != null ? spriteRenderer.color : Color.white;
    }

    private void OnValidate()
    {
        deathSmoke?.ClampInEditor();
        deathDebris?.ClampInEditor();
    }
}
