using UnityEngine;
using UnityEngine.Profiling;
using System.Collections.Generic;

/// <summary>
/// Tracks performance metrics for the four experimental agent conditions.
/// Measures CPU time, frame rate, and memory usage.
///
/// Usage: Attach to ConditionManager GameObject. Metrics are automatically collected each frame.
/// Use ExportMetrics() to save data to CSV.
/// </summary>
public class PerformanceProfiler : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Window size (in seconds) for FPS averaging.")]
    [SerializeField] private float fpsAveragingWindow = 10f;

    [Tooltip("Enable detailed CPU profiling (impacts performance).")]
    [SerializeField] private bool enableDetailedProfiling = true;

    [Header("Current Metrics (Read-Only)")]
    [SerializeField] private float currentFPS;
    [SerializeField] private float averageFPS;
    [SerializeField] private int activeAgentCount;
    [SerializeField] private float cpuTimeMs;

    private Queue<float> fpsHistory = new Queue<float>();
    private float fpsSum;
    private float lastFrameTime;

    // Per-frame metrics for export
    private List<FrameMetrics> frameMetricsLog = new List<FrameMetrics>();

    private ConditionManager conditionManager;

    private void Start()
    {
        lastFrameTime = Time.realtimeSinceStartup;
        conditionManager = GetComponent<ConditionManager>();
    }

    private void Update()
    {
        RecordFrameMetrics();
    }

    /// <summary>
    /// Records metrics for the current frame.
    /// </summary>
    private void RecordFrameMetrics()
    {
        if (enableDetailedProfiling)
            Profiler.BeginSample("PerformanceProfiler.RecordFrameMetrics");

        // Calculate current FPS
        float currentTime = Time.realtimeSinceStartup;
        float deltaTime = currentTime - lastFrameTime;
        lastFrameTime = currentTime;

        if (deltaTime > 0f)
        {
            currentFPS = 1f / deltaTime;
        }

        // Update FPS history for averaging
        fpsHistory.Enqueue(currentFPS);
        fpsSum += currentFPS;

        while (fpsHistory.Count > 0 && (fpsHistory.Count * deltaTime) > fpsAveragingWindow)
        {
            fpsSum -= fpsHistory.Dequeue();
        }

        averageFPS = fpsHistory.Count > 0 ? fpsSum / fpsHistory.Count : currentFPS;

        // Track active agent count
        activeAgentCount = CountActiveAgents();

        // CPU time (approximation via Time.deltaTime)
        cpuTimeMs = Time.deltaTime * 1000f;

        // Log frame data
        frameMetricsLog.Add(new FrameMetrics
        {
            frameNumber = Time.frameCount,
            time = Time.time,
            condition = conditionManager != null ? conditionManager.CurrentCondition.ToString() : "Unknown",
            fps = currentFPS,
            averageFPS = averageFPS,
            agentCount = activeAgentCount,
            cpuTimeMs = cpuTimeMs
        });

        if (enableDetailedProfiling)
            Profiler.EndSample();
    }

    /// <summary>
    /// Counts the number of currently active agents in the scene.
    /// </summary>
    private int CountActiveAgents()
    {
        if (conditionManager != null && conditionManager.CurrentCondition == AgentCondition.GOAPWithBOIDSMovement)
            return GOAPBoidAgent.AllAgents.Count;

        // BoidAgent-based conditions (PureBOIDS, BOIDSWithGOAPLeader)
        var boids = FindObjectsByType<BoidAgent>(FindObjectsSortMode.None);
        if (boids.Length > 0)
            return boids.Length;

        // Fallback for PureGOAP or unknown conditions
        return FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Length;
    }

    /// <summary>
    /// Exports all collected metrics to a CSV file.
    /// </summary>
    public void ExportMetrics(string filepath)
    {
        if (frameMetricsLog.Count == 0)
        {
            Debug.LogWarning("[PerformanceProfiler] No metrics to export.");
            return;
        }

        Profiler.BeginSample("PerformanceProfiler.ExportMetrics");

        System.Text.StringBuilder csv = new System.Text.StringBuilder();
        csv.AppendLine("Frame,Time,Condition,FPS,AvgFPS,AgentCount,CPUTimeMs");

        foreach (var frame in frameMetricsLog)
        {
            csv.AppendLine($"{frame.frameNumber},{frame.time:F3},{frame.condition},{frame.fps:F2},{frame.averageFPS:F2},{frame.agentCount},{frame.cpuTimeMs:F3}");
        }

        try
        {
            System.IO.File.WriteAllText(filepath, csv.ToString());
            Debug.Log($"[PerformanceProfiler] Exported {frameMetricsLog.Count} frames to: {filepath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PerformanceProfiler] Failed to export metrics: {e.Message}");
        }

        Profiler.EndSample();
    }

    /// <summary>
    /// Clears all collected metrics.
    /// </summary>
    public void ClearMetrics()
    {
        frameMetricsLog.Clear();
        fpsHistory.Clear();
        fpsSum = 0f;
        Debug.Log("[PerformanceProfiler] Metrics cleared.");
    }

    /// <summary>
    /// Returns the current average FPS.
    /// </summary>
    public float GetAverageFPS()
    {
        return averageFPS;
    }

    /// <summary>
    /// Returns the current instantaneous FPS.
    /// </summary>
    public float GetCurrentFPS()
    {
        return currentFPS;
    }

    [System.Serializable]
    private struct FrameMetrics
    {
        public int frameNumber;
        public float time;
        public string condition;
        public float fps;
        public float averageFPS;
        public int agentCount;
        public float cpuTimeMs;
    }

    #if UNITY_EDITOR
    [ContextMenu("Export Metrics to Desktop")]
    private void EditorExportMetrics()
    {
        string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
        string filename = $"PerformanceMetrics_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        string filepath = System.IO.Path.Combine(desktop, filename);
        ExportMetrics(filepath);
    }

    [ContextMenu("Clear Metrics")]
    private void EditorClearMetrics()
    {
        ClearMetrics();
    }
    #endif
}
