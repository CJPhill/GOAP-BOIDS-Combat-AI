using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Collects behavioral metrics for thesis comparison across swarming conditions.
/// Tracks goal distribution over time, flock coherence, and event logs.
/// Attach to the same GameObject as ConditionManager.
/// </summary>
public class BehavioralMetricsCollector : MonoBehaviour
{
    public static BehavioralMetricsCollector Instance { get; private set; }

    [Header("Configuration")]
    [SerializeField] private float sampleInterval = 0.5f;
    [SerializeField] private bool isRecording = false;

    private ConditionManager conditionManager;
    private float nextSampleTime;

    // Frame-level goal distribution snapshots
    private List<GoalDistributionSnapshot> snapshots = new List<GoalDistributionSnapshot>();

    // Event log
    private List<BehavioralEvent> eventLog = new List<BehavioralEvent>();

    [System.Serializable]
    public struct GoalDistributionSnapshot
    {
        public float time;
        public string condition;
        public int totalAgents;
        public float flockCoherence;
        public int goalAttack;
        public int goalFlee;
        public int goalScatter;
        public int goalRegroup;
        public int goalFlank;
        public int goalGuard;
        public int goalWander;
        public int goalKite;
        public int goalRangedAttack;
    }

    [System.Serializable]
    public struct BehavioralEvent
    {
        public float time;
        public string condition;
        public string eventType;
        public string agentName;
        public int flockId;
        public string details;
    }

    private void Awake()
    {
        Instance = this;
        conditionManager = GetComponent<ConditionManager>();
    }

    private void Update()
    {
        if (!isRecording) return;

        if (Time.time >= nextSampleTime)
        {
            nextSampleTime = Time.time + sampleInterval;
            RecordSnapshot();
        }
    }

    public void StartRecording()
    {
        isRecording = true;
        nextSampleTime = Time.time;
        snapshots.Clear();
        eventLog.Clear();
        Debug.Log("[BehavioralMetrics] Recording started.");
    }

    public void StopRecording()
    {
        isRecording = false;
        Debug.Log($"[BehavioralMetrics] Recording stopped. {snapshots.Count} snapshots, {eventLog.Count} events.");
    }

    /// <summary>
    /// Log a behavioral event from any script (goal change, damage, attack completion).
    /// </summary>
    public void LogEvent(string eventType, string agentName, int flockId, string details)
    {
        if (!isRecording) return;

        eventLog.Add(new BehavioralEvent
        {
            time = Time.time,
            condition = conditionManager != null ? conditionManager.CurrentCondition.ToString() : "Unknown",
            eventType = eventType,
            agentName = agentName,
            flockId = flockId,
            details = details
        });
    }

    private void RecordSnapshot()
    {
        string condition = conditionManager != null ? conditionManager.CurrentCondition.ToString() : "Unknown";

        var snapshot = new GoalDistributionSnapshot
        {
            time = Time.time,
            condition = condition,
            totalAgents = 0,
            flockCoherence = 0f
        };

        // Count GOAPWithBOIDSMovement agents by goal
        if (conditionManager != null && conditionManager.CurrentCondition == AgentCondition.GOAPWithBOIDSMovement)
        {
            var brains = FindObjectsByType<GOAPBoidBrain>(FindObjectsSortMode.None);
            snapshot.totalAgents = brains.Length;

            foreach (var brain in brains)
            {
                switch (brain.currentGoalType)
                {
                    case GoalPriorityResolver.GoalType.Attack: snapshot.goalAttack++; break;
                    case GoalPriorityResolver.GoalType.RangedAttack: snapshot.goalRangedAttack++; break;
                    case GoalPriorityResolver.GoalType.Flee: snapshot.goalFlee++; break;
                    case GoalPriorityResolver.GoalType.Scatter: snapshot.goalScatter++; break;
                    case GoalPriorityResolver.GoalType.Regroup: snapshot.goalRegroup++; break;
                    case GoalPriorityResolver.GoalType.Flank: snapshot.goalFlank++; break;
                    case GoalPriorityResolver.GoalType.Guard: snapshot.goalGuard++; break;
                    case GoalPriorityResolver.GoalType.Wander: snapshot.goalWander++; break;
                    case GoalPriorityResolver.GoalType.Kite: snapshot.goalKite++; break;
                }
            }

            // Compute flock coherence (avg distance from own flock centroid)
            snapshot.flockCoherence = ComputeFlockCoherence_GOAPBoid();
        }
        else if (conditionManager != null && conditionManager.CurrentCondition == AgentCondition.BOIDSWithGOAPLeader)
        {
            // For leader condition, only 1 leader brain per flock has goal tracking
            var leaderBrains = FindObjectsByType<LeaderGoapBrain>(FindObjectsSortMode.None);
            var allBoids = FindObjectsByType<BoidAgent>(FindObjectsSortMode.None);
            snapshot.totalAgents = allBoids.Length;

            foreach (var brain in leaderBrains)
            {
                switch (brain.currentGoalType)
                {
                    case GoalPriorityResolver.GoalType.Attack: snapshot.goalAttack++; break;
                    case GoalPriorityResolver.GoalType.Flee: snapshot.goalFlee++; break;
                    case GoalPriorityResolver.GoalType.Scatter: snapshot.goalScatter++; break;
                    case GoalPriorityResolver.GoalType.Regroup: snapshot.goalRegroup++; break;
                    case GoalPriorityResolver.GoalType.Flank: snapshot.goalFlank++; break;
                    case GoalPriorityResolver.GoalType.Guard: snapshot.goalGuard++; break;
                    case GoalPriorityResolver.GoalType.Wander: snapshot.goalWander++; break;
                    case GoalPriorityResolver.GoalType.Kite: snapshot.goalKite++; break;
                }
            }

            snapshot.flockCoherence = ComputeFlockCoherence_Boid();
        }
        else if (conditionManager != null && conditionManager.CurrentCondition == AgentCondition.PureBOIDS)
        {
            var allBoids = FindObjectsByType<BoidAgent>(FindObjectsSortMode.None);
            snapshot.totalAgents = allBoids.Length;
            // PureBOIDS has no GOAP goals — all agents are in flocking state
            snapshot.goalWander = allBoids.Length;
            snapshot.flockCoherence = ComputeFlockCoherence_Boid();
        }

        snapshots.Add(snapshot);
    }

    private float ComputeFlockCoherence_GOAPBoid()
    {
        if (GOAPBoidAgent.AllAgents.Count == 0) return 0f;

        float totalDist = 0f;
        int count = 0;

        // Get unique flock IDs
        var flockIds = new HashSet<int>();
        foreach (var agent in GOAPBoidAgent.AllAgents)
            flockIds.Add(agent.flockId);

        foreach (int id in flockIds)
        {
            Vector3 centroid = GOAPBoidAgent.GetFlockCentroid(id);
            var mates = GOAPBoidAgent.GetFlockmates(id);
            foreach (var agent in mates)
            {
                totalDist += Vector3.Distance(agent.Position, centroid);
                count++;
            }
        }

        return count > 0 ? totalDist / count : 0f;
    }

    private float ComputeFlockCoherence_Boid()
    {
        var managers = FindObjectsByType<FlockManager>(FindObjectsSortMode.None);
        float totalDist = 0f;
        int count = 0;

        foreach (var mgr in managers)
        {
            if (mgr.BoidCount == 0) continue;
            Vector3 centroid = mgr.GetFlockCenter();
            foreach (var boid in mgr.Boids)
            {
                if (boid == null) continue;
                totalDist += Vector3.Distance(boid.Position, centroid);
                count++;
            }
        }

        return count > 0 ? totalDist / count : 0f;
    }

    /// <summary>
    /// Export goal distribution snapshots to CSV.
    /// </summary>
    public void ExportGoalDistribution(string filepath)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Time,Condition,TotalAgents,FlockCoherence,Attack,RangedAttack,Flee,Scatter,Regroup,Flank,Guard,Wander,Kite");

        foreach (var s in snapshots)
        {
            csv.AppendLine($"{s.time:F2},{s.condition},{s.totalAgents},{s.flockCoherence:F2}," +
                $"{s.goalAttack},{s.goalRangedAttack},{s.goalFlee},{s.goalScatter}," +
                $"{s.goalRegroup},{s.goalFlank},{s.goalGuard},{s.goalWander},{s.goalKite}");
        }

        System.IO.File.WriteAllText(filepath, csv.ToString());
        Debug.Log($"[BehavioralMetrics] Exported {snapshots.Count} snapshots to: {filepath}");
    }

    /// <summary>
    /// Export event log to CSV.
    /// </summary>
    public void ExportEventLog(string filepath)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Time,Condition,EventType,AgentName,FlockId,Details");

        foreach (var e in eventLog)
        {
            csv.AppendLine($"{e.time:F3},{e.condition},{e.eventType},{e.agentName},{e.flockId},{e.details}");
        }

        System.IO.File.WriteAllText(filepath, csv.ToString());
        Debug.Log($"[BehavioralMetrics] Exported {eventLog.Count} events to: {filepath}");
    }

    #if UNITY_EDITOR
    [ContextMenu("Export Goal Distribution to Desktop")]
    private void EditorExportGoals()
    {
        string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
        string filename = $"GoalDistribution_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        ExportGoalDistribution(System.IO.Path.Combine(desktop, filename));
    }

    [ContextMenu("Export Event Log to Desktop")]
    private void EditorExportEvents()
    {
        string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
        string filename = $"EventLog_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        ExportEventLog(System.IO.Path.Combine(desktop, filename));
    }

    [ContextMenu("Start Recording")]
    private void EditorStartRecording() { StartRecording(); }

    [ContextMenu("Stop Recording")]
    private void EditorStopRecording() { StopRecording(); }
    #endif
}
