using System.Collections;
using UnityEngine;

/// <summary>
/// Automated experiment runner for thesis data collection.
/// Cycles through swarming conditions, runs each for a configurable duration,
/// and exports behavioral metrics CSVs.
///
/// Usage: Attach alongside ConditionManager + BehavioralMetricsCollector.
/// Right-click context menu → "Run All Experiments" in Play mode.
/// </summary>
public class ExperimentRunner : MonoBehaviour
{
    [Header("Experiment Settings")]
    [Tooltip("Duration to run each condition (seconds).")]
    [SerializeField] private float experimentDuration = 60f;

    [Tooltip("Delay between conditions for scene cleanup (seconds).")]
    [SerializeField] private float transitionDelay = 3f;

    [Tooltip("Which conditions to run in order.")]
    [SerializeField] private AgentCondition[] conditionsToTest = new[]
    {
        AgentCondition.PureBOIDS,
        AgentCondition.BOIDSWithGOAPLeader,
        AgentCondition.GOAPWithBOIDSMovement
    };

    [Header("Output")]
    [Tooltip("Directory for CSV output. Defaults to Desktop.")]
    [SerializeField] private string outputDirectory = "";

    [Header("Status")]
    [SerializeField] private bool isRunning = false;
    [SerializeField] private int currentConditionIndex = -1;
    [SerializeField] private float timeRemaining = 0f;

    [Header("Automated Player")]
    [Tooltip("Reference to the AutomatedPlayer on the Player GameObject. Enabled during experiments.")]
    [SerializeField] private AutomatedPlayer automatedPlayer;

    private ConditionManager conditionManager;
    private BehavioralMetricsCollector metricsCollector;
    private PerformanceProfiler performanceProfiler;
    private PlayerHealth playerHealth;

    private void Awake()
    {
        conditionManager = GetComponent<ConditionManager>();
        metricsCollector = GetComponent<BehavioralMetricsCollector>();
        performanceProfiler = GetComponent<PerformanceProfiler>();

        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);

        // Find player health for reset between conditions
        if (conditionManager != null && conditionManager.playerTransform != null)
            playerHealth = conditionManager.playerTransform.GetComponent<PlayerHealth>();
    }

    /// <summary>
    /// Runs all configured conditions sequentially, recording metrics for each.
    /// </summary>
    public void RunAllExperiments()
    {
        if (isRunning)
        {
            Debug.LogWarning("[ExperimentRunner] Already running!");
            return;
        }

        if (conditionManager == null || metricsCollector == null)
        {
            Debug.LogError("[ExperimentRunner] Missing ConditionManager or BehavioralMetricsCollector!");
            return;
        }

        StartCoroutine(RunExperimentsCoroutine());
    }

    private IEnumerator RunExperimentsCoroutine()
    {
        isRunning = true;
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        Debug.Log($"[ExperimentRunner] Starting {conditionsToTest.Length} experiments, {experimentDuration}s each.");

        for (int i = 0; i < conditionsToTest.Length; i++)
        {
            currentConditionIndex = i;
            AgentCondition condition = conditionsToTest[i];

            Debug.Log($"[ExperimentRunner] === Starting {condition} ({i + 1}/{conditionsToTest.Length}) ===");

            // Set condition and spawn agents
            conditionManager.CurrentCondition = condition;
            conditionManager.SpawnAgentsForCondition();

            // Reset player health and enable automated movement
            if (playerHealth != null)
                playerHealth.ResetHealth();
            if (automatedPlayer != null)
                automatedPlayer.enabled = true;

            // Wait for scene to stabilize
            yield return new WaitForSeconds(transitionDelay);

            // Start recording
            metricsCollector.StartRecording();
            if (performanceProfiler != null)
                performanceProfiler.ClearMetrics();

            // Run for duration
            timeRemaining = experimentDuration;
            while (timeRemaining > 0f)
            {
                timeRemaining -= Time.deltaTime;
                yield return null;
            }

            // Stop recording and export
            metricsCollector.StopRecording();

            string goalPath = System.IO.Path.Combine(outputDirectory,
                $"GoalDistribution_{condition}_{timestamp}.csv");
            string eventPath = System.IO.Path.Combine(outputDirectory,
                $"EventLog_{condition}_{timestamp}.csv");
            string perfPath = System.IO.Path.Combine(outputDirectory,
                $"Performance_{condition}_{timestamp}.csv");

            metricsCollector.ExportGoalDistribution(goalPath);
            metricsCollector.ExportEventLog(eventPath);

            if (performanceProfiler != null)
                performanceProfiler.ExportMetrics(perfPath);

            Debug.Log($"[ExperimentRunner] === Completed {condition} ===");

            // Clean up
            if (automatedPlayer != null)
                automatedPlayer.enabled = false;
            conditionManager.ClearAgents();
            yield return new WaitForSeconds(1f);
        }

        currentConditionIndex = -1;
        isRunning = false;
        if (automatedPlayer != null)
            automatedPlayer.enabled = false;
        Debug.Log($"[ExperimentRunner] All experiments complete. CSVs saved to: {outputDirectory}");
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
        if (metricsCollector != null)
            metricsCollector.StopRecording();
        isRunning = false;
        currentConditionIndex = -1;
        Debug.Log("[ExperimentRunner] Experiment stopped.");
    }
    #endif
}
