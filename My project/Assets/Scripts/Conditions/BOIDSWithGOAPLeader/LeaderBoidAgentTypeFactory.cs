using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// AgentType factory for the GOAP leader in BOIDSWithGOAPLeader condition.
/// Registers "LeaderBoid" with flee, combat, and wander capabilities.
/// Add this to the GOAP Manager GameObject.
/// </summary>
public class LeaderBoidAgentTypeFactory : AgentTypeFactoryBase
{
    public override IAgentTypeConfig Create()
    {
        var builder = this.CreateBuilder("LeaderBoid");

        builder.AddCapability<LeaderFleeCapabilityFactory>();
        builder.AddCapability<LeaderCombatCapabilityFactory>();
        builder.AddCapability<LeaderWanderCapabilityFactory>();

        return builder.Build();
    }
}
