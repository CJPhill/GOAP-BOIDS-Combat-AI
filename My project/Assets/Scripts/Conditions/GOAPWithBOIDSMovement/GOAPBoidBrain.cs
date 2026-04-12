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

        if (playerNearby && agent.HealthPercent < 0.3f)
        {
            provider.RequestGoal<PureFleeGoal>();
        }
        else if (playerNearby && agent.cooldownTimer <= 0f && GOAPBoidAgent.SwarmAttackCooldown <= 0f)
        {
            if (agent.AgentAttackType == AttackType.Ranged)
                provider.RequestGoal<PureRangedAttackGoal>();
            else
                provider.RequestGoal<PureAttackGoal>();
        }
        else
        {
            provider.RequestGoal<PureWanderGoal>();
        }
    }
}
