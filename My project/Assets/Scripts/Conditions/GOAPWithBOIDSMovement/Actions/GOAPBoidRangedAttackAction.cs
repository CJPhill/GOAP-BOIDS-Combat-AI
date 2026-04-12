using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Ranged attack for GOAPWithBOIDSMovement: Approach → Circle → Fire projectile.
/// Same as PureRangedAttackAction but references GOAPBoidAgent.
/// </summary>
public class GOAPBoidRangedAttackAction : GoapActionBase<GOAPBoidRangedAttackAction.Data>
{
    private const float FiringRange = 12f;
    private const float OrbitRadius = 10f;
    private const float CircleDuration = 1.5f;
    private const float OrbitSpeed = 90f;
    private const float ProjectileSpeed = 15f;
    private const float ProjectileDamage = 30f;
    private const float ProjectileTurnSpeed = 5f;

    public enum Phase { Approach, Circle, Fire }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public GOAPBoidAgent Agent { get; set; }
        public Phase CurrentPhase { get; set; }
        public float PhaseTimer { get; set; }
        public float CircleAngle { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.CurrentPhase = Phase.Approach;
        data.PhaseTimer = 0f;
        data.CircleAngle = 0f;

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
                if (distance <= FiringRange)
                {
                    data.CurrentPhase = Phase.Circle;
                    data.PhaseTimer = CircleDuration;
                    Vector3 offset = agentPos - targetPos;
                    data.CircleAngle = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;
                }
                break;

            case Phase.Circle:
                data.CircleAngle += OrbitSpeed * context.DeltaTime;
                float rad = data.CircleAngle * Mathf.Deg2Rad;
                Vector3 orbitPos = targetPos + new Vector3(
                    Mathf.Cos(rad) * OrbitRadius,
                    0f,
                    Mathf.Sin(rad) * OrbitRadius
                );
                orbitPos.y = agentPos.y;
                data.Agent.SteerToward(orbitPos);

                data.PhaseTimer -= context.DeltaTime;
                if (data.PhaseTimer <= 0f)
                    data.CurrentPhase = Phase.Fire;
                break;

            case Phase.Fire:
                if (data.Agent.ProjectilePrefab != null && data.Agent.targetPlayer != null)
                {
                    Vector3 dir = (targetPos - agentPos).normalized;
                    GameObject proj = Object.Instantiate(
                        data.Agent.ProjectilePrefab,
                        agentPos,
                        Quaternion.LookRotation(dir)
                    );
                    BoidProjectile bp = proj.GetComponent<BoidProjectile>();
                    bp?.Initialize(dir, ProjectileSpeed, ProjectileDamage, data.Agent.targetPlayer, ProjectileTurnSpeed);
                }
                return ActionRunState.Completed;
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
