using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Pure GOAP wander action: moves to random positions for idle behavior.
/// No BOIDS dependencies - uses direct steering.
///
/// Effect: Increases idle state (satisfies WanderGoal)
/// Target: WanderTarget (random position)
/// </summary>
public class PureWanderAction : GoapActionBase<PureWanderAction.Data>
{
    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public PureGOAPAgent Agent { get; set; }

        public float Timer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        // Wander for a random duration
        data.Timer = Random.Range(2f, 5f);
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null)
            return ActionRunState.Stop;

        // Steer toward wander target
        data.Agent.SteerToward(data.Target.Position);

        // Check if we've reached the target or timer expired
        float distance = Vector3.Distance(data.Agent.Position, data.Target.Position);
        if (distance <= 2f || data.Timer <= 0f)
        {
            return ActionRunState.Completed;
        }

        data.Timer -= context.DeltaTime;
        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data) { }
}
