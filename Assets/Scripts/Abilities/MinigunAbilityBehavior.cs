using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Abilities/Behaviors/Minigun", fileName = "MinigunAbilityBehavior")]
public class MinigunAbilityBehavior : AbilityBehavior
{
    [Header("Tank Visuals")]
    [Tooltip("Body prefab used while this ability is equipped. Must be inside a Resources folder.")]
    [SerializeField] private GameObject minigunTankBodyPrefab;
    [SerializeField, HideInInspector] private string minigunTankBodyPrefabResourcePath = "Prefabs/Abilities/Minigun/MinigunTankBody";

    [Header("Minigun")]
    [Tooltip("Projectile prefab fired by the minigun.")]
    [SerializeField] private GameObject miniBulletPrefab;
    [Tooltip("Spread half-angle in degrees. Actual shot angle is randomized in [-spread, +spread].")]
    [SerializeField] private float spreadDegrees = 8f;
    [Tooltip("How many bullets are fired once charge completes.")]
    [SerializeField] private int bulletCount = 20;
    [Tooltip("Total duration over which bullets are emitted.")]
    [SerializeField] private float firingDurationSeconds = 2.5f;
    [Tooltip("Launch speed of each mini bullet.")]
    [SerializeField] private float bulletSpeed = 2.4f;

    [Header("Timing")]
    [Tooltip("How long the player must hold before firing starts.")]
    [SerializeField] private float chargeUpSeconds = 1f;
    [Tooltip("How long after the full burst finishes before the ability clears.")]
    [SerializeField] private float clearAfterSeconds = 2f;

    [Header("Spawn")]
    [Tooltip("Extra forward spawn distance from the tank muzzle.")]
    [SerializeField] private float extraSpawnDistance = 0.03f;
    [Tooltip("Used to derive unique shot sequences per emitted mini bullet.")]
    [SerializeField] private int shotSequenceStride = 1000;
    [Tooltip("When enabled, mini bullets are tinted to the shooter's tank color.")]
    [SerializeField] private bool tintProjectilesWithShooterColor = true;

    [Header("Minigun Despawn Smoke")]
    [Tooltip("Directional smoke burst played when a minigun bullet despawns.")]
    [SerializeField] private DirectionalSmokeBurst.Config minigunBulletDespawnSmoke = new DirectionalSmokeBurst.Config();

    public override AbilityActivationResult TryActivateServer(TankController owner, int shotSequence)
    {
        if (owner == null || !owner.IsServer || miniBulletPrefab == null)
        {
            return AbilityActivationResult.NotActivated;
        }

        MinigunAbilityRuntime runtime = owner.GetComponent<MinigunAbilityRuntime>();
        if (runtime == null)
        {
            runtime = owner.gameObject.AddComponent<MinigunAbilityRuntime>();
        }

        if (runtime.IsRunning)
        {
            return AbilityActivationResult.ActivatedKeep;
        }

        runtime.Configure(
            owner,
            miniBulletPrefab,
            spreadDegrees,
            bulletCount,
            firingDurationSeconds,
            bulletSpeed,
            chargeUpSeconds,
            clearAfterSeconds,
            extraSpawnDistance,
            shotSequenceStride,
            tintProjectilesWithShooterColor,
            minigunBulletDespawnSmoke,
            HandleRuntimeCompletedServer);

        if (!runtime.BeginCharge(shotSequence))
        {
            return AbilityActivationResult.NotActivated;
        }

        TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
        if (abilityController != null)
        {
            abilityController.SetModelOverridePrefabResourceServer(minigunTankBodyPrefabResourcePath);
        }

        return AbilityActivationResult.ActivatedKeep;
    }

    public override void NotifyInputReleasedServer(TankController owner)
    {
        if (owner == null || !owner.IsServer)
        {
            return;
        }

        MinigunAbilityRuntime runtime = owner.GetComponent<MinigunAbilityRuntime>();
        if (runtime == null || !runtime.IsRunning)
        {
            return;
        }

        runtime.NotifyInputReleased();
    }

    private void HandleRuntimeCompletedServer(TankController owner)
    {
        if (owner == null || !owner.IsServer)
        {
            return;
        }

        TankAbilityController abilityController = owner.GetComponent<TankAbilityController>();
        if (abilityController != null)
        {
            abilityController.ClearEquippedAbilityServer();
        }
    }

    private void OnValidate()
    {
        spreadDegrees = Mathf.Max(0f, spreadDegrees);
        bulletCount = Mathf.Max(0, bulletCount);
        firingDurationSeconds = Mathf.Max(0f, firingDurationSeconds);
        bulletSpeed = Mathf.Max(0f, bulletSpeed);
        chargeUpSeconds = Mathf.Max(0f, chargeUpSeconds);
        clearAfterSeconds = Mathf.Max(0f, clearAfterSeconds);
        extraSpawnDistance = Mathf.Max(0f, extraSpawnDistance);
        shotSequenceStride = Mathf.Max(1, shotSequenceStride);
        minigunBulletDespawnSmoke?.ClampInEditor();

#if UNITY_EDITOR
        if (minigunTankBodyPrefab != null)
        {
            string prefabAssetPath = AssetDatabase.GetAssetPath(minigunTankBodyPrefab);
            string marker = "/Resources/";
            int markerIndex = prefabAssetPath.IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                string relativePath = prefabAssetPath.Substring(markerIndex + marker.Length);
                int extensionIndex = relativePath.LastIndexOf('.');
                minigunTankBodyPrefabResourcePath = extensionIndex >= 0
                    ? relativePath.Substring(0, extensionIndex)
                    : relativePath;
            }
        }
#endif
    }
}
