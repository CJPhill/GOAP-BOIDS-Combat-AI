using UnityEngine;

/// <summary>
/// Placeholder for BOIDS with GOAP Leader condition.
/// One agent per flock is designated as leader and uses GOAP for high-level decisions.
/// Followers use BOIDS steering with this leader as their cohesion target.
///
/// TODO: Implement leader GOAP brain and follower coordination.
/// </summary>
public class FlockLeader : MonoBehaviour
{
    [Header("Leader Status")]
    [Tooltip("True if this agent is the designated leader for its flock.")]
    public bool isLeader = true;

    private void Awake()
    {
        // TODO: Initialize GOAP components for leader
        // TODO: Broadcast leader position to followers
    }

    private void Update()
    {
        // TODO: GOAP goal selection for leader
        // TODO: Publish leader state to followers
    }

    /// <summary>
    /// Returns the leader's current position for followers to use as cohesion target.
    /// </summary>
    public Vector3 GetLeaderPosition()
    {
        return transform.position;
    }

    /// <summary>
    /// Returns the leader's current velocity for followers to match.
    /// </summary>
    public Vector3 GetLeaderVelocity()
    {
        // TODO: Implement velocity tracking
        return Vector3.zero;
    }
}
