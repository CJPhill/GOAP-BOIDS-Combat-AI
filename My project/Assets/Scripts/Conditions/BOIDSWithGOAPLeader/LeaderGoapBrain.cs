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

    private BoidAgent boid;
    private BoidGoapBrain brain;
    private GoapActionProvider provider;
    private Transform playerTransform;

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

        if (playerNearby && healthPercent < criticalThreshold)
        {
            provider.RequestGoal<LeaderScatterGoal>();
        }
        else if (playerNearby && healthPercent < 0.3f)
        {
            provider.RequestGoal<LeaderFleeGoal>();
        }
        else if (playerNearby && cooldownReady)
        {
            // Ranged leaders kite when player is too close, otherwise normal attack
            if (isRanged && playerDist < kiteMin)
                provider.RequestGoal<LeaderKiteGoal>();
            else
                provider.RequestGoal<LeaderAttackGoal>();
        }
        else if (boid.manager != null && Vector3.Distance(transform.position, boid.manager.GetFlockCenter()) > isolationThreshold)
        {
            provider.RequestGoal<LeaderRegroupGoal>();
        }
        else if (playerNearby && boid.manager.BoidCount >= 3 && !cooldownReady)
        {
            provider.RequestGoal<LeaderFlankGoal>();
        }
        else if (playerDist >= guardInner && playerDist <= guardOuter)
        {
            provider.RequestGoal<LeaderGuardGoal>();
        }
        else
        {
            provider.RequestGoal<LeaderWanderGoal>();
        }
    }
}
