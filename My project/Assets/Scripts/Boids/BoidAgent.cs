using UnityEngine;

public class BoidAgent : MonoBehaviour, IEnemy
{
    [HideInInspector] public BoidSettings settings;
    [HideInInspector] public FlockManager manager;

    private enum AttackState { Flocking, WindUp, Charging, Circling, Firing, Cooldown }

    private Vector3 velocity;
    private Transform cachedTransform;
    private AttackState attackState = AttackState.Flocking;
    private float attackStateTimer;
    private float cooldownTimer;
    private Vector3 chargeDirection;
    private bool damageDealtThisCharge;
    private float circleAngle;

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

        // Suppress flocking and seek forces while executing an attack
        if (!IsAttacking)
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
                UpdateRangedAttackState(ref acceleration);
        }

        // Obstacle avoidance
        acceleration += ComputeObstacleAvoidance();

        // Apply acceleration to velocity (skip during attack — velocity set directly)
        if (!IsAttacking)
        {
            velocity += acceleration * Time.deltaTime;

            // Clamp speed between min and max
            float speed = velocity.magnitude;
            if (speed > 0f)
            {
                speed = Mathf.Clamp(speed, settings.minSpeed, settings.maxSpeed);
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

    public bool IsAttacking => attackState == AttackState.WindUp
        || attackState == AttackState.Charging
        || attackState == AttackState.Circling
        || attackState == AttackState.Firing;

    private void UpdateMeleeAttackState(ref Vector3 acceleration)
    {
        switch (attackState)
        {
            case AttackState.Flocking:
            {
                if (cooldownTimer > 0f)
                {
                    cooldownTimer -= Time.deltaTime;
                    break;
                }

                float dist = (manager.Target.position - cachedTransform.position).magnitude;
                if (dist <= settings.attackTriggerDistance && manager.RequestAttack())
                {
                    attackState = AttackState.WindUp;
                    attackStateTimer = settings.attackWindUpDuration;
                }
                break;
            }

            case AttackState.WindUp:
            {
                // Fly directly away from the player
                Vector3 awayDir = (cachedTransform.position - manager.Target.position).normalized;
                velocity = awayDir * (settings.attackWindUpDistance / settings.attackWindUpDuration);

                attackStateTimer -= Time.deltaTime;
                if (attackStateTimer <= 0f)
                {
                    // Lock in charge direction toward player at moment of release
                    chargeDirection = (manager.Target.position - cachedTransform.position).normalized;
                    damageDealtThisCharge = false;
                    attackState = AttackState.Charging;
                    attackStateTimer = settings.attackChargeDuration;
                }
                break;
            }

            case AttackState.Charging:
            {
                // Dash through the player
                velocity = chargeDirection * settings.attackChargeSpeed;

                // Deal damage once when close enough
                if (!damageDealtThisCharge)
                {
                    float dist = (manager.Target.position - cachedTransform.position).magnitude;
                    if (dist <= settings.attackContactDistance)
                    {
                        manager.Target.GetComponent<PlayerHealth>()?.TakeDamage(settings.attackDamage);
                        damageDealtThisCharge = true;
                    }
                }

                attackStateTimer -= Time.deltaTime;
                if (attackStateTimer <= 0f)
                {
                    manager.ReleaseAttack();
                    cooldownTimer = settings.attackCooldown;
                    attackState = AttackState.Flocking;
                }
                break;
            }
        }
    }

    private void UpdateRangedAttackState(ref Vector3 acceleration)
    {
        switch (attackState)
        {
            case AttackState.Flocking:
            {
                if (cooldownTimer > 0f)
                {
                    cooldownTimer -= Time.deltaTime;
                    break;
                }

                float dist = (manager.Target.position - cachedTransform.position).magnitude;
                if (dist <= settings.attackTriggerDistance && manager.RequestAttack())
                {
                    // Start circle angle from current position relative to player so orbit begins smoothly
                    Vector3 toSelf = cachedTransform.position - manager.Target.position;
                    circleAngle = Mathf.Atan2(toSelf.z, toSelf.x) * Mathf.Rad2Deg;
                    attackState = AttackState.Circling;
                    attackStateTimer = settings.circleDuration;
                }
                break;
            }

            case AttackState.Circling:
            {
                // Orbit the player in the horizontal plane at a slight upward offset
                circleAngle += settings.circleSpeed * Time.deltaTime;

                float rad = circleAngle * Mathf.Deg2Rad;
                Vector3 orbitOffset = new Vector3(
                    Mathf.Cos(rad),
                    0.5f,
                    Mathf.Sin(rad)
                ) * settings.circleRadius;

                Vector3 orbitTarget = manager.Target.position + orbitOffset;
                Vector3 toOrbit = orbitTarget - cachedTransform.position;

                // Move quickly to keep up with the orbit position
                velocity = toOrbit.normalized * settings.maxSpeed * 2.5f;

                attackStateTimer -= Time.deltaTime;
                if (attackStateTimer <= 0f)
                {
                    attackState = AttackState.Firing;
                }
                break;
            }

            case AttackState.Firing:
            {
                // Fire a projectile aimed at the player's current position
                if (settings.projectilePrefab != null)
                {
                    Vector3 dir = (manager.Target.position - cachedTransform.position).normalized;
                    GameObject proj = Instantiate(
                        settings.projectilePrefab,
                        cachedTransform.position,
                        Quaternion.LookRotation(dir)
                    );
                    BoidProjectile bp = proj.GetComponent<BoidProjectile>();
                    bp?.Initialize(dir, settings.projectileSpeed, settings.attackDamage);
                }

                manager.ReleaseAttack();
                cooldownTimer = settings.attackCooldown;
                attackState = AttackState.Flocking;
                break;
            }
        }
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
