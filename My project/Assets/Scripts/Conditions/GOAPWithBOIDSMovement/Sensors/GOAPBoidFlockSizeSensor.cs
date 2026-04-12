using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Returns 1 when 3 or more GOAPBoid agents are alive (enough for flanking).
/// </summary>
public class GOAPBoidFlockSizeSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        return GOAPBoidAgent.AllAgents.Count >= 3 ? 1 : 0;
    }
}
