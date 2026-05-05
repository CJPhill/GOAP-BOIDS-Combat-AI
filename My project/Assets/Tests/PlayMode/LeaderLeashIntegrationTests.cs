using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode integration test for the LeaderGoapBrain leash. The pure-formula
/// behaviour is already covered by <see cref="LeaderLeashTests"/>; this fixture
/// catches the *integration* failure mode CJ observed while watching a live
/// trial — leaders (especially the ranged Kite leader) drifting away from the
/// flock and not coming back.
///
/// Two reasons the integration can fail even though the formula is correct:
///   1. Script-execution-order bug — if the GOAP Action.Perform runs AFTER
///      LeaderGoapBrain.Update in the same frame, the leash multiplier on
///      <c>boid.velocity</c> is overwritten before BoidAgent integrates
///      velocity into position. The leader keeps charging at full speed.
///   2. Centroid dilution — FlockManager.GetFlockCenter() averages over ALL
///      boids INCLUDING the leader. With small per-flock counts (e.g. ranged
///      flock at N=50 split ≈ 17), a 1/17 leader weight in the centroid lets
///      the leader's apparent distance to centre stay under the 12 m leash
///      threshold even when the leader is 25 m+ from the nearest follower.
///
/// The test samples leader-vs-follower-only-centroid every ~0.5 s during a
/// short Combat trial and asserts the max sustained gap stays under
/// 2 × default maxLeaderSeparation. If it fails, the diagnostic message
/// reports per-flock max gap so we can tell which hypothesis is firing.
/// </summary>
[TestFixture]
public class LeaderLeashIntegrationTests
{
    private const string SceneName = "New Scene";
    private const float TrialSeconds = 15f;
    private const float WallDeadlineSeconds = 60f;
    private const float SampleIntervalSeconds = 0.5f;
    // Max separation default in LeaderGoapBrain.cs is 12 m. 2× allows brief
    // overshoot during a charge or kite retreat; sustained excursions past
    // this bound are the runaway behaviour CJ observed.
    private const float MaxAllowedSeparation = 24f;

    [UnityTest]
    public IEnumerator Leader_StaysBoundedToFollowers_DuringCombatTrial()
    {
        if (!Application.CanStreamedLevelBeLoaded(SceneName))
        {
            Assert.Inconclusive(
                $"Scene '{SceneName}' is not in Build Settings. "
                + "Add 'Assets/Scenes/New Scene.unity' under File → Build Profiles → Scene List.");
            yield break;
        }

        var load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
        while (!load.isDone) yield return null;
        yield return null;

        var cm = Object.FindFirstObjectByType<ConditionManager>();
        Assert.IsNotNull(cm, "No ConditionManager in scene.");
        var runner = cm.GetComponent<ExperimentRunner>();
        Assert.IsNotNull(runner, "No ExperimentRunner.");

        string tempDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"LeashIntegration_{System.DateTime.Now:HHmmss_fff}");
        System.IO.Directory.CreateDirectory(tempDir);

        SetField(runner, "runsPerCondition", 1);
        SetField(runner, "experimentDuration", TrialSeconds);
        SetField(runner, "transitionDelay", 0.5f);
        SetField(runner, "outputDirectory", tempDir);
        SetField(runner, "baseSeed", 42);
        SetField(runner, "conditionsToTest", new[] { AgentCondition.BOIDSWithGOAPLeader });
        // N=50 gives ~30 melee + 20 ranged after the standard 60/40 split,
        // which is enough that each flock has multiple followers (so a runaway
        // leader will produce a meaningful follower-centroid gap) but small
        // enough that the test stays under the 60 s wall deadline.
        SetField(runner, "agentCountsToTest", new[] { 50 });
        SetField(runner, "stimulusWarmupSec", 0f);
        SetField(runner, "modesToTest", new[] { BenchmarkMode.Combat });

        runner.RunAllExperiments();

        // Per-flock max-gap tracker — keyed by flock GameObject name so melee
        // and ranged are reported separately in the failure message.
        var maxGapByFlock = new Dictionary<string, float>();
        var sampleCountByFlock = new Dictionary<string, int>();
        var lastBreachByFlock = new Dictionary<string, float>();
        float lastSampleTime = -SampleIntervalSeconds;

        float wallDeadline = Time.realtimeSinceStartup + WallDeadlineSeconds;
        while ((bool)GetField(runner, "isRunning"))
        {
            if (Time.realtimeSinceStartup > wallDeadline)
                Assert.Fail("Trial did not complete within wall deadline.");

            if (Time.time - lastSampleTime >= SampleIntervalSeconds)
            {
                lastSampleTime = Time.time;
                SampleAllFlocks(maxGapByFlock, sampleCountByFlock, lastBreachByFlock);
            }

            yield return null;
        }

        // Report per-flock results. We only assert when we actually got
        // samples (a flock with no leader produces no samples and that's
        // a separate concern, not a leash failure).
        Assert.Greater(maxGapByFlock.Count, 0,
            "Test did not observe any flock with a leader during the trial. "
            + "Either spawning failed or the BOIDSWithGOAPLeader condition is "
            + "not assigning leaders to flocks.");

        var failures = new List<string>();
        foreach (var kv in maxGapByFlock)
        {
            int samples = sampleCountByFlock[kv.Key];
            float lastBreach = lastBreachByFlock.TryGetValue(kv.Key, out float v) ? v : -1f;
            if (kv.Value > MaxAllowedSeparation)
            {
                failures.Add(
                    $"  • {kv.Key}: max leader-vs-follower-centroid gap = {kv.Value:F1} m "
                    + $"(allowed: {MaxAllowedSeparation:F0} m), {samples} samples, "
                    + $"last breach at t={lastBreach:F1} s");
            }
        }

        if (failures.Count > 0)
        {
            string summary = string.Join("\n", failures);
            Assert.Fail(
                "Leader leash failed to keep one or more leaders bounded to their flock.\n"
                + summary
                + "\n\nLikely causes (in priority order):\n"
                + "  1. Script execution order — GOAP Action.Perform runs after\n"
                + "     LeaderGoapBrain.Update, so the leash velocity multiplier\n"
                + "     is overwritten before BoidAgent integrates velocity.\n"
                + "  2. GetFlockCenter() includes the leader's own position,\n"
                + "     diluting the leash distance metric at small flock counts.\n"
                + "  3. LeaderKiteAction.Phase.Retreat overshoots kite range and\n"
                + "     keeps backpedaling for the full 8 s KiteTimer.");
        }
    }

    /// <summary>
    /// Walks every active <see cref="FlockManager"/> in the scene, computes the
    /// leader's distance to the *follower-only* centroid (excluding the leader
    /// itself, unlike <see cref="FlockManager.GetFlockCenter"/>), and updates
    /// the per-flock max + last-breach trackers. Skips flocks without a leader
    /// or with no followers.
    /// </summary>
    private static void SampleAllFlocks(
        Dictionary<string, float> maxGapByFlock,
        Dictionary<string, int> sampleCountByFlock,
        Dictionary<string, float> lastBreachByFlock)
    {
        FlockManager[] flocks = Object.FindObjectsByType<FlockManager>(FindObjectsSortMode.None);
        foreach (var fm in flocks)
        {
            BoidAgent leader = fm.LeaderBoid;
            if (leader == null) continue;
            if (fm.BoidCount <= 1) continue; // leader alone — no follower centroid

            Vector3 followerCentroid = ComputeFollowerCentroid(fm, leader);
            float gap = Vector3.Distance(leader.Position, followerCentroid);

            string key = fm.gameObject.name;
            if (!maxGapByFlock.TryGetValue(key, out float prevMax) || gap > prevMax)
                maxGapByFlock[key] = gap;
            sampleCountByFlock[key] = sampleCountByFlock.TryGetValue(key, out int n) ? n + 1 : 1;
            if (gap > MaxAllowedSeparation)
                lastBreachByFlock[key] = Time.time;
        }
    }

    private static Vector3 ComputeFollowerCentroid(FlockManager fm, BoidAgent leader)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var b in fm.Boids)
        {
            if (b == null || b == leader) continue;
            sum += b.Position;
            count++;
        }
        return count > 0 ? sum / count : leader.Position;
    }

    // ── Reflection helpers (mirror BatchRunnerSmokeTest) ──

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
