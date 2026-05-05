using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// GOAP brain for the flock leader in BOIDSWithGOAPLeader condition.
/// Same priority as other conditions: Flee > Attack > Wander.
/// Uses flock health (from FlockManager) for flee decisions.
/// </summary>
[RequireComponent(typeof(BoidAgent))]
[RequireComponent(typeof(GoapActionProvider))]
public class LeaderGoapBrain : MonoBehaviour
{
    /// <summary>
    /// When true, the leader emits diagnostic events into BehavioralMetricsCollector
    /// so a single editor trial produces a four-bucket reaction-time budget
    /// (StimulusAcquired → LeaderPerceives → GoalChange → LeaderActionStart).
    /// Off by default — flipped via Thesis → Toggle Leader Diagnostic.
    /// Investigates the v1 batch finding: Leader median melee reaction = 857 ms,
    /// vs PureBOIDS 2–43 ms and GOAPWithBOIDSMovement 5–59 ms.
    /// </summary>
    public static bool DiagnosticLogging = false;

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
    private bool wasPlayerNearby = false;

    /// <summary>
    /// Pure helper used by both Update() and EditMode tests. Maps a leader's
    /// BoidSettings.flockType to the flockId used by BehavioralMetricsCollector
    /// (melee=0, ranged=1). Null-safe; returns 0 when settings are missing so a
    /// mis-wired leader still emits *something* (callers can detect 0 as the
    /// fallback). Mirrors the inline mapping that previously lived at the
    /// LogEvent call site so the regression that was introduced by hardcoding
    /// flockId=0 (and that left ReactionRangedMs perpetually -1) cannot recur
    /// silently.
    /// </summary>
    public static int ResolveFlockId(BoidAgent boid)
    {
        if (boid == null || boid.settings == null) return 0;
        return (int)boid.settings.flockType;
    }

    /// <summary>
    /// Pure mapping from <see cref="GoalPriorityResolver.GoalType"/> to the
    /// concrete <c>GoalBase</c> subclass that <c>Update()</c> requests via
    /// <c>provider.RequestGoal&lt;T&gt;()</c>. Exposed so EditMode tests can
    /// lock the table down — the production switch in <see cref="Update"/> is
    /// the source of truth, but it can't be tested directly because
    /// <c>GoapActionProvider</c> isn't reachable from the EditMode test
    /// asmdef. This helper MUST stay byte-identical to the switch below;
    /// drift is what created the v1 ranged-leader bug (no case for
    /// <c>GoalType.RangedAttack</c> → silent fallback to Wander → ranged
    /// leader Wandered through every v1 trial while metrics logged it as
    /// engaged). Regression test: <c>LeaderGoalRequestMappingTests</c>.
    /// </summary>
    public static System.Type ResolveGoalRequestType(GoalPriorityResolver.GoalType goal)
    {
        switch (goal)
        {
            case GoalPriorityResolver.GoalType.Scatter:      return typeof(LeaderScatterGoal);
            case GoalPriorityResolver.GoalType.Flee:         return typeof(LeaderFleeGoal);
            case GoalPriorityResolver.GoalType.Attack:       return typeof(LeaderAttackGoal);
            case GoalPriorityResolver.GoalType.RangedAttack: return typeof(LeaderRangedAttackGoal);
            case GoalPriorityResolver.GoalType.Kite:         return typeof(LeaderKiteGoal);
            case GoalPriorityResolver.GoalType.Regroup:      return typeof(LeaderRegroupGoal);
            case GoalPriorityResolver.GoalType.Flank:        return typeof(LeaderFlankGoal);
            case GoalPriorityResolver.GoalType.Guard:        return typeof(LeaderGuardGoal);
            default:                                         return typeof(LeaderWanderGoal);
        }
    }

    private void Awake()
    {
        boid = GetComponent<BoidAgent>();
        brain = GetComponent<BoidGoapBrain>();
        provider = GetComponent<GoapActionProvider>();
    }

    private void Update()
    {
        Profiler.BeginSample("LeaderGoapBrain.Update");
        try
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

            // Diagnostic: rising-edge of "leader is now within its own detection
            // sphere of the player". This is the (b) boundary of the four-bucket
            // budget — the gap between StimulusAcquired (flock-level) and this
            // event is the H1 hypothesis (detection-range gap).
            if (DiagnosticLogging && playerNearby && !wasPlayerNearby)
            {
                int diagFlockId = ResolveFlockId(boid);
                BehavioralMetricsCollector.Instance?.LogEvent(
                    "LeaderPerceives", gameObject.name, diagFlockId,
                    $"Range={playerDetectionRange:F1}");
            }
            wasPlayerNearby = playerNearby;

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
            float fleeThreshold = boid.settings != null ? boid.settings.fleeHealthThreshold : 0.3f;

            bool isIsolated = boid.manager != null &&
                Vector3.Distance(transform.position, boid.manager.GetFlockCenter()) > isolationThreshold;
            int flockCount = boid.manager != null ? boid.manager.BoidCount : 0;

            var goal = GoalPriorityResolver.ResolveLeaderGoal(
                healthPercent, playerNearby, cooldownReady, isRanged,
                playerDist, isIsolated, flockCount,
                criticalThreshold, kiteMin, guardInner, guardOuter, fleeThreshold);

            currentGoalType = goal;

            if (goal != previousGoalType)
            {
                int flockId = ResolveFlockId(boid);
                BehavioralMetricsCollector.Instance?.LogEvent(
                    "GoalChange", gameObject.name, flockId, $"{previousGoalType}→{goal}");
                previousGoalType = goal;

                // Diagnostic hand-off: stamp the manager so the next LateUpdate's
                // cohesion redirect can fire FollowerReact once. This gives the
                // (d)→(e) order-propagation budget without coupling FlockManager
                // to LeaderGoapBrain's internals.
                if (DiagnosticLogging && boid.manager != null)
                    boid.manager.pendingFollowerReactTime = Time.time;
            }

            switch (goal)
            {
                case GoalPriorityResolver.GoalType.Scatter:
                    provider.RequestGoal<LeaderScatterGoal>(); break;
                case GoalPriorityResolver.GoalType.Flee:
                    provider.RequestGoal<LeaderFleeGoal>(); break;
                case GoalPriorityResolver.GoalType.Attack:
                    provider.RequestGoal<LeaderAttackGoal>(); break;
                case GoalPriorityResolver.GoalType.RangedAttack:
                    // Added 2026-05-05: GoalType.RangedAttack was previously falling
                    // through to default → LeaderWanderGoal, leaving the ranged
                    // leader stuck in Wander whenever the resolver wanted ranged
                    // engagement. Surfaced by the four-bucket diagnostic batch
                    // (zero non-Wander LeaderActionStart events on fid=1 across
                    // a 60 s trial despite 7 Wander→RangedAttack flips).
                    provider.RequestGoal<LeaderRangedAttackGoal>(); break;
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

            // Leash: if the leader has outrun the flock, slow down so followers can catch up
            // rather than the leader abandoning the group. Applied unconditionally — previously
            // gated on isMovementOverridden, which left Wander/Idle phases leashless.
            if (boid.manager != null)
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
        finally
        {
            Profiler.EndSample();
        }
    }
}
