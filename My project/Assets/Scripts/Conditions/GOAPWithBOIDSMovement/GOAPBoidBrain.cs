using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Goal selection brain for GOAPWithBOIDSMovement agents.
/// Same priority logic as PureGOAP: Flee > Attack > Wander.
/// </summary>
[RequireComponent(typeof(GOAPBoidAgent))]
[RequireComponent(typeof(GoapActionProvider))]
public class GOAPBoidBrain : MonoBehaviour
{
    [SerializeField] private float playerDetectionRange = 30f;
    [SerializeField] private string playerTag = "Player";

    private GOAPBoidAgent agent;
    private GoapActionProvider provider;
    private Transform playerTransform;

    private void Awake()
    {
        agent = GetComponent<GOAPBoidAgent>();
        provider = GetComponent<GoapActionProvider>();
    }

    private void Update()
    {
        if (provider == null || provider.AgentType == null)
            return;

        if (playerTransform == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
                playerTransform = player.transform;
        }

        bool playerNearby = false;
        if (playerTransform != null)
        {
            float dist = Vector3.Distance(transform.position, playerTransform.position);
            playerNearby = dist <= playerDetectionRange;
        }

        // Goal priority: Scatter > Flee > Attack/Kite > Regroup > Flank > Guard > Wander
        float healthPercent = agent.HealthPercent;
        bool cooldownReady = agent.cooldownTimer <= 0f && GOAPBoidAgent.SwarmAttackCooldown <= 0f;
        bool isRanged = agent.AgentAttackType == AttackType.Ranged;
        float playerDist = playerTransform != null
            ? Vector3.Distance(transform.position, playerTransform.position)
            : float.MaxValue;

        // Check isolation from swarm centroid
        bool isIsolated = false;
        if (GOAPBoidAgent.AllAgents.Count > 1)
        {
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < GOAPBoidAgent.AllAgents.Count; i++)
                centroid += GOAPBoidAgent.AllAgents[i].Position;
            centroid /= GOAPBoidAgent.AllAgents.Count;
            isIsolated = Vector3.Distance(transform.position, centroid) > 20f;
        }

        if (playerNearby && healthPercent < 0.15f)
        {
            provider.RequestGoal<ScatterGoal>();
        }
        else if (playerNearby && healthPercent < 0.3f)
        {
            provider.RequestGoal<PureFleeGoal>();
        }
        else if (playerNearby && cooldownReady)
        {
            // Ranged agents kite when player is too close
            if (isRanged && playerDist < 8f)
                provider.RequestGoal<KiteGoal>();
            else if (isRanged)
                provider.RequestGoal<PureRangedAttackGoal>();
            else
                provider.RequestGoal<PureAttackGoal>();
        }
        else if (isIsolated)
        {
            provider.RequestGoal<RegroupGoal>();
        }
        else if (playerNearby && GOAPBoidAgent.AllAgents.Count >= 3 && !cooldownReady)
        {
            provider.RequestGoal<FlankGoal>();
        }
        else if (playerDist >= 15f && playerDist <= 30f)
        {
            provider.RequestGoal<GuardGoal>();
        }
        else
        {
            provider.RequestGoal<PureWanderGoal>();
        }
    }
}
