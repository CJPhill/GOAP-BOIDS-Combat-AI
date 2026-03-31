using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Pure GOAP attack action: moves toward player and deals damage when in range.
/// No BOIDS dependencies - uses direct steering.
///
/// Condition: PlayerVisible >= 1
/// Effect: PureAttackDone Increase
/// Target: PlayerTarget
/// </summary>
public class PureAttackAction : GoapActionBase<PureAttackAction.Data>
{
    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public PureGOAPAgent Agent { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        // Cache player reference
        if (data.Target is TransformTarget transformTarget)
        {
            data.Agent.targetPlayer = transformTarget.Transform;
        }
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null || data.Agent.targetPlayer == null)
            return ActionRunState.Stop;

        Vector3 targetPosition = data.Target.Position;
        float distance = Vector3.Distance(data.Agent.Position, targetPosition);

        // Steer toward player
        data.Agent.SteerToward(targetPosition);

        // If in attack range, deal damage
        if (distance <= data.Agent.AttackRange)
        {
            var playerHealth = data.Agent.targetPlayer.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(data.Agent.AttackDamage);
            }

            return ActionRunState.Completed;
        }

        // Continue approaching
        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.Agent.cooldownTimer = data.Agent.AttackCooldown;
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        data.Agent.targetPlayer = null;
    }
}
