using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// 3-phase melee attack for GOAPWithBOIDSMovement: Approach → WindUp → Charge.
/// Same behavior as PureAttackAction but references GOAPBoidAgent.
/// </summary>
public class GOAPBoidAttackAction : GoapActionBase<GOAPBoidAttackAction.Data>
{
    private const float TriggerDistance = 6f;
    private const float WindUpDuration = 0.4f;
    private const float ChargeDuration = 0.6f;
    private const float ChargeSpeedMultiplier = 3f;
    private const float DamageContactDistance = 2f;

    public enum Phase { Approach, WindUp, Charge }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public Phase CurrentPhase { get; set; }
        public float PhaseTimer { get; set; }
        public Vector3 ChargeDirection { get; set; }
        public bool DamageDealt { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.CurrentPhase = Phase.Approach;
        data.PhaseTimer = 0f;
        data.DamageDealt = false;

        if (data.Target is TransformTarget transformTarget)
            data.Agent.targetPlayer = transformTarget.Transform;

        GOAPBoidAgent.SwarmAttackCooldown = data.Agent.SwarmCooldownDuration;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null || data.Agent.targetPlayer == null)
            return ActionRunState.Stop;

        Vector3 agentPos = data.Agent.Position;
        Vector3 targetPos = data.Target.Position;
        float distance = Vector3.Distance(agentPos, targetPos);

        switch (data.CurrentPhase)
        {
            case Phase.Approach:
                data.Agent.SteerToward(targetPos);
                if (distance <= TriggerDistance)
                {
                    data.CurrentPhase = Phase.WindUp;
                    data.PhaseTimer = WindUpDuration;
                    data.Agent.SetVelocity(data.Agent.velocity * 0.2f);
                }
                break;

            case Phase.WindUp:
                data.Agent.SetVelocity(Vector3.zero);
                data.PhaseTimer -= context.DeltaTime;
                if (data.PhaseTimer <= 0f)
                {
                    data.ChargeDirection = (targetPos - agentPos).normalized;
                    data.CurrentPhase = Phase.Charge;
                    data.PhaseTimer = ChargeDuration;
                }
                break;

            case Phase.Charge:
                float chargeSpeed = data.Agent.MaxSpeed * ChargeSpeedMultiplier;
                data.Agent.SetVelocity(data.ChargeDirection * chargeSpeed);
                data.PhaseTimer -= context.DeltaTime;

                if (!data.DamageDealt && distance <= DamageContactDistance)
                {
                    var playerHealth = data.Agent.targetPlayer.GetComponent<PlayerHealth>();
                    if (playerHealth != null)
                    {
                        playerHealth.TakeDamage(data.Agent.AttackDamage);
                        BehavioralMetricsCollector.Instance?.LogEvent(
                            "AttackHit", data.Agent.gameObject.name, data.Agent.flockId,
                            $"Melee,Dmg={data.Agent.AttackDamage}");
                    }
                    data.DamageDealt = true;
                }

                if (data.PhaseTimer <= 0f)
                    return ActionRunState.Completed;
                break;
        }

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.Agent.cooldownTimer = data.Agent.AttackCooldown;
        GOAPBoidAgent.SwarmAttackCooldown = data.Agent.SwarmCooldownDuration;
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        data.Agent.targetPlayer = null;
    }
}
