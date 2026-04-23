using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// GOAP brain for the flock leader in BOIDSWithGOAPLeader condition.
/// Same priority as other conditions: Flee > Attack > Wander.
/// Uses flock health (from FlockManager) for flee decisions.
/// </summary>
[RequireComponent(typeof(BoidAgent))]
[RequireComponent(typeof(GoapActionProvider))]
public class LeaderGoapBrain : MonoBehaviour
{
    [SerializeField] private float playerDetectionRange = 30f;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float maxLeaderSeparation = 12f;

    private BoidAgent boid;
    private BoidGoapBrain brain;
    private GoapActionProvider provider;
    private Transform playerTransform;

    /// <summary>The last resolved goal type (for metrics/testing).</summary>
    [HideInInspector] public GoalPriorityResolver.GoalType currentGoalType;
    private GoalPriorityResolver.GoalType previousGoalType = GoalPriorityResolver.GoalType.Wander;

    private void Awake()
    {
        boid = GetComponent<BoidAgent>();
        brain = GetComponent<BoidGoapBrain>();
        provider = GetComponent<GoapActionProvider>();
    }

    private void Update()
    {
        if (provider == null || provider.AgentType == null)
            return;
        if (boid.manager == null)
            return;

        // Cache player
        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
                playerTransform = player.transform;
        }

        // Tick cooldown
        if (brain != null && brain.cooldownTimer > 0f)
            brain.cooldownTimer -= Time.deltaTime;

        // Check player proximity
        bool playerNearby = false;
        if (playerTransform != null)
        {
            float dist = Vector3.Distance(transform.position, playerTransform.position);
            playerNearby = dist <= playerDetectionRange;
        }

        // Goal priority: Scatter > Flee > Attack/Kite > Regroup > Flank > Guard > Wander
        float healthPercent = boid.manager.HealthPercent;
        float criticalThreshold = boid.settings != null ? boid.settings.criticalHealthThreshold : 0.15f;
        bool cooldownReady = brain == null || brain.cooldownTimer <= 0f;
        bool isRanged = boid.settings != null && boid.settings.flockType == FlockType.Ranged;
        float playerDist = playerTransform != null
            ? Vector3.Distance(transform.position, playerTransform.position)
            : float.MaxValue;
        float guardInner = boid.settings != null ? boid.settings.guardInnerRange : 15f;
        float guardOuter = boid.settings != null ? boid.settings.guardOuterRange : 30f;
        float kiteMin = boid.settings != null ? boid.settings.kiteMinDistance : 8f;
        float isolationThreshold = boid.settings != null ? boid.settings.isolationThreshold : 20f;

        bool isIsolated = boid.manager != null &&
            Vector3.Distance(transform.position, boid.manager.GetFlockCenter()) > isolationThreshold;
        int flockCount = boid.manager != null ? boid.manager.BoidCount : 0;

        var goal = GoalPriorityResolver.ResolveLeaderGoal(
            healthPercent, playerNearby, cooldownReady, isRanged,
            playerDist, isIsolated, flockCount,
            criticalThreshold, kiteMin, guardInner, guardOuter);

        currentGoalType = goal;

        if (goal != previousGoalType)
        {
            // Use the flock's type as the flockId so melee-leader events land in
            // flockId=0 and ranged-leader events land in flockId=1. Previously
            // hardcoded to 0 which collapsed both leaders' events into one bucket,
            // leaving ReactionRangedMs perpetually -1 in trial summaries.
            int flockId = boid.settings != null ? (int)boid.settings.flockType : 0;
            BehavioralMetricsCollector.Instance?.LogEvent(
                "GoalChange", gameObject.name, flockId, $"{previousGoalType}→{goal}");
            previousGoalType = goal;
        }

        switch (goal)
        {
            case GoalPriorityResolver.GoalType.Scatter:
                provider.RequestGoal<LeaderScatterGoal>(); break;
            case GoalPriorityResolver.GoalType.Flee:
                provider.RequestGoal<LeaderFleeGoal>(); break;
            case GoalPriorityResolver.GoalType.Attack:
                provider.RequestGoal<LeaderAttackGoal>(); break;
            case GoalPriorityResolver.GoalType.Kite:
                provider.RequestGoal<LeaderKiteGoal>(); break;
            case GoalPriorityResolver.GoalType.Regroup:
                provider.RequestGoal<LeaderRegroupGoal>(); break;
            case GoalPriorityResolver.GoalType.Flank:
                provider.RequestGoal<LeaderFlankGoal>(); break;
            case GoalPriorityResolver.GoalType.Guard:
                provider.RequestGoal<LeaderGuardGoal>(); break;
            default:
                provider.RequestGoal<LeaderWanderGoal>(); break;
        }

        // Leash: if leader is overriding movement and has outrun the flock, slow down
        // so followers can catch up rather than the leader abandoning the group.
        if (brain != null && brain.isMovementOverridden && boid.manager != null)
        {
            Vector3 flockCenter = boid.manager.GetFlockCenter();
            float dist = Vector3.Distance(transform.position, flockCenter);
            if (dist > maxLeaderSeparation)
            {
                float excess = dist - maxLeaderSeparation;
                float slowFactor = Mathf.Clamp01(1f - excess / maxLeaderSeparation);
                boid.velocity *= slowFactor;
            }
        }
    }
}
