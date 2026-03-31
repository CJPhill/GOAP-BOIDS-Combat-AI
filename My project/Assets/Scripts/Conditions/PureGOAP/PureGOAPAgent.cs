using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Pure GOAP agent for Condition 1: Individual GOAP planning with direct steering.
/// No flocking behaviors - agents act independently using only GOAP for decision-making.
///
/// Movement: Direct steering toward GOAP action targets with 3D swimming/bobbing
/// Planning: Individual GOAP brain per agent
/// Obstacle Avoidance: SphereCast-based (same as BOIDS system)
/// Swarm: Soft cohesion + separation forces toward/from nearby allies
/// Bounds: Stays within RoomBounds if present in scene
/// Target: Player (detected via VisionSensor)
///
/// Components required:
/// - This script
/// - GoapActionProvider (CrashKonijn)
/// - AgentBehaviour (CrashKonijn)
/// - PureGOAPBrain (goal selection)
/// </summary>
public class PureGOAPAgent : MonoBehaviour, IEnemy
{
    // Static registry of all living agents (for swarm forces and sensors)
    public static readonly List<PureGOAPAgent> AllAgents = new List<PureGOAPAgent>();

    [Header("Movement Settings")]
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float minSpeed = 2f;
    [SerializeField] private float maxSteerForce = 5f;
    [SerializeField] private float rotationSpeed = 5f;

    [Header("Obstacle Avoidance")]
    [SerializeField] private float obstacleAvoidanceRadius = 1.5f;
    [SerializeField] private float obstacleAvoidanceWeight = 10f;
    [SerializeField] private float perceptionRadius = 10f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Combat")]
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float attackDamage = 10f;
    [SerializeField] private float attackRange = 5f;
    [SerializeField] private float attackCooldown = 2f;

    [Header("Swarm Cohesion")]
    [SerializeField] private float cohesionRadius = 15f;
    [SerializeField] private float cohesionWeight = 1.5f;
    [SerializeField] private float separationRadius = 3f;
    [SerializeField] private float separationWeight = 3f;

    [Header("Room Bounds")]
    [SerializeField] private float boundaryMargin = 10f;

    [Header("Swimming/Bobbing")]
    [SerializeField] private float bobAmplitude = 0.3f;
    [SerializeField] private float bobFrequency = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool drawDebug = false;

    // Public state (accessed by GOAP actions)
    [HideInInspector] public Vector3 velocity;
    [HideInInspector] public Transform cachedTransform;
    [HideInInspector] public float cooldownTimer;
    [HideInInspector] public Transform targetPlayer;

    private float currentHealth;
    private bool isDead;
    private RoomBounds roomBounds;
    private float bobPhaseOffset;

    // IEnemy interface
    public bool IsDead => isDead;
    public float HealthPercent => currentHealth / maxHealth;
    public Vector3 Position => cachedTransform.position;
    public Vector3 Velocity => velocity;

    public float MaxSpeed => maxSpeed;
    public float AttackRange => attackRange;
    public float AttackDamage => attackDamage;
    public float AttackCooldown => attackCooldown;

    private void Awake()
    {
        cachedTransform = transform;
        currentHealth = maxHealth;
        velocity = cachedTransform.forward * maxSpeed * 0.5f;
        roomBounds = RoomBounds.Instance;
        bobPhaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void OnEnable()
    {
        AllAgents.Add(this);
    }

    private void OnDisable()
    {
        AllAgents.Remove(this);
    }

    private void Update()
    {
        Profiler.BeginSample("PureGOAPAgent.Update");

        // Tick cooldown
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        // GOAP actions set velocity via SteerToward/SetVelocity.
        // Here we layer on environmental forces.

        // Obstacle avoidance
        Vector3 obstacleAvoidance = ComputeObstacleAvoidance();
        velocity += obstacleAvoidance * Time.deltaTime;

        // Swarm cohesion + separation
        Vector3 swarmForce = ComputeSwarmForces();
        velocity += swarmForce * Time.deltaTime;

        // Room boundary steering
        if (roomBounds != null)
        {
            Vector3 boundaryForce = roomBounds.GetBoundarySteeringForce(cachedTransform.position, boundaryMargin);
            velocity += boundaryForce * Time.deltaTime;
        }

        // Clamp velocity
        float speed = velocity.magnitude;
        if (speed > 0f)
        {
            speed = Mathf.Clamp(speed, minSpeed, maxSpeed);
            velocity = velocity.normalized * speed;
        }

        // Move
        cachedTransform.position += velocity * Time.deltaTime;

        // Swimming bob (sine-wave vertical oscillation)
        float bob = Mathf.Sin((Time.time + bobPhaseOffset) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        cachedTransform.position += Vector3.up * bob * Time.deltaTime;

        // Orient toward velocity
        if (velocity.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(velocity.normalized);
            cachedTransform.rotation = Quaternion.Slerp(
                cachedTransform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        Profiler.EndSample();
    }

    /// <summary>
    /// Steer toward a target using direct steering (no flocking).
    /// Called by GOAP actions to set movement direction.
    /// </summary>
    public void SteerToward(Vector3 targetPosition)
    {
        Vector3 desiredDirection = (targetPosition - cachedTransform.position).normalized;
        Vector3 desiredVelocity = desiredDirection * maxSpeed;
        Vector3 steering = Vector3.ClampMagnitude(desiredVelocity - velocity, maxSteerForce);
        velocity += steering * Time.deltaTime;
    }

    /// <summary>
    /// Set velocity directly (used by GOAP actions for precise movement control).
    /// </summary>
    public void SetVelocity(Vector3 newVelocity)
    {
        velocity = newVelocity;
    }

    /// <summary>
    /// Returns true if this agent has fewer than minNeighbors within the given radius.
    /// Used by IsolationSensor and PureGOAPBrain for GroupUp goal.
    /// </summary>
    public bool IsIsolated(float radius, int minNeighbors)
    {
        int count = 0;
        Vector3 pos = cachedTransform.position;
        float radiusSq = radius * radius;

        for (int i = 0; i < AllAgents.Count; i++)
        {
            if (AllAgents[i] == this) continue;
            if ((AllAgents[i].Position - pos).sqrMagnitude <= radiusSq)
            {
                count++;
                if (count >= minNeighbors)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Soft cohesion and separation forces toward/from nearby allies.
    /// Always-on swarm behavior layered on top of GOAP action steering.
    /// </summary>
    private Vector3 ComputeSwarmForces()
    {
        if (AllAgents.Count <= 1)
            return Vector3.zero;

        Profiler.BeginSample("PureGOAPAgent.SwarmForces");

        Vector3 cohesionCenter = Vector3.zero;
        Vector3 separationForce = Vector3.zero;
        int cohesionCount = 0;
        Vector3 myPos = cachedTransform.position;
        float cohesionRadiusSq = cohesionRadius * cohesionRadius;
        float separationRadiusSq = separationRadius * separationRadius;

        for (int i = 0; i < AllAgents.Count; i++)
        {
            if (AllAgents[i] == this) continue;

            Vector3 offset = AllAgents[i].Position - myPos;
            float distSq = offset.sqrMagnitude;

            // Cohesion: drift toward average neighbor position
            if (distSq <= cohesionRadiusSq)
            {
                cohesionCenter += AllAgents[i].Position;
                cohesionCount++;
            }

            // Separation: push away from very close neighbors
            if (distSq <= separationRadiusSq && distSq > 0.001f)
            {
                separationForce -= offset.normalized / Mathf.Sqrt(distSq);
            }
        }

        Vector3 result = Vector3.zero;

        if (cohesionCount > 0)
        {
            cohesionCenter /= cohesionCount;
            result += SteerTowards(cohesionCenter - myPos) * cohesionWeight;
        }

        result += separationForce.normalized * separationWeight;

        Profiler.EndSample();
        return result;
    }

    /// <summary>
    /// Obstacle avoidance using SphereCast with golden ratio direction sampling.
    /// Same system as BOIDS for consistency.
    /// </summary>
    private Vector3 ComputeObstacleAvoidance()
    {
        if (obstacleMask == 0)
            return Vector3.zero;

        Profiler.BeginSample("PureGOAPAgent.ObstacleAvoidance");

        Vector3 forward = cachedTransform.forward;

        // Check if there's an obstacle ahead
        if (!Physics.SphereCast(cachedTransform.position, obstacleAvoidanceRadius, forward,
                out RaycastHit hit, perceptionRadius, obstacleMask))
        {
            Profiler.EndSample();
            return Vector3.zero;
        }

        // Find the first unobstructed direction
        Vector3[] dirs = BoidHelper.Directions;
        for (int i = 0; i < dirs.Length; i++)
        {
            Vector3 worldDir = cachedTransform.TransformDirection(dirs[i]);
            if (!Physics.SphereCast(cachedTransform.position, obstacleAvoidanceRadius, worldDir,
                    out RaycastHit _, perceptionRadius, obstacleMask))
            {
                Vector3 steer = SteerTowards(worldDir) * obstacleAvoidanceWeight;
                Profiler.EndSample();
                return steer;
            }
        }

        // All directions blocked — steer away from the hit
        Vector3 result = SteerTowards(-hit.normal) * obstacleAvoidanceWeight;
        Profiler.EndSample();
        return result;
    }

    private Vector3 SteerTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
            return Vector3.zero;

        Vector3 steer = direction.normalized * maxSpeed - velocity;
        return Vector3.ClampMagnitude(steer, maxSteerForce);
    }

    // IEnemy interface implementation
    public void TakeDamage(float amount)
    {
        if (isDead) return;

        currentHealth = Mathf.Max(currentHealth - amount, 0f);
        if (currentHealth <= 0f)
        {
            isDead = true;
            Destroy(gameObject);
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawDebug || cachedTransform == null) return;

        // Draw velocity vector
        Gizmos.color = Color.green;
        Gizmos.DrawLine(cachedTransform.position, cachedTransform.position + velocity);

        // Draw perception radius
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(cachedTransform.position, perceptionRadius);

        // Draw cohesion radius
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
        Gizmos.DrawWireSphere(cachedTransform.position, cohesionRadius);

        // Draw attack range
        if (targetPlayer != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(cachedTransform.position, targetPlayer.position);
            Gizmos.DrawWireSphere(cachedTransform.position, attackRange);
        }
    }
}
