using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Target sensor for GroupUp behavior.
/// Returns the center of mass of all other PureGOAP agents.
/// </summary>
public class NearestClusterSensor : LocalTargetSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        if (PureGOAPAgent.AllAgents.Count <= 1)
        {
            // No other agents — return current position as fallback
            if (existingTarget is PositionTarget posTarget)
                return posTarget.SetPosition(agent.Transform.position);
            return new PositionTarget(agent.Transform.position);
        }

        // Compute center of mass of all other agents
        Vector3 center = Vector3.zero;
        int count = 0;

        for (int i = 0; i < PureGOAPAgent.AllAgents.Count; i++)
        {
            if (PureGOAPAgent.AllAgents[i].cachedTransform == agent.Transform)
                continue;

            center += PureGOAPAgent.AllAgents[i].Position;
            count++;
        }

        if (count > 0)
            center /= count;
        else
            center = agent.Transform.position;

        // Reuse existing target to reduce GC
        if (existingTarget is PositionTarget existing)
            return existing.SetPosition(center);

        return new PositionTarget(center);
    }
}
