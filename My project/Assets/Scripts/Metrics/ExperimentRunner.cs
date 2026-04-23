using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Automated experiment runner for thesis data collection.
///
/// Each invocation runs a **batch** of trials: <see cref="runsPerCondition"/>
/// trials per condition, interleaved so early runs of every condition finish
/// before later ones (avoids losing a condition if the session is stopped early).
///
/// Per-trial seeds are deterministic — trial i of any condition uses seed
/// <c>baseSeed + i</c>, so two batch runs with the same configuration produce
/// identical data for statistical validation.
///
/// A trial ends on the earliest of:
///   - PlayerDeath — <see cref="PlayerHealth.IsDead"/> returns true.
///   - FlockWiped  — <see cref="ConditionManager.AllFlocksDead"/> returns true.
///   - TimeLimit   — <see cref="experimentDuration"/> seconds elapsed.
///
/// Output files (written to <see cref="outputDirectory"/>, one set per batch):
///   - TrialSummary_batch_{ts}.csv  ← the one the thesis results section reads
///   - Performance_batch_{ts}.csv
///   - GoalDistribution_batch_{ts}.csv
///   - EventLog_batch_{ts}.csv
/// </summary>
public class ExperimentRunner : MonoBehaviour
{
    [Header("Batch Configuration")]
    [Tooltip("Max duration per trial (seconds) before a TimeLimit outcome.")]
    [SerializeField] private float experimentDuration = 60f;

    [Tooltip("Number of repeat trials per condition per agent-count. 10 gives usable stddev on most metrics.")]
    [SerializeField] private int runsPerCondition = 10;

    [Tooltip("Base seed. Each trial uses baseSeed + trialIndex where trialIndex is unique across sizes+runs.")]
    [SerializeField] private int baseSeed = 42;

    [Tooltip("Delay between trials for scene cleanup (seconds).")]
    [SerializeField] private float transitionDelay = 2f;

    [Tooltip("Which conditions to compare.")]
    [SerializeField] private AgentCondition[] conditionsToTest = new[]
    {
        AgentCondition.PureBOIDS,
        AgentCondition.BOIDSWithGOAPLeader,
        AgentCondition.GOAPWithBOIDSMovement
    };

    [Tooltip("Agent counts to sweep. Default [100] reproduces the original single-size batch. " +
             "[50, 100, 200, 400] adds a 4-point scaling curve. " +
             "Total trials = sizes × runs × conditions.")]
    [SerializeField] private int[] agentCountsToTest = new[] { 100 };

    [Header("Output")]
    [Tooltip("Directory for CSV output. Defaults to Desktop.")]
    [SerializeField] private string outputDirectory = "";

    [Header("Status (read-only)")]
    [SerializeField] private bool isRunning = false;
    [SerializeField] private int currentRunIndex = -1;
    [SerializeField] private int currentConditionIndex = -1;
    [SerializeField] private float timeRemaining = 0f;
    [SerializeField] private string lastOutcome = "";

    [Header("Automated Player")]
    [Tooltip("Reference to the AutomatedPlayer on the Player GameObject. Enabled during trials.")]
    [SerializeField] private AutomatedPlayer automatedPlayer;

    [Header("Required Dependencies (auto-found from this GameObject if empty)")]
    [Tooltip("Drag in if not on the same GameObject as ExperimentRunner.")]
    [SerializeField] private ConditionManager conditionManager;
    [SerializeField] private BehavioralMetricsCollector metricsCollector;
    [SerializeField] private PerformanceProfiler performanceProfiler;

    private PlayerHealth playerHealth;

    private void Awake()
    {
        // Fall back to same-GameObject components if Inspector slots are empty.
        if (conditionManager == null) conditionManager = GetComponent<ConditionManager>();
        if (metricsCollector == null) metricsCollector = GetComponent<BehavioralMetricsCollector>();
        if (performanceProfiler == null) performanceProfiler = GetComponent<PerformanceProfiler>();

        // Then try finding them anywhere in the scene as a second fallback.
        if (conditionManager == null) conditionManager = FindFirstObjectByType<ConditionManager>();
        if (metricsCollector == null) metricsCollector = FindFirstObjectByType<BehavioralMetricsCollector>();
        if (performanceProfiler == null) performanceProfiler = FindFirstObjectByType<PerformanceProfiler>();

        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);

        if (conditionManager != null && conditionManager.playerTransform != null)
            playerHealth = conditionManager.playerTransform.GetComponent<PlayerHealth>();
    }

    /// <summary>Entry point — kicks off the full batch.</summary>
    public void RunAllExperiments()
    {
        if (isRunning)
        {
            Debug.LogWarning("[ExperimentRunner] Already running!");
            return;
        }
        var missing = new System.Collections.Generic.List<string>();
        if (conditionManager == null) missing.Add(nameof(ConditionManager));
        if (metricsCollector == null) missing.Add(nameof(BehavioralMetricsCollector));
        if (performanceProfiler == null) missing.Add(nameof(PerformanceProfiler));
        if (missing.Count > 0)
        {
            Debug.LogError(
                $"[ExperimentRunner] Missing {string.Join(", ", missing)}. " +
                "Attach the missing component(s) to this GameObject, or drag a reference into the " +
                "Inspector under \"Required Dependencies\" on the ExperimentRunner.");
            return;
        }
        if (runsPerCondition < 1)
        {
            Debug.LogError("[ExperimentRunner] runsPerCondition must be ≥ 1.");
            return;
        }

        StartCoroutine(RunBatchCoroutine());
    }

    private IEnumerator RunBatchCoroutine()
    {
        isRunning = true;
        string batchStamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // Shared paths — all trials in the batch append to the same 4 files.
        string summaryPath     = System.IO.Path.Combine(outputDirectory, $"TrialSummary_batch_{batchStamp}.csv");
        string performancePath = System.IO.Path.Combine(outputDirectory, $"Performance_batch_{batchStamp}.csv");
        string goalsPath       = System.IO.Path.Combine(outputDirectory, $"GoalDistribution_batch_{batchStamp}.csv");
        string eventsPath      = System.IO.Path.Combine(outputDirectory, $"EventLog_batch_{batchStamp}.csv");

        // Guard against an empty sweep array — fall back to a single run at whatever
        // ConditionManager currently has configured so Play-from-Inspector still works.
        int[] sizes = agentCountsToTest;
        if (sizes == null || sizes.Length == 0) sizes = new[] { conditionManager.AgentCount };

        int totalTrials = sizes.Length * runsPerCondition * conditionsToTest.Length;
        Debug.Log($"[ExperimentRunner] Starting batch: {totalTrials} trials " +
                  $"({sizes.Length} size(s) × {runsPerCondition} runs × {conditionsToTest.Length} conditions), " +
                  $"duration ≤ {experimentDuration}s each. BaseSeed={baseSeed}.");

        // Track per-(size, condition) aggregates for the final text summary.
        var perGroup = new Dictionary<(int size, AgentCondition cond), List<TrialResult>>();

        // Size-outer, run-middle, condition-inner.
        // Rationale: smaller sizes complete first so if the batch is interrupted
        // we still have full data for the earlier size points. Runs interleave
        // conditions (same as before) so each (size, run) block covers all conditions.
        for (int sizeIdx = 0; sizeIdx < sizes.Length; sizeIdx++)
        {
            int size = sizes[sizeIdx];
            conditionManager.SetAgentCount(size);

            Debug.Log($"[ExperimentRunner] --- size {size} ({sizeIdx + 1}/{sizes.Length}) ---");

            for (int run = 0; run < runsPerCondition; run++)
            {
                currentRunIndex = run;
                // Unique seed per trial across the whole batch: sizeIdx offsets so
                // the same runId at different sizes gets different seeds, keeping
                // the EventLog / GoalDistribution CSVs joinable by Seed alone.
                int seed = baseSeed + sizeIdx * runsPerCondition + run;

                for (int c = 0; c < conditionsToTest.Length; c++)
                {
                    currentConditionIndex = c;
                    var condition = conditionsToTest[c];

                    Debug.Log($"[ExperimentRunner] === size={size} run={run}/{runsPerCondition - 1} cond={condition} seed={seed} ===");

                    var result = new TrialResult
                    {
                        condition = condition,
                        agentCount = size,
                        runId = run,
                        seed = seed
                    };
                    yield return RunSingleTrial(
                        condition, size, run, seed,
                        summaryPath, performancePath, goalsPath, eventsPath,
                        result);

                    var key = (size, condition);
                    if (!perGroup.ContainsKey(key)) perGroup[key] = new List<TrialResult>();
                    perGroup[key].Add(result);

                    yield return new WaitForSeconds(transitionDelay);
                }
            }
        }

        currentRunIndex = -1;
        currentConditionIndex = -1;
        isRunning = false;
        if (automatedPlayer != null) automatedPlayer.enabled = false;

        LogBatchSummary(perGroup, summaryPath);
    }

    private IEnumerator RunSingleTrial(
        AgentCondition condition, int configuredAgentCount, int runId, int seed,
        string summaryPath, string performancePath, string goalsPath, string eventsPath,
        TrialResult result)
    {
        // Reset everything from the previous trial.
        conditionManager.ClearAgents();
        conditionManager.SetSeed(seed); // reseeds Random.InitState + serialized field
        conditionManager.CurrentCondition = condition;
        conditionManager.SpawnAgentsForCondition();

        // Re-resolve playerHealth every trial — Awake-time lookup misses the ref
        // when ConditionManager.playerTransform is wired later. Tag lookup is the
        // same pattern the visual tests use.
        if (playerHealth == null)
        {
            GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null) playerHealth = playerGO.GetComponent<PlayerHealth>();
        }

        if (playerHealth != null) playerHealth.ResetHealth();
        if (automatedPlayer != null) automatedPlayer.enabled = true;

        // Start recording BEFORE the settle delay so any stimuli that fire during
        // scene settling (FlockManager.SetTarget from aggro triggers, first
        // GoalChange from brains waking up) land inside the recording window.
        // Previously these were dropped because recording started after the delay,
        // breaking the reaction-time metric for most trials.
        metricsCollector.StartRecording(runId, seed);
        performanceProfiler.StartTrial(runId, seed);
        performanceProfiler.ClearMetrics();

        // Belt-and-suspenders: the aggro trigger only fires OnTriggerEnter, which
        // may miss the case where the player is ALREADY inside the trigger at
        // spawn (no collision event). Force the stimulus explicitly so every
        // trial has a deterministic stimulus timestamp for reaction-time math.
        yield return null; // let Start() fire on spawned FlockManagers / GOAPBoidFlockManager
        ForceStimulusOnAllFlocks();

        // Now let the scene settle. Events during this window are still recorded.
        yield return new WaitForSeconds(transitionDelay);

        // Trial loop — exits on any termination condition.
        float elapsed = 0f;
        string outcome = "TimeLimit";
        timeRemaining = experimentDuration;

        while (elapsed < experimentDuration)
        {
            if (playerHealth != null && playerHealth.IsDead)
            {
                outcome = "PlayerDeath";
                break;
            }
            if (conditionManager.AllFlocksDead)
            {
                outcome = "FlockWiped";
                break;
            }

            elapsed += Time.deltaTime;
            timeRemaining = experimentDuration - elapsed;
            yield return null;
        }
        lastOutcome = outcome;

        // Capture metrics BEFORE stopping so snapshot/events include the final frame.
        metricsCollector.StopRecording();

        // Per-trial reaction times (by flockId: 0 = melee, 1 = ranged for GOAPBoid;
        // for PureBOIDS/Leader the flockId is cast from FlockType, so 0/1 too).
        float reactionMelee = metricsCollector.GetReactionTimeMs(0);
        float reactionRanged = metricsCollector.GetReactionTimeMs(1);
        float entropy = metricsCollector.ComputeGoalEntropy();
        var perfAgg = performanceProfiler.ComputeTrialAggregates();

        metricsCollector.AppendTrialSummaryRow(
            summaryPath, condition.ToString(), configuredAgentCount, runId, seed,
            outcome, elapsed,
            reactionMelee, reactionRanged, entropy,
            perfAgg.avgFPS, perfAgg.avgCpuTimeMs, perfAgg.avgCpuTimeMsPerAgent, perfAgg.maxAgentCount);

        metricsCollector.ExportGoalDistribution(goalsPath);
        metricsCollector.ExportEventLog(eventsPath);
        performanceProfiler.ExportMetrics(performancePath);

        // Snapshot the trial result for the text summary.
        result.outcome = outcome;
        result.duration = elapsed;
        result.reactionMelee = reactionMelee;
        result.reactionRanged = reactionRanged;
        result.entropy = entropy;
        result.avgFPS = perfAgg.avgFPS;
        result.avgCpuMs = perfAgg.avgCpuTimeMs;
        result.avgCpuMsPerAgent = perfAgg.avgCpuTimeMsPerAgent;
        result.maxAgentCount = perfAgg.maxAgentCount;

        if (automatedPlayer != null) automatedPlayer.enabled = false;
        Debug.Log($"[ExperimentRunner] Trial done: {condition} run={runId} outcome={outcome} duration={elapsed:F1}s entropy={entropy:F2}");
    }

    private void LogBatchSummary(
        Dictionary<(int size, AgentCondition cond), List<TrialResult>> perGroup,
        string summaryPath)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[ExperimentRunner] Batch complete.");
        sb.AppendLine($"Summary CSV: {summaryPath}");
        sb.AppendLine("");
        sb.AppendLine("Size | Condition                  | trials | AvgFPS (μ±σ) | CPU ms/agent (μ±σ) | Entropy (μ±σ) | React melee ms (μ±σ)");

        // Sort by (size, condition) so the table reads top-to-bottom in sweep order.
        var keys = new List<(int size, AgentCondition cond)>(perGroup.Keys);
        keys.Sort((a, b) =>
        {
            int byS = a.size.CompareTo(b.size);
            return byS != 0 ? byS : a.cond.CompareTo(b.cond);
        });

        foreach (var key in keys)
        {
            var results = perGroup[key];
            if (results.Count == 0) continue;
            var fps = MeanStd(results, r => r.avgFPS);
            var cpu = MeanStd(results, r => r.avgCpuMsPerAgent);
            var ent = MeanStd(results, r => r.entropy);
            var rxn = MeanStd(results, r => r.reactionMelee < 0 ? 0f : r.reactionMelee);
            sb.AppendLine($"{key.size,4} | {key.cond,-26} | {results.Count,6} | {fps.mean,6:F1}±{fps.std,5:F1} | {cpu.mean,7:F3}±{cpu.std,5:F3}     | {ent.mean,5:F2}±{ent.std,4:F2} | {rxn.mean,7:F1}±{rxn.std,5:F1}");
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Calls SetTarget / NotifyTargetAcquired on every flock in the scene with the
    /// player's transform. Guarantees a StimulusAcquired event inside the recording
    /// window even when the aggro trigger wouldn't fire (player already inside, or
    /// trigger disabled). Needed so the reaction-time metric gets a deterministic
    /// starting timestamp.
    /// </summary>
    private void ForceStimulusOnAllFlocks()
    {
        Transform player = null;
        if (playerHealth != null) player = playerHealth.transform;
        if (player == null)
        {
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null) player = playerGO.transform;
        }
        if (player == null) return;

        foreach (var fm in FindObjectsByType<FlockManager>(FindObjectsSortMode.None))
            fm.SetTarget(player);

        foreach (var gbfm in FindObjectsByType<GOAPBoidFlockManager>(FindObjectsSortMode.None))
            gbfm.NotifyTargetAcquired(player);
    }

    private static (float mean, float std) MeanStd(List<TrialResult> results, System.Func<TrialResult, float> accessor)
    {
        if (results.Count == 0) return (0, 0);
        double sum = 0; foreach (var r in results) sum += accessor(r);
        double mean = sum / results.Count;
        double sqSum = 0; foreach (var r in results) { double d = accessor(r) - mean; sqSum += d * d; }
        double std = results.Count > 1 ? System.Math.Sqrt(sqSum / (results.Count - 1)) : 0;
        return ((float)mean, (float)std);
    }

    private class TrialResult
    {
        public AgentCondition condition;
        public int agentCount;
        public int runId;
        public int seed;
        public string outcome = "";
        public float duration;
        public float reactionMelee;
        public float reactionRanged;
        public float entropy;
        public float avgFPS;
        public float avgCpuMs;
        public float avgCpuMsPerAgent;
        public int maxAgentCount;
    }

    #if UNITY_EDITOR
    [ContextMenu("Run All Experiments")]
    private void EditorRunAll()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ExperimentRunner] Must be in Play Mode!");
            return;
        }
        RunAllExperiments();
    }

    [ContextMenu("Stop Experiment")]
    private void EditorStop()
    {
        StopAllCoroutines();
        if (metricsCollector != null) metricsCollector.StopRecording();
        isRunning = false;
        currentRunIndex = -1;
        currentConditionIndex = -1;
        Debug.Log("[ExperimentRunner] Batch stopped.");
    }
    #endif
}
