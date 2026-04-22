using UnityEngine;

/// <summary>
/// Minimal player health component.
/// Combat teammate: flesh this out with UI, death handling, etc.
/// Boids call TakeDamage() via BoidAgent when in melee range.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 500f;

    private float currentHealth;

    public float HealthPercent => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    /// <summary>Set true by AutomatedPlayer during a dodge roll — blocks incoming damage.</summary>
    public bool IsInvincible { get; set; }

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (IsInvincible) return;

        currentHealth = Mathf.Max(currentHealth - amount, 0f);

        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerDamage", gameObject.name, -1, $"Amount={amount:F1},HP={currentHealth:F1}");

        if (currentHealth <= 0f)
            OnDeath();
    }

    /// <summary>
    /// Resets health to max. Called by ExperimentRunner between conditions.
    /// </summary>
    public void ResetHealth()
    {
        currentHealth = maxHealth;
    }

    private void OnDeath()
    {
        Debug.Log("[PlayerHealth] Player died.");
        BehavioralMetricsCollector.Instance?.LogEvent(
            "PlayerDeath", gameObject.name, -1, "Player destroyed");
        // Reset health so experiments can continue
        ResetHealth();
    }
}
