using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Goal selection brain for PureGOAP agents.
/// Requests GOAP goals based on player visibility and cooldown state.
///
/// Logic (priority order):
/// - If health low AND player visible → Request FleeGoal
/// - If player visible AND not on cooldown → Request AttackGoal
/// - If isolated (few nearby allies) → Request GroupUpGoal
/// - Otherwise → Request WanderGoal (idle behavior)
/// </summary>
[RequireComponent(typeof(PureGOAPAgent))]
[RequireComponent(typeof(GoapActionProvider))]
public class PureGOAPBrain : MonoBehaviour
{
    private PureGOAPAgent agent;
    private GoapActionProvider provider;

    private void Awake()
    {
        agent = GetComponent<PureGOAPAgent>();
        provider = GetComponent<GoapActionProvider>();
    }

    private void Update()
    {
        if (provider == null || provider.AgentType == null)
            return;

        // Goal selection: flee > attack > groupUp > wander
        if (agent.HealthPercent < 0.3f && agent.targetPlayer != null)
        {
            provider.RequestGoal<PureFleeGoal>();
        }
        else if (agent.targetPlayer != null && agent.cooldownTimer <= 0f)
        {
            provider.RequestGoal<PureAttackGoal>();
        }
        else if (agent.IsIsolated(20f, 2))
        {
            provider.RequestGoal<PureGroupUpGoal>();
        }
        else
        {
            provider.RequestGoal<PureWanderGoal>();
        }
    }
}
