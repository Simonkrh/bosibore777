using UnityEngine;

[RequireComponent(typeof(Projectile))]
public class LazerProjectileRangeLimiter : MonoBehaviour
{
    private Projectile projectile;
    private bool configured;
    private Vector2 previousPosition;
    private float maxTravelDistance;
    private float traveledDistance;

    private void Awake()
    {
        projectile = GetComponent<Projectile>();
    }

    public void Configure(Vector2 startPosition, float maxDistance)
    {
        previousPosition = startPosition;
        maxTravelDistance = Mathf.Max(0f, maxDistance);
        traveledDistance = 0f;
        configured = true;
    }

    private void FixedUpdate()
    {
        if (!configured || projectile == null || !projectile.IsServer)
        {
            return;
        }

        Vector2 currentPosition = transform.position;
        traveledDistance += Vector2.Distance(previousPosition, currentPosition);
        previousPosition = currentPosition;

        if (traveledDistance < maxTravelDistance)
        {
            return;
        }

        projectile.ForceDestroy();
        configured = false;
    }
}
