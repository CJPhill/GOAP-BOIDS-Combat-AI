using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Target sensor for PureGOAP wander behavior.
/// Generates random positions within room bounds if available,
/// otherwise within a wander radius around the agent.
/// </summary>
public class WanderTargetSensor : LocalTargetSensorBase
{
    private static readonly float WanderRadius = 20f;

    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        Vector3 position;

        if (RoomBounds.Instance != null)
        {
            // Generate target within room bounds
            position = RoomBounds.Instance.GetRandomPointInside();
        }
        else
        {
            // Fallback: offset from agent with wider vertical range
            Vector3 randomOffset = Random.insideUnitSphere * WanderRadius;
            randomOffset.y = Mathf.Clamp(randomOffset.y, -15f, 15f);
            position = agent.Transform.position + randomOffset;
        }

        // Reuse existing target to reduce GC
        if (existingTarget is PositionTarget posTarget)
            return posTarget.SetPosition(position);

        return new PositionTarget(position);
    }
}
