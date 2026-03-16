using UnityEngine;

public class BoidProjectile : MonoBehaviour
{
    private Vector3 direction;
    private float speed;
    private float damage;
    private float lifetime = 6f;

    public void Initialize(Vector3 dir, float spd, float dmg)
    {
        direction = dir.normalized;
        speed = spd;
        damage = dmg;
    }

    private void Update()
    {
        transform.position += direction * speed * Time.deltaTime;

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f)
            Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            other.GetComponent<PlayerHealth>()?.TakeDamage(damage);
            Destroy(gameObject);
        }
    }
}
