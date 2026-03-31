using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Senses IsIsolated: 1 when agent has fewer than 2 allies within 20m.
/// </summary>
public class IsolationSensor : LocalWorldSensorBase
{
    private static readonly float GroupRadius = 20f;
    private static readonly int MinNeighbors = 2;

    public override ISensorTimer Timer => SensorTimer.Interval(0.2f);

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var pureAgent = references.GetCachedComponent<PureGOAPAgent>();
        if (pureAgent == null)
            return 0;

        return pureAgent.IsIsolated(GroupRadius, MinNeighbors) ? 1 : 0;
    }
}
