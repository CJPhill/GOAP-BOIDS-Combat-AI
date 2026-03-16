using UnityEngine;

public enum FlockType { Melee, Ranged }

[CreateAssetMenu(fileName = "BoidSettings", menuName = "Boids/Boid Settings")]
public class BoidSettings : ScriptableObject
{
    [Header("Flock Identity")]
    public FlockType flockType;
    public Color flockColor = Color.white;

    [Header("Movement")]
    public float minSpeed = 2f;
    public float maxSpeed = 5f;
    public float maxSteerForce = 3f;

    [Header("Detection")]
    public float perceptionRadius = 2.5f;
    public float avoidanceRadius = 1f;

    [Header("Rule Weights")]
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;
    public float crossFlockSeparationWeight = 2f;

    [Header("Obstacle Avoidance")]
    public float obstacleAvoidanceWeight = 10f;
    public float obstacleAvoidanceRadius = 1.5f;
    public LayerMask obstacleMask;

    [Header("Boundary")]
    public float boundaryRadius = 25f;
    public float boundaryTurnStrength = 5f;

    [Header("Spawning")]
    public int flockSize = 40;
    public float spawnRadius = 5f;

    [Header("Target Following")]
    public float targetFollowSpeed = 3f;
    public float targetStopDistance = 2f;
    public float targetSlowDistance = 8f;
    public float aggroRadius = 15f;
    public string aggroTag = "Player";

    [Header("Engagement")]
    public float targetSeekWeight = 0f;
    public float engageBoundaryRadius = -1f;
    public float engageAvoidanceRadius = -1f;
    public float targetKeepDistance = 3f;
    public float groupUpThreshold = 0.7f;

    [Header("Combat")]
    public float maxHealth = 100f;
    public float attackDamage = 10f;
    public float attackCooldown = 1.5f;
    [Tooltip("Fraction of boids kept alive as a last stand until flock health hits 0")]
    public float minSurvivorFraction = 0.35f;

    [Header("Ranged Attack Behaviour")]
    [Tooltip("Orbit radius around the player during the circle wind-up")]
    public float circleRadius = 6f;
    [Tooltip("How long the boid circles before firing (seconds)")]
    public float circleDuration = 1.5f;
    [Tooltip("Orbital speed during circle phase (degrees per second)")]
    public float circleSpeed = 180f;
    [Tooltip("Speed of the fired projectile")]
    public float projectileSpeed = 15f;
    [Tooltip("Prefab with BoidProjectile script, SphereCollider (trigger), and a mesh")]
    public GameObject projectilePrefab;

    [Header("Melee Attack Behaviour")]
    [Tooltip("Distance at which a boid triggers its wind-up")]
    public float attackTriggerDistance = 4f;
    [Tooltip("How long the boid pulls back before charging (seconds)")]
    public float attackWindUpDuration = 0.4f;
    [Tooltip("How far back the boid flies during wind-up")]
    public float attackWindUpDistance = 2f;
    [Tooltip("How long the charge dash lasts (seconds)")]
    public float attackChargeDuration = 0.25f;
    [Tooltip("Speed of the charge dash")]
    public float attackChargeSpeed = 20f;
    [Tooltip("Distance at which the charge deals damage")]
    public float attackContactDistance = 1.5f;
    [Tooltip("Max boids from this flock that can be in WindUp or Charging at once")]
    public int maxSimultaneousAttackers = 3;
}
