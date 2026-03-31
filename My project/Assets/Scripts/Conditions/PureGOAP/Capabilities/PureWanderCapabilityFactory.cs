using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Wander/idle capability for PureGOAP agents.
/// Fallback behavior when no player is visible.
/// </summary>
public class PureWanderCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("PureWanderCapability");

        // Goal: Wander (low priority fallback)
        builder.AddGoal<PureWanderGoal>()
            .SetBaseCost(10)
            .AddCondition<IsWandering>(Comparison.GreaterThanOrEqual, 1);

        // Action: Wander to random positions
        builder.AddAction<PureWanderAction>()
            .SetBaseCost(1)
            .SetTarget<WanderTarget>()
            .AddEffect<IsWandering>(EffectType.Increase);

        // Sensor: Generate wander targets
        builder.AddTargetSensor<WanderTargetSensor>()
            .SetTarget<WanderTarget>();

        return builder.Build();
    }
}
