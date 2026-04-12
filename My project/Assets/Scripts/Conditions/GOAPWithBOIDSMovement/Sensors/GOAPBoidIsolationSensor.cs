using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Senses if this GOAPBoid agent is isolated from the swarm centroid.
/// </summary>
public class GOAPBoidIsolationSensor : LocalWorldSensorBase
{
    private const float IsolationThreshold = 20f;

    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var boidAgent = references.GetCachedComponent<GOAPBoidAgent>();
        if (boidAgent == null || GOAPBoidAgent.AllAgents.Count <= 1) return 0;

        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < GOAPBoidAgent.AllAgents.Count; i++)
            centroid += GOAPBoidAgent.AllAgents[i].Position;
        centroid /= GOAPBoidAgent.AllAgents.Count;

        float dist = Vector3.Distance(boidAgent.Position, centroid);
        return dist > IsolationThreshold ? 1 : 0;
    }
}
