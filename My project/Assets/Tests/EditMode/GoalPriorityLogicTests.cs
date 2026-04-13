using NUnit.Framework;
using static GoalPriorityResolver;

[TestFixture]
public class GoalPriorityLogicTests
{
    // ── Scatter (critical health + player nearby) ──

    [Test]
    public void CriticalHealth_ReturnsScatter()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.10f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Scatter, goal);
    }

    // ── Flee (low health + player nearby) ──

    [Test]
    public void LowHealth_ReturnsFlee()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.25f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flee, goal);
    }

    // ── Attack (melee, cooldown ready) ──

    [Test]
    public void CooldownReady_Melee_ReturnsAttack()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Attack, goal);
    }

    // ── Kite (ranged, close to player) ──

    [Test]
    public void CooldownReady_Ranged_Close_ReturnsKite()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: true, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Kite, goal);
    }

    // ── RangedAttack (ranged, far enough from player) ──

    [Test]
    public void CooldownReady_Ranged_Far_ReturnsRangedAttack()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: true,
            isRanged: true, playerDist: 12f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.RangedAttack, goal);
    }

    // ── Regroup (isolated from flock) ──

    [Test]
    public void Isolated_ReturnsRegroup()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 50f, isIsolated: true, flockCount: 5);

        Assert.AreEqual(GoalType.Regroup, goal);
    }

    // ── Flank (cooldown not ready, player nearby, enough agents) ──

    [Test]
    public void MultipleAgents_CooldownActive_ReturnsFlank()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 10f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flank, goal);
    }

    // ── Guard (player at mid range) ──

    [Test]
    public void MidRange_ReturnsGuard()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 20f, isIsolated: false, flockCount: 1);

        Assert.AreEqual(GoalType.Guard, goal);
    }

    // ── Wander (nothing else triggers) ──

    [Test]
    public void Nothing_ReturnsWander()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 50f, isIsolated: false, flockCount: 1);

        Assert.AreEqual(GoalType.Wander, goal);
    }

    // ── Priority ordering ──

    [Test]
    public void ScatterBeatsFleeAtCriticalHealth()
    {
        // health=10% is below both scatter (15%) and flee (30%) thresholds
        // Scatter should win because it has higher priority
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.10f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Scatter, goal, "Scatter should beat Flee at critical health");
    }

    [Test]
    public void FleeBeatsAttackAtLowHealth()
    {
        // health=25%, cooldown ready — Flee should beat Attack
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.25f, playerNearby: true, cooldownReady: true,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flee, goal, "Flee should beat Attack at low health");
    }

    // ── Leader goal delegates to same logic ──

    [Test]
    public void LeaderGoal_DelegatesToSameLogic()
    {
        var goapGoal = ResolveGOAPBoidGoal(
            0.10f, true, true, false, 5f, false, 5);
        var leaderGoal = ResolveLeaderGoal(
            0.10f, true, true, false, 5f, false, 5);

        Assert.AreEqual(goapGoal, leaderGoal, "Leader should resolve same goal as GOAP boid");
    }

    // ── Boundary thresholds ──

    [Test]
    public void HealthExactlyAtCriticalThreshold_DoesNotScatter()
    {
        // At exactly 0.15 (not below), scatter should NOT trigger
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.15f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreNotEqual(GoalType.Scatter, goal, "Exactly at threshold should NOT scatter");
    }

    [Test]
    public void HealthJustBelowFleeThreshold_ReturnsFlee()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.29f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 5);

        Assert.AreEqual(GoalType.Flee, goal);
    }

    [Test]
    public void FlankRequiresAtLeast3Agents()
    {
        // Only 2 agents — should NOT flank
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 10f, isIsolated: false, flockCount: 2);

        Assert.AreNotEqual(GoalType.Flank, goal);
    }

    [Test]
    public void GuardRange_OutsideOuter_ReturnsWander()
    {
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.8f, playerNearby: false, cooldownReady: false,
            isRanged: false, playerDist: 35f, isIsolated: false, flockCount: 1);

        Assert.AreEqual(GoalType.Wander, goal, "Outside guard outer range should wander");
    }

    [Test]
    public void CustomThresholds_Respected()
    {
        // Custom critical health = 0.5, so health=0.4 triggers scatter
        var goal = ResolveGOAPBoidGoal(
            healthPercent: 0.4f, playerNearby: true, cooldownReady: false,
            isRanged: false, playerDist: 5f, isIsolated: false, flockCount: 1,
            criticalHealthThreshold: 0.5f);

        Assert.AreEqual(GoalType.Scatter, goal, "Custom critical threshold should be respected");
    }
}
