using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// GroupUp capability for PureGOAP agents.
/// Medium priority (cost 5) — activates when agent is isolated from allies.
/// </summary>
public class PureGroupUpCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("PureGroupUpCapability");

        // Goal: Regroup with allies
        builder.AddGoal<PureGroupUpGoal>()
            .SetBaseCost(5)
            .AddCondition<GroupUpDone>(Comparison.GreaterThanOrEqual, 1);

        // Action: Steer toward nearest cluster
        builder.AddAction<PureGroupUpAction>()
            .SetBaseCost(1)
            .SetTarget<NearestClusterTarget>()
            .AddCondition<IsIsolated>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<GroupUpDone>(EffectType.Increase);

        // Sensor: Detect isolation
        builder.AddWorldSensor<IsolationSensor>()
            .SetKey<IsIsolated>();

        // Sensor: Find nearest cluster position
        builder.AddTargetSensor<NearestClusterSensor>()
            .SetTarget<NearestClusterTarget>();

        return builder.Build();
    }
}
