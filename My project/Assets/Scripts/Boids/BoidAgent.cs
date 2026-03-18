using UnityEngine;

public class BoidAgent : MonoBehaviour, IEnemy
{
    [HideInInspector] public BoidSettings settings;
    [HideInInspector] public FlockManager manager;

    private enum AttackState { Flocking, WindUp, Charging, Formation }

    private Vector3 velocity;
    private Transform cachedTransform;
    private AttackState attackState = AttackState.Flocking;
    private float attackStateTimer;
    private float cooldownTimer;
    private Vector3 chargeDirection;
    private bool damageDealtThisCharge;
    private bool inFormation;
    private Vector3 formationTargetPos;

    // IEnemy proxies to the flock — combat teammate calls these without knowing about flocks
    public bool IsDead => manager.IsDead;
    public float HealthPercent => manager.HealthPercent;

    public void TakeDamage(float amount)
    {
        manager.TakeDamage(amount);
    }

    public Vector3 Position => cachedTransform.position;
    public Vector3 Velocity => velocity;
    public FlockType FlockType => settings.flockType;

    private void Awake()
    {
        cachedTransform = transform;
    }

    public void Initialize(Vector3 startVelocity)
    {
        velocity = startVelocity;
    }

    public void UpdateBoid(Vector3 separationHeading, Vector3 alignmentHeading, Vector3 cohesionCenter, int neighborCount, Vector3 crossFlockSeparation)
    {
        Vector3 acceleration = Vector3.zero;

        // Suppress flocking and seek forces only during full movement override (ranged formation)
        if (!IsMovementOverridden)
        {
            // Apply the three core flocking rules
            if (neighborCount > 0)
            {
                acceleration += SteerTowards(separationHeading) * settings.separationWeight;
                acceleration += SteerTowards(alignmentHeading) * settings.alignmentWeight;
                acceleration += SteerTowards(cohesionCenter) * settings.cohesionWeight;
            }

            // Cross-flock avoidance (independent of same-flock neighbors)
            acceleration += SteerTowards(crossFlockSeparation) * settings.crossFlockSeparationWeight;

            // Boundary steering
            acceleration += ComputeBoundarySteer();

            // Target-seek steering (per-boid, only when Engaging)
            if (manager.Target != null && manager.State == FlockManager.FlockState.Engaging && settings.targetSeekWeight > 0f)
            {
                Vector3 targetOffset = manager.Target.position - cachedTransform.position;
                float targetDist = targetOffset.magnitude;

                if (targetDist > settings.targetKeepDistance)
                    acceleration += SteerTowards(targetOffset) * settings.targetSeekWeight;
                else
                    acceleration += SteerTowards(-targetOffset) * settings.targetSeekWeight;
            }
        }

        // Attack state machine — branched by flock type
        if (manager.Target != null && manager.State == FlockManager.FlockState.Engaging && !manager.IsDead)
        {
            if (settings.flockType == FlockType.Melee)
                UpdateMeleeAttackState(ref acceleration);
            else if (settings.flockType == FlockType.Ranged)
                UpdateRangedFormationState();
        }

        // Obstacle avoidance
        acceleration += ComputeObstacleAvoidance();

        // Apply acceleration to velocity (skip during full movement override)
        if (!IsMovementOverridden)
        {
            velocity += acceleration * Time.deltaTime;

            // Clamp speed — use higher cap during melee charge
            float speed = velocity.magnitude;
            if (speed > 0f)
            {
                float maxSpd = (attackState == AttackState.Charging) ? settings.attackChargeSpeed : settings.maxSpeed;
                speed = Mathf.Clamp(speed, settings.minSpeed, maxSpd);
                velocity = velocity.normalized * speed;
            }
        }

        // Move and orient
        cachedTransform.position += velocity * Time.deltaTime;
        if (velocity.sqrMagnitude > 0.001f)
        {
            cachedTransform.forward = velocity.normalized;
        }
    }

    public bool IsAttacking => attackState != AttackState.Flocking;

    // True only for states that fully override movement (ranged formation)
    private bool IsMovementOverridden => attackState == AttackState.Formation;

    public void SetFormationTarget(Vector3 worldPos, bool active)
    {
        formationTargetPos = worldPos;
        inFormation = active;
        if (active)
            attackState = AttackState.Formation;
        else if (attackState == AttackState.Formation)
            attackState = AttackState.Flocking;
    }

    public void BeginMeleeWindUp()
    {
        attackState = AttackState.WindUp;
    }

    public void BeginMeleeCharge()
    {
        chargeDirection = (manager.Target.position - cachedTransform.position).normalized;
        damageDealtThisCharge = false;
        attackState = AttackState.Charging;
    }

    public void EndMeleeAttack()
    {
        attackState = AttackState.Flocking;
    }

    private void UpdateMeleeAttackState(ref Vector3 acceleration)
    {
        switch (attackState)
        {
            case AttackState.WindUp:
            {
                Vector3 awayDir = (cachedTransform.position - manager.Target.position).normalized;
                acceleration += SteerTowards(awayDir) * settings.meleeWindUpSteerWeight;
                break;
            }
            case AttackState.Charging:
            {
                acceleration += SteerTowards(chargeDirection) * settings.meleeChargeSteerWeight;
                break;
            }
        }
    }

    private void UpdateRangedFormationState()
    {
        if (!inFormation) return;

        // Fly toward assigned formation slot
        Vector3 toSlot = formationTargetPos - cachedTransform.position;
        float dist = toSlot.magnitude;

        if (dist > 0.3f)
        {
            velocity = toSlot.normalized * settings.formationApproachSpeed;
        }
        else
        {
            // Hold position — hover with minimal drift
            velocity = toSlot * 2f;
        }

        // Face toward the player while in formation
        if (manager.Target != null)
        {
            Vector3 toTarget = manager.Target.position - cachedTransform.position;
            if (toTarget.sqrMagnitude > 0.01f)
                cachedTransform.forward = toTarget.normalized;
        }
    }

    public void ApplyKnockback(Vector3 impulse)
    {
        velocity += impulse;
    }

    private Vector3 SteerTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
            return Vector3.zero;

        Vector3 steer = direction.normalized * settings.maxSpeed - velocity;
        return Vector3.ClampMagnitude(steer, settings.maxSteerForce);
    }

    private Vector3 ComputeBoundarySteer()
    {
        Vector3 managerPos = manager.transform.position;
        Vector3 offset = cachedTransform.position - managerPos;
        float distance = offset.magnitude;
        float boundaryRadius = manager.EffectiveBoundaryRadius;

        if (distance < boundaryRadius)
            return Vector3.zero;

        // Steer back toward center, strength proportional to how far past the boundary
        float overshoot = distance - boundaryRadius;
        Vector3 directionToCenter = -offset.normalized;
        float strength = overshoot * settings.boundaryTurnStrength;
        float maxBoundaryForce = settings.obstacleAvoidanceWeight * settings.maxSteerForce;
        strength = Mathf.Min(strength, maxBoundaryForce);
        return directionToCenter * strength;
    }

    private Vector3 ComputeObstacleAvoidance()
    {
        if (settings.obstacleMask == 0)
            return Vector3.zero;

        Vector3 forward = cachedTransform.forward;

        // Check if there's an obstacle ahead
        if (!Physics.SphereCast(cachedTransform.position, settings.obstacleAvoidanceRadius, forward,
                out RaycastHit hit, settings.perceptionRadius, settings.obstacleMask))
        {
            return Vector3.zero;
        }

        // Find the first unobstructed direction
        Vector3[] dirs = BoidHelper.Directions;
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = cachedTransform.TransformDirection(dirs[i]);
            if (!Physics.SphereCast(cachedTransform.position, settings.obstacleAvoidanceRadius, worldDir,
                    out RaycastHit _, settings.perceptionRadius, settings.obstacleMask))
            {
                return SteerTowards(worldDir) * settings.obstacleAvoidanceWeight;
            }
        }

        // All directions blocked — steer away from the hit
        return SteerTowards(-hit.normal) * settings.obstacleAvoidanceWeight;
    }
}
