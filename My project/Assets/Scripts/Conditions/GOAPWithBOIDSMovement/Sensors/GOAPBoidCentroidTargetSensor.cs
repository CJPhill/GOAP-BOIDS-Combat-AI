using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Provides the swarm centroid as a target position for the regroup action.
/// </summary>
public class GOAPBoidCentroidTargetSensor : LocalTargetSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        if (GOAPBoidAgent.AllAgents.Count == 0)
            return existingTarget;

        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < GOAPBoidAgent.AllAgents.Count; i++)
            centroid += GOAPBoidAgent.AllAgents[i].Position;
        centroid /= GOAPBoidAgent.AllAgents.Count;

        return new PositionTarget(centroid);
    }
}
