# AI Game Demo — Workstation Handoff
**Last updated:** 2026-04-22 | **Branch:** main | **Commit:** e952070

---

## Project
Unity 6000.3.8f1 thesis project comparing 4 AI agent architectures in a shared combat scene.
CrashKonijn GOAP v3.1.2 + custom BOIDS implementation.

### 4 Conditions
| # | Name | Description |
|---|------|-------------|
| 1 | PureGOAP | GOAP-only agents (PureGOAPAgent, PureGOAPBrain) |
| 2 | PureBOIDS | BOIDS flocking only (BoidAgent, FlockManager) |
| 3 | BOIDSWithGOAPLeader | One GOAP leader per flock, followers are BOIDS |
| 4 | GOAPWithBOIDSMovement | Every agent has GOAP brain + BOIDS movement forces |

---

## Key Files
```
My project/Assets/Scripts/
  Conditions/
    ConditionManager.cs            ← spawns all 4 conditions; fixed seed (default 42)
    BOIDSWithGOAPLeader/
      LeaderGoapBrain.cs           ← leader GOAP brain + leash fix (maxLeaderSeparation=12)
    GOAPWithBOIDSMovement/
      GOAPBoidAgent.cs             ← IEnemy impl, attack slots, AllAgents static list
      GOAPBoidBrain.cs             ← goal selection
      Actions/
        GOAPBoidAttackAction.cs    ← melee; arc approach fix (PreferredApproachDir)
        GOAPBoidRangedAttackAction.cs ← ranged; same arc fix, orbits from unique angle
        GOAPBoidKiteAction.cs
    Shared/
      GoalPriorityResolver.cs
  Metrics/
    AutomatedPlayer.cs             ← hack-and-slash bot (7 phases, 5 abilities, seeded)
  PlayerHealth.cs                  ← IsInvincible flag for dodge i-frames

My project/Assets/Tests/
  EditMode/
    ArcApproachDirectionTests.cs   ← 7 tests: arc direction math
    LeaderLeashTests.cs            ← 8 tests: leash velocity scalar
    ReproducibilityTests.cs        ← 5 tests: seed reproducibility
    AutomatedPlayerLogicTests.cs   ← 9 tests: phases, cone, i-frames, orbit
  PlayMode/
    ArcApproachPlayModeTests.cs    ← 6 tests: unique dirs, spread targets, attack slots
    AutomatedPlayerPlayModeTests.cs← 9 tests: i-frames, wander start, combat, flags
```

---

## What Was Done This Session
1. **Arc attack approach** — GOAPWithBOIDSMovement agents no longer pile up on player center.
   Each agent computes `PreferredApproachDir = normalize(agentPos - playerPos, XZ)` at action
   start and steers to `playerPos + dir * contactDistance` instead.

2. **Leader leash** — BOIDSWithGOAPLeader leader velocity scaled down proportionally when it
   drifts more than `maxLeaderSeparation` (12u) from flock centroid. Applied in `LeaderGoapBrain.Update()`.

3. **Fixed random seed** — `ConditionManager.SpawnAgentsForCondition()` calls
   `Random.InitState(42)` before spawning. Toggle `useFixedSeed` in Inspector.

4. **AutomatedPlayer rewrite** — Full hack-and-slash bot:
   - 7 weighted phases: Wander(10%), Approach(25%), CircleStrafe(25%), HitAndRun(20%),
     StandAndFight(12%), Kite(8%), Retreat (auto at <35% HP)
   - 5 abilities: LightAttack (cone, 0.55s CD), HeavyAttack (AOE wind-up 1.2s, 4s CD),
     AOEBurst (360°, 12s CD), RangedThrow (8s CD), DodgeRoll (i-frames, 3s CD)
   - FindObjectsByType cached every 0.5s (not per-frame)
   - All Random calls only at phase transitions, not per-frame

5. **6 new test files** (EditMode + PlayMode) added and passing.

---

## Still TODO (not coded yet)
- **Agent size match** — Unity Editor only: open `goapWithBOIDSMovementPrefab`, match
  `Transform.localScale` to `pureGOAPAgentPrefab`.
- **Separation radius** — Raise `separationRadius` 4 → 7 on GOAPBoidMovement prefab in Inspector.
- **Scene wiring check** — Verify `New Scene.unity` has all 4 conditions wired to ConditionManager
  (prefab slots, GoapBehaviour components, Player tag on player GO).
- **PureGOAP goal parity** — PureGOAPBrain only has 3 goals (Flee/Attack/Wander) vs 8 in
  conditions 3 & 4. Decide if intentional or needs expanding before thesis data collection.

---

## Running Tests
Open Unity → Window → General → Test Runner
- EditMode tab: run `ArcApproachDirectionTests`, `LeaderLeashTests`, `ReproducibilityTests`, `AutomatedPlayerLogicTests`
- PlayMode tab: run `ArcApproachPlayModeTests`, `AutomatedPlayerPlayModeTests`

## Reproducing a Run
1. In Inspector on ConditionManager: `useFixedSeed = true`, `randomSeed = 42`
2. Press Play → ExperimentRunner cycles through all 4 conditions automatically
3. CSV output written by BehavioralMetricsCollector
