using UnityEngine;

/// <summary>
/// Automated player controller for experiment testing.
/// Moves the player around the room in patterns that trigger
/// the full range of agent behaviors (Guard, Attack, Flee, Scatter, etc.).
///
/// Attach to the Player GameObject alongside PlayerHealth.
/// ExperimentRunner enables/disables this component per experiment.
/// </summary>
public class AutomatedPlayer : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float directionChangeInterval = 3f;

    [Header("Behavior Weights (0-1)")]
    [Tooltip("Chance each cycle to approach nearest flock instead of wandering.")]
    [SerializeField] private float approachChance = 0.35f;

    [Tooltip("Chance each cycle to stand still (lets agents close in).")]
    [SerializeField] private float pauseChance = 0.15f;

    [Tooltip("Chance to retreat when health drops below retreatHealthThreshold.")]
    [SerializeField] private float retreatChance = 0.7f;

    [Header("Thresholds")]
    [SerializeField] private float retreatHealthThreshold = 0.4f;
    [SerializeField] private float retreatDuration = 4f;

    private enum PlayerPhase { Wander, Approach, Pause, Retreat }
    private PlayerPhase currentPhase = PlayerPhase.Wander;

    private Vector3 moveDirection;
    private float phaseTimer;
    private PlayerHealth playerHealth;
    private RoomBounds roomBounds;

    private void OnEnable()
    {
        playerHealth = GetComponent<PlayerHealth>();
        roomBounds = RoomBounds.Instance;
        PickNewPhase();
    }

    private void Update()
    {
        phaseTimer -= Time.deltaTime;

        // Check if we should retreat due to low health
        if (currentPhase != PlayerPhase.Retreat
            && playerHealth != null
            && playerHealth.HealthPercent < retreatHealthThreshold
            && Random.value < retreatChance * Time.deltaTime)
        {
            currentPhase = PlayerPhase.Retreat;
            phaseTimer = retreatDuration;
            moveDirection = GetRetreatDirection();
        }

        // Phase expired — pick new one
        if (phaseTimer <= 0f)
            PickNewPhase();

        // Execute current phase
        switch (currentPhase)
        {
            case PlayerPhase.Wander:
                MoveInDirection(moveDirection);
                break;

            case PlayerPhase.Approach:
                Vector3 targetPos = GetNearestFlockCenter();
                if (targetPos != Vector3.zero)
                {
                    moveDirection = (targetPos - transform.position).normalized;
                    moveDirection.y = 0f;
                }
                MoveInDirection(moveDirection);
                break;

            case PlayerPhase.Pause:
                // Stand still
                break;

            case PlayerPhase.Retreat:
                MoveInDirection(moveDirection);
                break;
        }

        // Boundary containment
        ClampToBounds();
    }

    private void PickNewPhase()
    {
        float roll = Random.value;

        if (roll < pauseChance)
        {
            currentPhase = PlayerPhase.Pause;
            phaseTimer = Random.Range(1.5f, 3f);
        }
        else if (roll < pauseChance + approachChance)
        {
            currentPhase = PlayerPhase.Approach;
            phaseTimer = Random.Range(3f, 6f);
        }
        else
        {
            currentPhase = PlayerPhase.Wander;
            phaseTimer = directionChangeInterval + Random.Range(-1f, 1f);
            moveDirection = RandomHorizontalDirection();
        }
    }

    private void MoveInDirection(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.001f) return;
        transform.position += dir.normalized * moveSpeed * Time.deltaTime;
    }

    private void ClampToBounds()
    {
        if (roomBounds == null) return;

        Vector3 pos = transform.position;
        Vector3 clamped = roomBounds.ClampToRoom(pos);

        if (Vector3.Distance(pos, clamped) > 0.1f)
        {
            // Hit boundary — bounce inward
            transform.position = clamped;
            moveDirection = (roomBounds.transform.position - clamped).normalized;
            moveDirection.y = 0f;
        }
    }

    private Vector3 GetNearestFlockCenter()
    {
        // Check GOAPBoidAgent flocks first
        if (GOAPBoidAgent.AllAgents.Count > 0)
        {
            Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(0);
            if (centroid != Vector3.zero)
                return centroid;
        }

        // Check FlockManager-based flocks
        var managers = FindObjectsByType<FlockManager>(FindObjectsSortMode.None);
        float bestDist = float.MaxValue;
        Vector3 bestCenter = Vector3.zero;

        foreach (var mgr in managers)
        {
            if (mgr.BoidCount == 0) continue;
            Vector3 center = mgr.GetFlockCenter();
            float dist = Vector3.Distance(transform.position, center);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestCenter = center;
            }
        }

        return bestCenter;
    }

    private Vector3 GetRetreatDirection()
    {
        Vector3 nearestFlock = GetNearestFlockCenter();
        if (nearestFlock != Vector3.zero)
        {
            Vector3 away = (transform.position - nearestFlock).normalized;
            away.y = 0f;
            return away;
        }
        return RandomHorizontalDirection();
    }

    private static Vector3 RandomHorizontalDirection()
    {
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }
}
