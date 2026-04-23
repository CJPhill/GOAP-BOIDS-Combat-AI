using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// End-to-end smoke test for ExperimentRunner batch mode. Configures a minimal
/// batch (1 condition × 1 run × 2s), runs it, and asserts the TrialSummary.csv
/// landed with the expected header + row count. Confirms the termination /
/// metrics / batch pipeline is wired correctly after the item 3 / 2 / 1 changes.
/// </summary>
[TestFixture]
public class BatchRunnerSmokeTest
{
    private const string SceneName = "New Scene";

    [UnityTest]
    public IEnumerator Batch_SingleRun_PureBOIDS_WritesTrialSummary()
    {
        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Assert.Inconclusive(
                $"Scene '{SceneName}' is not in Build Settings. " +
                "Add 'Assets/Scenes/New Scene.unity' under File → Build Profiles → Scene List.");
            yield break;
        }

        var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        while (!load.isDone) yield return null;
        yield return null;

        var cm = Object.FindFirstObjectByType<ConditionManager>();
        if (cm == null) { Assert.Inconclusive("No ConditionManager in scene."); yield break; }

        var runner = cm.GetComponent<ExperimentRunner>();
        if (runner == null) { Assert.Inconclusive("No ExperimentRunner on ConditionManager GameObject."); yield break; }

        // Unique temp dir so the test doesn't collide with real batches on Desktop.
        string tempDir = Path.Combine(Path.GetTempPath(), $"BatchSmoke_{System.DateTime.Now:HHmmss_fff}");
        Directory.CreateDirectory(tempDir);

        SetField(runner, "runsPerCondition", 1);
        SetField(runner, "experimentDuration", 2f);
        SetField(runner, "transitionDelay", 0.5f);
        SetField(runner, "outputDirectory", tempDir);
        SetField(runner, "baseSeed", 42);
        SetField(runner, "conditionsToTest", new[] { AgentCondition.PureBOIDS });

        runner.RunAllExperiments();

        // Batch should finish in ≤ 10s (2s trial + 0.5s delay + startup). Poll isRunning.
        float wallDeadline = Time.realtimeSinceStartup + 20f;
        while ((bool)GetField(runner, "isRunning"))
        {
            if (Time.realtimeSinceStartup > wallDeadline)
                Assert.Fail("Batch did not complete within 20s wall time.");
            yield return null;
        }

        // Verify outputs.
        string[] summaries = Directory.GetFiles(tempDir, "TrialSummary_batch_*.csv");
        Assert.AreEqual(1, summaries.Length, "Expected exactly one TrialSummary CSV.");

        string[] lines = File.ReadAllLines(summaries[0]);
        Assert.GreaterOrEqual(lines.Length, 2, "Expected header + at least one data row.");

        string header = lines[0];
        StringAssert.Contains("Condition", header);
        StringAssert.Contains("RunId", header);
        StringAssert.Contains("Seed", header);
        StringAssert.Contains("Outcome", header);
        StringAssert.Contains("DurationSec", header);
        StringAssert.Contains("ReactionMeleeMs", header);
        StringAssert.Contains("GoalEntropy", header);
        StringAssert.Contains("AvgCpuMsPerAgent", header);

        StringAssert.Contains("PureBOIDS", lines[1]);
        StringAssert.Contains("42", lines[1]); // seed

        // Sibling CSVs should also exist.
        Assert.AreEqual(1, Directory.GetFiles(tempDir, "Performance_batch_*.csv").Length);
        Assert.AreEqual(1, Directory.GetFiles(tempDir, "GoalDistribution_batch_*.csv").Length);
        Assert.AreEqual(1, Directory.GetFiles(tempDir, "EventLog_batch_*.csv").Length);
    }

    // ── Reflection helpers (test-only) ──

    private static void SetField(object target, string name, object value)
    {
        var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null) throw new System.Exception($"No field '{name}' on {target.GetType().Name}");
        f.SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
        var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (f == null) throw new System.Exception($"No field '{name}' on {target.GetType().Name}");
        return f.GetValue(target);
    }
}
