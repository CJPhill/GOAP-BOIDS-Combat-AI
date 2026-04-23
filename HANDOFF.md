# AI Game Demo — Workstation Handoff
**Last updated:** 2026-04-23 | **Branch:** main

---

## Project
Unity 6000.3.8f1 thesis project comparing **three swarming architectures** against a consistent adversary (AutomatedPlayer) in a shared combat scene.
CrashKonijn GOAP v3.1.2 + custom BOIDS implementation.

### Conditions under comparison
| # | Name | Decision mechanism |
|---|------|-------------------|
| 1 | PureGOAP | **Kept in code, excluded from benchmarks.** GOAP-only agents (no swarm). |
| 2 | PureBOIDS | Flock-level state machine + pure emergent swarm — no planning. |
| 3 | BOIDSWithGOAPLeader | GOAP leader per flock, followers are plain BOIDS (cohese to leader). |
| 4 | GOAPWithBOIDSMovement | GOAP brain per agent + BOIDS steering forces layered on. |

All three active conditions express the **same 9 behaviors** (Wander, Attack, RangedAttack, Flee, Scatter, Kite, Flank, Guard, Regroup) through different mechanisms. Tuning parity: same agent count, same pooled HP, same player damage across conditions so architecture is the only variable.

---

## Key Files

```
My project/Assets/Scripts/
  Conditions/
    ConditionManager.cs               ← spawns all conditions; SetSeed/SetAgentCount for batch runs; comparison HP override
    AttackType.cs                     ← enum { Melee, Ranged }
    Shared/
      GoalPriorityResolver.cs         ← priority tree for goal selection (shared across conditions 3 & 4)
      FlockStateResolver.cs           ← ✨ pure-function resolver for PureBOIDS flock states (mirrors priority tree)

    PureGOAP/                         ← condition 1 (dropped from benchmarks, kept for reference)

    BOIDSWithGOAPLeader/              ← condition 3
      LeaderGoapBrain.cs              ← leader goal selection; ✨ now logs per-flock (fixed flockId bug)
      Actions/ Goals/ Capabilities/

    GOAPWithBOIDSMovement/            ← condition 4
      GOAPBoidAgent.cs                ← IEnemy impl; flockManager back-ref; centroid leash; pooled HP proxy
      GOAPBoidBrain.cs                ← goal selection; logs GoalChange events
      GOAPBoidFlockManager.cs         ← ✨ flock-level glue: pooled HP, centroid cache, attack phases, formation ring
      Actions/                        ← Wander/Flee/Attack/Ranged/Kite/Flank/Guard/Regroup/Scatter
      Sensors/ Capabilities/

  Boids/
    BoidAgent.cs                      ← ✨ state-based steering (flee/scatter/kite/flank/guard/regroup branches)
    BoidSettings.cs                   ← ScriptableObject; ✨ new parity fields (flee thresholds, scatter dur, etc.)
    FlockManager.cs                   ← ✨ 9-state machine; UpdateFlockState; ApplyComparisonOverrides; StimulusAcquired event
    FlockCoordinator.cs
    BoidProjectile.cs

  Metrics/
    ExperimentRunner.cs               ← ✨ batch mode: multi-run × multi-size × multi-condition; termination conditions
    BehavioralMetricsCollector.cs     ← ✨ reaction time + Shannon entropy; run/seed/size tagging; TrialSummary append
    PerformanceProfiler.cs            ← ✨ per-agent CPU column; trial aggregates; run/seed tagging
    AutomatedPlayer.cs                ← hack-and-slash bot (7 phases, 5 abilities, seeded)

  PlayerHealth.cs                     ← ✨ IsDead persists for trial termination
  VisualTests/
    GoalLabelOverlay.cs               ← on-screen goal label above each GOAPBoidAgent

My project/Assets/Editor/
  ExperimentBatchCli.cs               ← ✨ "Thesis → Run Batch Experiment" menu + CLI -executeMethod hook

My project/Assets/Tests/
  EditMode/
    GoalPriorityLogicTests.cs
    FlockStateTransitionTests.cs      ← ✨ 14 tests of FlockStateResolver priority tree
    ShannonEntropyTests.cs            ← ✨ 8 tests of entropy math
    ArcApproachDirectionTests.cs
    LeaderLeashTests.cs
    ReproducibilityTests.cs
    AutomatedPlayerLogicTests.cs
    FlankingSlotCalculatorTests.cs
    ScatterDirectionPickerTests.cs
  PlayMode/
    GOAPVisualPlayTests.cs            ← ✨ 9 goal scenarios for GOAPBoid; observe-any-frame assertion
    PureBoidsVisualPlayTests.cs       ← ✨ 6 state scenarios for PureBOIDS flock machine
    BatchRunnerSmokeTest.cs           ← ✨ end-to-end batch smoke test
    ArcApproachPlayModeTests.cs
    AutomatedPlayerPlayModeTests.cs
    GOAPBoidAgentTests.cs
    FlockSeparationTests.cs
    SpawningTests.cs
    PlayModeTests.asmdef              ← ✨ now references CrashKonijn.* assemblies
```

---

## What Was Done This Session

### 1. Condition 4 flock-manager parity (GOAPBoidFlockManager)
PureBOIDS-flavoured glue for `GOAPWithBOIDSMovement`, additive to the per-agent GOAP brains:
- **Pooled HP** (mirrors `FlockManager.SyncBoidCountToHealth`)
- **Centroid cache** (updated in LateUpdate)
- **Centroid leash** force read by `GOAPBoidAgent.ComputeBoidsForces`
- **Ranged phases** Forming → Locked → Firing → Recovering with formation-ring slots (agents fire from their slot — preserves GOAP's per-agent fire step)
- **Melee wave** WindUp → Charging → Recovering (melee action snaps to Charge when the wave hits)

### 2. PureBOIDS behavioral parity
`FlockManager.FlockState` expanded from 3 states to 9. New states fire via `FlockStateResolver` (pure function mirroring `GoalPriorityResolver` priority tree). Gated on `!UsesGoapForBoids` so condition 3 is unaffected. `BoidAgent.UpdateBoid` branches per state: Fleeing/Kiting reverse target seek; Scattering disables cohesion and injects outward force; Flanking rotates target direction by ±60°; Guarding holds at `guardInnerRange`; Regrouping boosts cohesion. Flee/Kite bump max speed by `fleeSpeedMultiplier`; Scatter by `scatterSpeedMultiplier`.

### 3. Tuning parity across conditions 2/3/4
`ConditionManager.comparisonHPPerFlock` (default 3000) is applied via `FlockManager.ApplyComparisonOverrides(flockSize, maxHealth)` at spawn. All three conditions now spawn the same headcount (split by `rangedAgentRatio`) and share the same HP pool.

### 4. Experiment pipeline overhaul
- **End-of-trial outcomes**: `PlayerDeath` / `FlockWiped` / `TimeLimit`. `PlayerHealth.IsDead` persists (removed auto-reset on death). `ConditionManager.AllFlocksDead` unified check across both flock-manager types.
- **Reaction-time metric**: first-stimulus → first-active-response per flock, ms. Stimulus fires on `FlockManager.SetTarget` / `GOAPBoidFlockManager.NotifyTargetAcquired` (first null→non-null). `ExperimentRunner.ForceStimulusOnAllFlocks()` guarantees a deterministic stimulus timestamp inside the recording window. Ordering bug fixed — response only counts after stimulus for that flock.
- **Goal entropy (Shannon)**: over 9-bucket distribution, computed at `StopRecording`. Inner math is a pure static `BehavioralMetricsCollector.ShannonEntropy(long[])` for unit-testability.
- **Per-agent CPU**: `cpuTimeMsPerAgent = cpuTimeMs / agentCount` column added to performance CSV + trial aggregates.
- **Multi-run batch**: `ExperimentRunner` loops runs × conditions × **agent-count sweep** and appends to unified batch CSVs with `RunId / Seed / AgentCount` columns. Per-(size, condition) μ±σ text summary printed at end.
- **Deterministic unique seeds**: `seed = baseSeed + sizeIdx * runsPerCondition + run`. EventLog / GoalDistribution CSVs stay joinable by seed alone.

### 5. Leader reaction-time bug fixed
`LeaderGoapBrain.LogEvent` was hardcoding `flockId = 0` for every leader → ranged-leader events were attributed to melee → `ReactionRangedMs` always `-1`. Now reads `boid.settings.flockType`.

### 6. Visual test harness
- `GOAPVisualPlayTests` — 9 PlayMode tests, one per GOAP goal, each runs 5s at real time and observes goals every frame.
- `PureBoidsVisualPlayTests` — 6 tests, one per new PureBOIDS flock state.
- `BatchRunnerSmokeTest` — end-to-end validation of the batch pipeline.
- `GoalLabelOverlay.cs` — floating goal label above each `GOAPBoidAgent`, usable in any scene.
- **Editor menu**: `Thesis → Run Batch Experiment` / `Thesis → Stop Batch`.
- **CLI hook**: `ExperimentBatchCli.RunBatchFromCommandLine` callable via `Unity.exe -executeMethod`.

---

## Current batch defaults

`ExperimentRunner` (Inspector):
- `Experiment Duration`: 60s max per trial
- `Runs Per Condition`: 10
- `Base Seed`: 42
- `Transition Delay`: 2s
- `Conditions To Test`: `[PureBOIDS, BOIDSWithGOAPLeader, GOAPWithBOIDSMovement]`
- `Agent Counts To Test`: `[100]` (set to e.g. `[50, 100, 200]` for scaling sweep)
- `Comparison HP Per Flock`: 3000

`AutomatedPlayer` ability damage (halved from original for longer observation):
- Light 7 · Heavy 22 · AOE Burst 11 (range 5u) · Ranged Throw 10

---

## Still TODO
- **Analysis notebook** (Python/Jupyter) reading the 4 batch CSVs → mean-metric bar chart, entropy box plot, reaction-time histogram, scaling curves. ~50 lines of pandas+matplotlib.
- **Scaling sweep at larger counts** (`[100, 200, 400, 800]`) to find where FPS diverges between architectures — currently no differentiation at ~100.
- **Per-system CPU breakdown** via existing `Profiler.BeginSample` markers (BOIDS forces vs GOAP planning vs physics). Infrastructure exists; aggregation script needed.
- **Auto-screenshot at key events** (first scatter, first regroup) for thesis figures.

---

## Running the pipeline

### Batch experiments
1. Open the scene `Assets/Scenes/New Scene.unity`.
2. Top menu: **Thesis → Run Batch Experiment** (or right-click ExperimentRunner → Run All Experiments).
3. Wait for "Batch complete" in Console. CSVs on Desktop:
   - `TrialSummary_batch_{ts}.csv` ← thesis-facing
   - `Performance_batch_{ts}.csv`
   - `GoalDistribution_batch_{ts}.csv`
   - `EventLog_batch_{ts}.csv`

### Tests
Open **Window → General → Test Runner**.
- **EditMode** tab: all tests runnable headless; ~1s total.
- **PlayMode** tab: each test runs 5s real-time; requires `Assets/Scenes/New Scene.unity` in Build Settings.

### CLI headless batch (when Unity Editor is closed)
```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe" `
   -projectPath "C:\Users\cliff\Desktop\Thesis\AI_Game_demo\My project" `
   -executeMethod ExperimentBatchCli.RunBatchFromCommandLine `
   -logFile "$env:USERPROFILE\Desktop\unity-batch.log"
```

---

## User Preferences
- **Never edit TagManager.asset, project layers, or tags directly.** Instruct user to do it in Unity UI.
- For anything easily configurable in Inspector or Project Settings UI, instruct user to make the change manually.
