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

        // Goal priority: Flee > Attack > Wander
        if (playerNearby && boid.manager.HealthPercent < 0.3f)
        {
            provider.RequestGoal<LeaderFleeGoal>();
        }
        else if (playerNearby && (brain == null || brain.cooldownTimer <= 0f))
        {
            provider.RequestGoal<LeaderAttackGoal>();
        }
        else
        {
            provider.RequestGoal<LeaderWanderGoal>();
        }
    }
}
