using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode tests for the stress-mode invulnerability gates added in
/// methodology-revisions-2026-04 items 1 / 3b.
///
/// Verifies that <c>PlayerHealth.IsInvulnerableMode</c> blocks incoming damage
/// without interfering with the existing dodge-driven <c>IsInvincible</c> flag.
/// Flock-manager <c>SuspendDamage</c> behaviour is covered end-to-end by
/// BatchRunnerSmokeTest's stress-mode trial — those classes have scene-graph
/// dependencies (settings ScriptableObject, agent list, etc.) that aren't
/// worth reproducing in EditMode.
/// </summary>
[TestFixture]
public class StressModeGateTests
{
    private GameObject playerGO;
    private PlayerHealth ph;

    [SetUp]
    public void SetUp()
    {
        playerGO = new GameObject("TestPlayer");
        ph = playerGO.AddComponent<PlayerHealth>();
        ph.maxHealth = 100f;
        // MonoBehaviour.Awake() does NOT fire in EditMode tests — currentHealth
        // would stay at its default 0.0f, so TakeDamage(amount) clamps to 0 and
        // every assertion fails. Call ResetHealth() manually to mirror what
        // Awake would have done in PlayMode.
        ph.ResetHealth();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(playerGO);
    }

    [Test]
    public void TakeDamage_WithoutFlags_ReducesHealth()
    {
        ph.TakeDamage(25f);
        Assert.AreEqual(75f, ph.CurrentHealth, 1e-4f);
    }

    [Test]
    public void TakeDamage_WhenInvulnerableMode_HealthUnchanged()
    {
        ph.IsInvulnerableMode = true;
        ph.TakeDamage(25f);
        Assert.AreEqual(100f, ph.CurrentHealth, 1e-4f);
        Assert.IsFalse(ph.IsDead);
    }

    [Test]
    public void TakeDamage_WhenInvincible_HealthUnchanged()
    {
        // Sanity check: existing dodge-driven gate still works.
        ph.IsInvincible = true;
        ph.TakeDamage(25f);
        Assert.AreEqual(100f, ph.CurrentHealth, 1e-4f);
    }

    [Test]
    public void InvulnerableMode_PersistsAcrossResetHealth()
    {
        // ResetHealth is called between trials. Stress mode persists across that
        // boundary so a single SetStressMode(true) call covers the whole trial.
        ph.IsInvulnerableMode = true;
        ph.ResetHealth();
        Assert.IsTrue(ph.IsInvulnerableMode);
    }

    [Test]
    public void InvincibleResetByDodge_DoesNotClearInvulnerableMode()
    {
        // The dodge code clears IsInvincible at the end of a roll. That must
        // not affect IsInvulnerableMode (the stress flag).
        ph.IsInvulnerableMode = true;
        ph.IsInvincible = true;
        ph.IsInvincible = false; // simulate dodge end
        Assert.IsTrue(ph.IsInvulnerableMode, "Stress flag must survive dodge clear.");
        ph.TakeDamage(50f);
        Assert.AreEqual(100f, ph.CurrentHealth, 1e-4f);
    }
}
