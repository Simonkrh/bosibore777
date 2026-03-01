using Unity.Netcode;
using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Behaviors/Homing Missile", fileName = "HomingMissileAbilityBehavior")]
public class HomingMissileAbilityBehavior : AbilityBehavior
{
    [SerializeField] private GameObject homingMissilePrefab;
    [SerializeField] private float missileSpeed = 8f;
    [SerializeField] private float extraSpawnDistance = 0f;

    public override bool TryActivateServer(TankController owner, int shotSequence)
    {
        if (owner == null || homingMissilePrefab == null || !owner.IsServer)
        {
            return false;
        }

        if (!owner.TryComputeAbilityProjectileSpawn(
                extraSpawnDistance,
                out Vector2 spawnPosition2D,
                out Quaternion spawnRotation,
                out Vector2 fireDirection,
                out _))
        {
            return false;
        }

        if (!owner.TrySpawnAbilityProjectile(
                homingMissilePrefab,
                shotSequence,
                spawnPosition2D,
                spawnRotation,
                fireDirection,
                missileSpeed,
                out NetworkObject spawnedProjectile))
        {
            return false;
        }

        return true;
    }

    private NetworkObject FindClosestTarget(TankController owner)
    {
        if (owner == null || owner.NetworkManager == null)
        {
            return null;
        }

        GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            return null;
        }

        Vector2 origin = owner.transform.position;
        float bestDistanceSqr = float.MaxValue;
        NetworkObject closest = null;

        for (int i = 0; i < owner.NetworkManager.ConnectedClientsList.Count; i++)
        {
            ulong clientId = owner.NetworkManager.ConnectedClientsList[i].ClientId;
            if (clientId == owner.OwnerClientId)
            {
                continue;
            }

            if (!gameManager.TryGetPlayerObject(clientId, out GameObject playerObject) || playerObject == null)
            {
                continue;
            }

            NetworkObject targetNetworkObject = playerObject.GetComponent<NetworkObject>();
            if (targetNetworkObject == null || !targetNetworkObject.IsSpawned)
            {
                continue;
            }

            float distanceSqr = ((Vector2)playerObject.transform.position - origin).sqrMagnitude;
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                closest = targetNetworkObject;
            }
        }

        return closest;
    }
}
