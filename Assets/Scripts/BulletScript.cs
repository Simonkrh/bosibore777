using UnityEngine;

public class Projectile : MonoBehaviour
{
    public float lifetime = 10f; 

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player")) {
            collision.gameObject.GetComponent<PlayerController>().Die();
            Destroy(gameObject);
        }
    }
}
