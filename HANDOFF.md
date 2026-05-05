# AI Game Demo — Workstation Handoff
**Last updated:** 2026-05-04 | **Branch:** main

> **For canonical implementation state**, see [`wiki/thesis-implementation-status.md`](../wiki/thesis-implementation-status.md) in the parent Thesis-Brain repo. This file is for the most recent workstation handoff; the wiki page is the living source of truth for the project as a whole.

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

## Recent updates (2026-05-04 session)

### Combat-mode trial duration tuning
The 2026-04-25 follower-mimic-leader fix made BOIDSWithGOAPLeader followers cluster tightly around the leader at melee range, where AOE chewed through the pooled HP super-linearly with cluster density. Trials wiped in ~10 s at N=200. Fix: AutomatedPlayer attacks now cap targets per swing (Light=3, Heavy=6, AOE=8 closest) modelling physical swing reach, plus AOE damage 11 → 9. Drain rate is now N-independent. Verified live: N=200 Leader runs 60 s with 109/200 alive; N=800 Leader runs 60 s with 407/800 alive.

### 20-agent floor mystery resolved
The 2026-04-26 batch's flat-20-across-N artifact was an `ApplyComparisonOverrides` race already fixed in the 2026-04-25 round; today's diagnostic confirms `originalFlockSize` correctly scales with configured N. See [`wiki/methodology-revisions-2026-04.md`](../wiki/methodology-revisions-2026-04.md) Item 3a result for the full write-up.

### `ConditionManager.OnValidate` clamp removed
The Inspector field `agentCount` was silently clamped to ≤ 100. Now floors at 1, no upper limit. Direct edits to 800 etc. now stick. Note: the runner's `agentCountsToTest` array drives batch sizes; `ConditionManager.agentCount` is only used by the "Spawn Agents for Current Condition" context menu.

### Diagnostic instrumentation (off by default)
`FlockManager.DiagnosticLogging` (static bool) + `Thesis → Toggle Floor Diagnostic` Editor menu. When enabled, logs spawn sizes, every targetCount change, and KillAllBoids triggers. Useful for future HP/cull investigations.

### `ExperimentRunner` sweep echo
First console line on batch start now prints the resolved sweep config (`modes/sizes/conditions/runsPerCondition`) so a stale Inspector value or wrong-field edit is obvious from the first line of output.

---

## Final-batch preflight checklist (desktop, ~10 minutes before launch)

Run this list before kicking off the overnight 1,080-trial batch. The methodology is committed; the only thing left is making the Inspector match.

### 1. Pull the latest code
```powershell
cd <Thesis-Brain root>
git pull
git submodule update --init --recursive
```

Verify `git log -1 --oneline` inside `AI_Game_demo` shows the latest 2026-05-04 commits (AOE hit caps, diagnostic instrumentation, OnValidate fix).

### 2. Open the project in Unity 6000.3.8f1
Open scene **`Assets/Scenes/New Scene.unity`**. Confirm no compile errors in the console.

### 3. Set Inspector fields on the **`ExperimentRunner`** component

| Field | Final-batch value |
|---|---|
| `Experiment Duration` | `60` |
| `Stimulus Warmup Sec` | `5` |
| `Runs Per Condition` | `30` |
| `Base Seed` | `42` |
| `Transition Delay` | `0.5` |
| `Conditions To Test` | `[PureBOIDS, BOIDSWithGOAPLeader, GOAPWithBOIDSMovement]` (size 3) |
| `Agent Counts To Test` | `[25, 50, 100, 200, 400, 800]` (size 6) |
| `Modes To Test` | `[Stress, Combat]` (size 2) |

Total trials: 6 sizes × 3 conditions × 30 runs × 2 modes = **1,080**. Estimate: ~18 h overnight at 60 s/trial average.

**Do NOT edit the `agentCount` field on `ConditionManager` — it is only consulted by the "Spawn Agents for Current Condition" context menu, not by the batch.**

### 4. Set `obstacleMask` on three assets (currently warns on every spawn)
- `Assets/MeleeBoidSettings.asset` — set `obstacleMask` to the environment-collider layer.
- `Assets/RangedBoidSettings.asset` — same.
- The `GOAPBoidAgent` prefab — its `obstacleMask` field on the `GOAPBoidAgent` component, same layer.

Console warning: `BoidSettings 'X' has obstacleMask=0; obstacle avoidance disabled — boids will phase through geometry.` Fixing this lets boids respect environment colliders, which matters for spatial dynamics in the larger flocks.

### 5. **Save the scene (Ctrl+S).**
Inspector edits aren't persisted into `New Scene.unity` until you save. The `Thesis → Run Batch Experiment` menu reopens the scene from disk if needed, so unsaved edits would be lost.

### 6. Run EditMode tests as a sanity check
**Window → General → Test Runner → EditMode tab → Run All.** ~1 s total. All should pass; today's changes were additive. If `BoidSettingsAssetTests` fails on `AllBoidSettingsAssets_HaveNonZeroObstacleMask`, you missed step 4.

### 7. Run the smoke test
Either:
- **PlayMode test runner**: run `BatchRunnerSmokeTest` (5 s).
- **Manual mini-batch**: temporarily set `Agent Counts To Test = [100]`, `Runs Per Condition = 1`, run via menu. Verify all four CSVs land in `ExperimentResults/` with the `BenchmarkMode` column populated and the four combat metrics (`TotalDamageToPlayer`, `DamagePerSecondToPlayer`, `FirstHitMs`, `AgentsKilledByPlayer`) present and non-empty.
- **Restore the final-batch values from step 3 afterwards.**

### 8. Verify Stress mode actually suppresses damage
In a single Stress-mode trial, `AgentsKilledByPlayer` should be **0** and `TotalDamageToPlayer` should be **0** (the invulnerability gates suppress all damage). If non-zero, the `ConditionManager.SetStressMode` toggle didn't propagate to a flock manager — file a bug before the batch.

### 9. Launch the final batch
**Thesis → Run Batch Experiment** from the menu. Watch the first console line — should read something like:

```
[ExperimentRunner] Sweep: modes=[Stress,Combat] sizes=[25,50,100,200,400,800] conditions=[PureBOIDS,BOIDSWithGOAPLeader,GOAPWithBOIDSMovement] runsPerCondition=30
```

If `sizes` or `modes` doesn't match the table in step 3, you forgot to save the scene or edited the wrong field. Stop the batch (Thesis → Stop Batch) and re-check.

Walk away. CSVs land in `My project/ExperimentResults/`. The summary line will print to console when done.

---

## Deferred (post-batch / future work)
- **Per-system CPU breakdown** — `Profiler.BeginSample` markers are in place; aggregation script over `Performance_batch_*.csv` not yet written.
- **Auto-screenshot at key events** (first scatter, first regroup) for thesis figures.
- **Per-flock live-count breakdown** in the CSV (currently aggregated across both flocks).
- **Multi-swarm benchmark dimension** — sweep `swarmsPerType ∈ {1, 2, 4}` at fixed N. Tracked in `wiki/todos.md` Someday section. Earliest realistic window: post-2026-05-09.
- **Perception-radius centralization** — `playerDetectionRange = 30 m` in GOAP brains vs `15 m` aggro in `FlockManager`. Probably immaterial at the configured Ns; document as a parity caveat in the thesis methodology chapter.

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
