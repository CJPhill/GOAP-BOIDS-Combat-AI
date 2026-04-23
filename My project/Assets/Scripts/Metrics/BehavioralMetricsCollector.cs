using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Collects behavioral metrics for thesis comparison across swarming conditions.
/// Tracks goal distribution over time, flock coherence, reaction time, and event logs.
/// Attach to the same GameObject as ConditionManager.
///
/// In batch mode (ExperimentRunner), each run carries a runId + seed that's
/// written into every exported row so aggregation scripts can group by run.
/// </summary>
public class BehavioralMetricsCollector : MonoBehaviour
{
    public static BehavioralMetricsCollector Instance { get; private set; }

    [Header("Configuration")]
    [SerializeField] private float sampleInterval = 0.5f;
    [SerializeField] private bool isRecording = false;

    private ConditionManager conditionManager;
    private float nextSampleTime;

    // Per-trial run identifiers (populated by ExperimentRunner before StartRecording).
    private int currentRunId = 0;
    private int currentSeed = 0;

    // Stimulus-to-response reaction-time tracking.
    // Key = flockId; value = first SetTarget timestamp within this trial.
    private readonly Dictionary<int, float> stimulusTimeByFlock = new Dictionary<int, float>();
    // Key = flockId; value = first goal/state transition away from Idle/Grouping/Wander.
    private readonly Dictionary<int, float> responseTimeByFlock = new Dictionary<int, float>();

    // Frame-level goal distribution snapshots
    private List<GoalDistributionSnapshot> snapshots = new List<GoalDistributionSnapshot>();

    // Event log
    private List<BehavioralEvent> eventLog = new List<BehavioralEvent>();

    [System.Serializable]
    public struct GoalDistributionSnapshot
    {
        public float time;
        public string condition;
        public int runId;
        public int seed;
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
        public int runId;
        public int seed;
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

    /// <summary>
    /// Simple-mode recording start (no batch context). Keeps backward compat with
    /// editor menu items that don't pass run info.
    /// </summary>
    public void StartRecording() => StartRecording(runId: 0, seed: 0);

    public void StartRecording(int runId, int seed)
    {
        isRecording = true;
        nextSampleTime = Time.time;
        currentRunId = runId;
        currentSeed = seed;
        snapshots.Clear();
        eventLog.Clear();
        stimulusTimeByFlock.Clear();
        responseTimeByFlock.Clear();
        Debug.Log($"[BehavioralMetrics] Recording started (run={runId}, seed={seed}).");
    }

    public void StopRecording()
    {
        isRecording = false;
        Debug.Log($"[BehavioralMetrics] Recording stopped. {snapshots.Count} snapshots, {eventLog.Count} events.");
    }

    /// <summary>
    /// Log a behavioral event from any script (goal change, damage, attack completion).
    /// Also captures stimulus/response timestamps for reaction-time computation:
    /// - "StimulusAcquired" events record per-flock trigger time.
    /// - "GoalChange" events record the first transition away from Idle/Grouping/Wander.
    /// </summary>
    public void LogEvent(string eventType, string agentName, int flockId, string details)
    {
        if (!isRecording) return;

        float now = Time.time;

        eventLog.Add(new BehavioralEvent
        {
            time = now,
            condition = conditionManager != null ? conditionManager.CurrentCondition.ToString() : "Unknown",
            runId = currentRunId,
            seed = currentSeed,
            eventType = eventType,
            agentName = agentName,
            flockId = flockId,
            details = details
        });

        // Stimulus: first time a flock sees its target set this trial.
        if (eventType == "StimulusAcquired")
        {
            if (!stimulusTimeByFlock.ContainsKey(flockId))
                stimulusTimeByFlock[flockId] = now;
        }

        // Response: first non-passive GoalChange per flock. Two guards:
        //  1. Stimulus must have fired first for this flock — otherwise the brain's
        //     initial "Wander→Attack" from seeing the player before ForceStimulus
        //     lands gets counted as a response with a negative delta (clamped to 0).
        //  2. Idle/Grouping/Wander are not "responses" — they're the passive state.
        if (eventType == "GoalChange"
            && stimulusTimeByFlock.ContainsKey(flockId)
            && !responseTimeByFlock.ContainsKey(flockId))
        {
            if (IsActiveGoalDetail(details))
                responseTimeByFlock[flockId] = now;
        }
    }

    private static bool IsActiveGoalDetail(string details)
    {
        if (string.IsNullOrEmpty(details)) return false;
        // Details format: "Prev→New" — only the New half matters.
        int sep = details.IndexOf('→');
        string newGoal = sep >= 0 ? details.Substring(sep + "→".Length) : details;
        newGoal = newGoal.Trim();
        return newGoal != "Wander" && newGoal != "Idle" && newGoal != "Grouping";
    }

    private void RecordSnapshot()
    {
        string condition = conditionManager != null ? conditionManager.CurrentCondition.ToString() : "Unknown";

        var snapshot = new GoalDistributionSnapshot
        {
            time = Time.time,
            condition = condition,
            runId = currentRunId,
            seed = currentSeed,
            totalAgents = 0,
            flockCoherence = 0f
        };

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

            snapshot.flockCoherence = ComputeFlockCoherence_GOAPBoid();
        }
        else if (conditionManager != null && conditionManager.CurrentCondition == AgentCondition.BOIDSWithGOAPLeader)
        {
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

            // PureBOIDS now has a flock-level state machine — bucket by FlockManager.State
            // so the goal-distribution CSV has comparable columns across conditions.
            var managers = FindObjectsByType<FlockManager>(FindObjectsSortMode.None);
            foreach (var mgr in managers)
            {
                int boidCount = mgr.BoidCount;
                switch (mgr.State)
                {
                    case FlockManager.FlockState.Engaging: snapshot.goalAttack += boidCount; break;
                    case FlockManager.FlockState.Kiting: snapshot.goalKite += boidCount; break;
                    case FlockManager.FlockState.Fleeing: snapshot.goalFlee += boidCount; break;
                    case FlockManager.FlockState.Scattering: snapshot.goalScatter += boidCount; break;
                    case FlockManager.FlockState.Flanking: snapshot.goalFlank += boidCount; break;
                    case FlockManager.FlockState.Guarding: snapshot.goalGuard += boidCount; break;
                    case FlockManager.FlockState.Regrouping: snapshot.goalRegroup += boidCount; break;
                    default: snapshot.goalWander += boidCount; break; // Idle + Grouping
                }
            }

            snapshot.flockCoherence = ComputeFlockCoherence_Boid();
        }

        snapshots.Add(snapshot);
    }

    private float ComputeFlockCoherence_GOAPBoid()
    {
        if (GOAPBoidAgent.AllAgents.Count == 0) return 0f;

        float totalDist = 0f;
        int count = 0;

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

    // ── Trial-summary analytics ──

    /// <summary>
    /// Per-flock reaction time in ms (first StimulusAcquired → first active GoalChange).
    /// Returns -1 for flocks that never responded during the trial.
    /// </summary>
    public float GetReactionTimeMs(int flockId)
    {
        if (!stimulusTimeByFlock.TryGetValue(flockId, out float tStim)) return -1f;
        if (!responseTimeByFlock.TryGetValue(flockId, out float tResp)) return -1f;
        return Mathf.Max(0f, (tResp - tStim) * 1000f);
    }

    /// <summary>
    /// Shannon entropy of the goal distribution across all snapshots in this trial.
    /// H = -Σ p_i log2(p_i) over 9 possible buckets. Range: [0, log2(9) ≈ 3.17].
    /// Higher = more diverse behavior repertoire.
    /// </summary>
    public float ComputeGoalEntropy()
    {
        if (snapshots.Count == 0) return 0f;

        long[] counts = new long[9];
        foreach (var s in snapshots)
        {
            counts[0] += s.goalAttack;
            counts[1] += s.goalRangedAttack;
            counts[2] += s.goalFlee;
            counts[3] += s.goalScatter;
            counts[4] += s.goalRegroup;
            counts[5] += s.goalFlank;
            counts[6] += s.goalGuard;
            counts[7] += s.goalWander;
            counts[8] += s.goalKite;
        }

        return ShannonEntropy(counts);
    }

    /// <summary>
    /// Pure-function Shannon entropy (bits) over a bucket-count array.
    /// Extracted for unit testing. 0 when empty or single-bucket; log2(N) for uniform.
    /// </summary>
    public static float ShannonEntropy(long[] counts)
    {
        if (counts == null || counts.Length == 0) return 0f;
        long total = 0;
        for (int i = 0; i < counts.Length; i++) total += counts[i];
        if (total == 0) return 0f;

        double h = 0.0;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] <= 0) continue;
            double p = (double)counts[i] / total;
            h -= p * System.Math.Log(p, 2);
        }
        return (float)h;
    }

    public int SnapshotCount => snapshots.Count;
    public int EventCount => eventLog.Count;

    // ── CSV export ──

    /// <summary>
    /// Writes goal distribution to CSV. If the file already exists, appends rows
    /// without rewriting the header (batch mode). Otherwise writes header + rows.
    /// </summary>
    public void ExportGoalDistribution(string filepath)
    {
        bool fileExists = System.IO.File.Exists(filepath);
        var csv = new StringBuilder();
        if (!fileExists)
            csv.AppendLine("Time,Condition,RunId,Seed,TotalAgents,FlockCoherence,Attack,RangedAttack,Flee,Scatter,Regroup,Flank,Guard,Wander,Kite");

        foreach (var s in snapshots)
        {
            csv.AppendLine($"{s.time:F2},{s.condition},{s.runId},{s.seed},{s.totalAgents},{s.flockCoherence:F2}," +
                $"{s.goalAttack},{s.goalRangedAttack},{s.goalFlee},{s.goalScatter}," +
                $"{s.goalRegroup},{s.goalFlank},{s.goalGuard},{s.goalWander},{s.goalKite}");
        }

        if (fileExists)
            System.IO.File.AppendAllText(filepath, csv.ToString());
        else
            System.IO.File.WriteAllText(filepath, csv.ToString());
        Debug.Log($"[BehavioralMetrics] Wrote {snapshots.Count} snapshots to: {filepath}");
    }

    public void ExportEventLog(string filepath)
    {
        bool fileExists = System.IO.File.Exists(filepath);
        var csv = new StringBuilder();
        if (!fileExists)
            csv.AppendLine("Time,Condition,RunId,Seed,EventType,AgentName,FlockId,Details");

        foreach (var e in eventLog)
        {
            csv.AppendLine($"{e.time:F3},{e.condition},{e.runId},{e.seed},{e.eventType},{e.agentName},{e.flockId},{e.details}");
        }

        if (fileExists)
            System.IO.File.AppendAllText(filepath, csv.ToString());
        else
            System.IO.File.WriteAllText(filepath, csv.ToString());
        Debug.Log($"[BehavioralMetrics] Wrote {eventLog.Count} events to: {filepath}");
    }

    /// <summary>
    /// Appends a single row to the batch-wide TrialSummary.csv — the file the
    /// thesis results section actually references.
    /// </summary>
    public void AppendTrialSummaryRow(
        string filepath,
        string condition,
        int configuredAgentCount,
        int runId,
        int seed,
        string outcome,
        float actualDuration,
        float reactionMeleeMs,
        float reactionRangedMs,
        float goalEntropy,
        float avgFPS,
        float avgCpuTimeMs,
        float avgCpuTimeMsPerAgent,
        int maxAgentCount)
    {
        bool fileExists = System.IO.File.Exists(filepath);
        var sb = new StringBuilder();
        if (!fileExists)
            sb.AppendLine("Condition,AgentCount,RunId,Seed,Outcome,DurationSec,ReactionMeleeMs,ReactionRangedMs,GoalEntropy,AvgFPS,AvgCpuMs,AvgCpuMsPerAgent,MaxAgentCount");

        sb.AppendLine($"{condition},{configuredAgentCount},{runId},{seed},{outcome},{actualDuration:F2}," +
                      $"{reactionMeleeMs:F1},{reactionRangedMs:F1},{goalEntropy:F3}," +
                      $"{avgFPS:F1},{avgCpuTimeMs:F3},{avgCpuTimeMsPerAgent:F3},{maxAgentCount}");

        if (fileExists)
            System.IO.File.AppendAllText(filepath, sb.ToString());
        else
            System.IO.File.WriteAllText(filepath, sb.ToString());
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
