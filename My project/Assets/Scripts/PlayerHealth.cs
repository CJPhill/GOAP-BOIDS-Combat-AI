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

    public float CurrentHealth => currentHealth;
    public float HealthPercent => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    /// <summary>
    /// True when health has dropped to 0 and ResetHealth has not been called since.
    /// Stays true until the next ResetHealth so ExperimentRunner can detect
    /// PlayerDeath as a trial-termination condition.
    /// </summary>
    public bool IsDead => currentHealth <= 0f;

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
        // Stay dead — ExperimentRunner detects IsDead to terminate the trial,
        // then calls ResetHealth() before the next trial starts.
    }
}
