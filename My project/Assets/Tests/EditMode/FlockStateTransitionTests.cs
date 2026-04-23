using NUnit.Framework;
using static FlockStateResolver;

/// <summary>
/// Pure-function tests for FlockStateResolver — the priority tree that decides
/// PureBOIDS flock state transitions. Mirrors GoalPriorityLogicTests so conditions
/// 2 and 4 fire analogous behaviors from the same stimulus.
/// </summary>
[TestFixture]
public class FlockStateTransitionTests
{
    // Shared default inputs — override per-test via named args.
    private static FlockBehavior Resolve(
        float hp = 0.8f,
        bool hasTarget = true,
        bool playerNearby = true,
        float playerDist = 6f,
        bool isRanged = false,
        bool attackSlotsSaturated = false,
        bool meleeAttackActive = false,
        bool rangedAttackActive = false,
        float maxDistanceFromCentroid = 3f,
        int boidCount = 5,
        bool clusteredEnoughToEngage = true)
    {
        return FlockStateResolver.Resolve(
            healthPercent: hp,
            hasTarget: hasTarget,
            playerNearby: playerNearby,
            playerDist: playerDist,
            isRanged: isRanged,
            attackSlotsSaturated: attackSlotsSaturated,
            meleeAttackActive: meleeAttackActive,
            rangedAttackActive: rangedAttackActive,
            maxDistanceFromCentroid: maxDistanceFromCentroid,
            boidCount: boidCount,
            clusteredEnoughToEngage: clusteredEnoughToEngage);
    }

    [Test]
    public void CriticalHealth_PlayerNearby_Scatters()
    {
        Assert.AreEqual(FlockBehavior.Scattering, Resolve(hp: 0.10f));
    }

    [Test]
    public void LowHealth_PlayerNearby_Flees()
    {
        Assert.AreEqual(FlockBehavior.Fleeing, Resolve(hp: 0.25f));
    }

    [Test]
    public void Ranged_PlayerInsideKiteDistance_Kites()
    {
        Assert.AreEqual(FlockBehavior.Kiting, Resolve(isRanged: true, playerDist: 5f));
    }

    [Test]
    public void Ranged_PlayerBeyondKiteDistance_Engages()
    {
        Assert.AreEqual(FlockBehavior.Engaging, Resolve(isRanged: true, playerDist: 12f));
    }

    [Test]
    public void Melee_PlayerClose_Engages()
    {
        Assert.AreEqual(FlockBehavior.Engaging, Resolve(isRanged: false, playerDist: 5f));
    }

    [Test]
    public void NoPlayerNearby_SpreadFlock_Regroups()
    {
        Assert.AreEqual(FlockBehavior.Regrouping,
            Resolve(playerNearby: false, maxDistanceFromCentroid: 25f, boidCount: 4));
    }

    [Test]
    public void SlotsSaturated_DuringActiveAttack_Flanks()
    {
        Assert.AreEqual(FlockBehavior.Flanking,
            Resolve(attackSlotsSaturated: true, meleeAttackActive: true));
    }

    [Test]
    public void PlayerAtMidRange_NoActiveAttack_Guards()
    {
        // playerDist in [15, 30], playerNearby=false (beyond aggro, but guard
        // band captures the mid-range) → Guarding.
        Assert.AreEqual(FlockBehavior.Guarding,
            Resolve(playerNearby: false, playerDist: 22f, clusteredEnoughToEngage: false));
    }

    [Test]
    public void NoTarget_Idles()
    {
        Assert.AreEqual(FlockBehavior.Idle,
            Resolve(hasTarget: false, playerNearby: false, clusteredEnoughToEngage: false));
    }

    [Test]
    public void HasTarget_NotClusteredYet_Groups()
    {
        Assert.AreEqual(FlockBehavior.Grouping,
            Resolve(playerNearby: false, playerDist: 50f, clusteredEnoughToEngage: false));
    }

    // ── Priority-ordering invariants ──

    [Test]
    public void ScatterBeatsFleeWhenBothWouldFire()
    {
        // HP < critical AND < flee threshold — critical should win.
        Assert.AreEqual(FlockBehavior.Scattering, Resolve(hp: 0.10f));
    }

    [Test]
    public void FleeBeatsKiteWhenBothWouldFire()
    {
        // Ranged + close + low HP — Flee priority over Kite.
        Assert.AreEqual(FlockBehavior.Fleeing,
            Resolve(hp: 0.25f, isRanged: true, playerDist: 5f));
    }

    [Test]
    public void KiteBeatsEngageForRangedTooClose()
    {
        Assert.AreEqual(FlockBehavior.Kiting, Resolve(isRanged: true, playerDist: 5f));
    }

    [Test]
    public void EngageBeatsGuardWhenPlayerInBoth()
    {
        // playerDist in [15,30] and playerNearby=true and clustered → Engage wins.
        Assert.AreEqual(FlockBehavior.Engaging,
            Resolve(playerDist: 18f, clusteredEnoughToEngage: true));
    }
}
