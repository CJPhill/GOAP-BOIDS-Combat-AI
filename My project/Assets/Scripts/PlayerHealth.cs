using UnityEngine;

/// <summary>
/// Minimal player health component.
/// Combat teammate: flesh this out with UI, death handling, etc.
/// Boids call TakeDamage() via BoidAgent when in melee range.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;

    private float currentHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth = Mathf.Max(currentHealth - amount, 0f);
        Debug.Log($"[PlayerHealth] Took {amount} damage. HP: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f)
            OnDeath();
    }

    private void OnDeath()
    {
        Debug.Log("[PlayerHealth] Player died.");
        // TODO: trigger death animation / game-over screen
    }
}
