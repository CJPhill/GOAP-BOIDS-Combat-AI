using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Pure GOAP group-up action: steers toward the center of nearby allies.
/// Completes when close enough to the group or timer expires.
///
/// Condition: IsIsolated >= 1
/// Effect: GroupUpDone Increase
/// Target: NearestClusterTarget
/// </summary>
public class PureGroupUpAction : GoapActionBase<PureGroupUpAction.Data>
{
    private static readonly float ArrivalDistance = 10f;

    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public PureGOAPAgent Agent { get; set; }

        public float Timer { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.Timer = Random.Range(3f, 6f);
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null)
            return ActionRunState.Stop;

        // Steer toward group center
        data.Agent.SteerToward(data.Target.Position);

        data.Timer -= context.DeltaTime;

        // Complete if close to group or timer expired
        float distance = Vector3.Distance(data.Agent.Position, data.Target.Position);
        if (distance <= ArrivalDistance || data.Timer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data) { }
}
