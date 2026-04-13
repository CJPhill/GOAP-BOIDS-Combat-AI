/// <summary>
/// Pure-function goal priority resolution for both GOAP brain types.
/// Extracted from brain Update() logic to enable edit-mode unit testing.
/// </summary>
public static class GoalPriorityResolver
{
    public enum GoalType
    {
        Scatter,
        Flee,
        Attack,
        RangedAttack,
        Kite,
        Regroup,
        Flank,
        Guard,
        Wander
    }

    /// <summary>
    /// Resolves which goal should be active for a GOAPWithBOIDSMovement agent.
    /// Priority: Scatter > Flee > Attack/Kite > Regroup > Flank > Guard > Wander
    /// </summary>
    public static GoalType ResolveGOAPBoidGoal(
        float healthPercent,
        bool playerNearby,
        bool cooldownReady,
        bool isRanged,
        float playerDist,
        bool isIsolated,
        int flockCount,
        float criticalHealthThreshold = 0.15f,
        float kiteMinDistance = 8f,
        float guardInnerRange = 15f,
        float guardOuterRange = 30f)
    {
        if (playerNearby && healthPercent < criticalHealthThreshold)
            return GoalType.Scatter;

        if (playerNearby && healthPercent < 0.3f)
            return GoalType.Flee;

        if (playerNearby && cooldownReady)
        {
            if (isRanged && playerDist < kiteMinDistance)
                return GoalType.Kite;
            if (isRanged)
                return GoalType.RangedAttack;
            return GoalType.Attack;
        }

        if (isIsolated)
            return GoalType.Regroup;

        if (playerNearby && flockCount >= 3 && !cooldownReady)
            return GoalType.Flank;

        if (playerDist >= guardInnerRange && playerDist <= guardOuterRange)
            return GoalType.Guard;

        return GoalType.Wander;
    }

    /// <summary>
    /// Resolves which goal should be active for a BOIDSWithGOAPLeader leader.
    /// Same priority chain but reads thresholds from BoidSettings.
    /// </summary>
    public static GoalType ResolveLeaderGoal(
        float healthPercent,
        bool playerNearby,
        bool cooldownReady,
        bool isRanged,
        float playerDist,
        bool isIsolated,
        int flockCount,
        float criticalHealthThreshold = 0.15f,
        float kiteMinDistance = 8f,
        float guardInnerRange = 15f,
        float guardOuterRange = 30f)
    {
        // Same priority logic — leader and GOAPBoid share the same decision tree
        return ResolveGOAPBoidGoal(
            healthPercent, playerNearby, cooldownReady, isRanged,
            playerDist, isIsolated, flockCount,
            criticalHealthThreshold, kiteMinDistance, guardInnerRange, guardOuterRange);
    }
}
