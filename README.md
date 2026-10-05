# GOAP vs. BOIDS: Multi-Agent Combat AI

M.S. thesis project, Loyola Marymount University (defended May 2026). Advisor: Prof. Ray Toal.

**Paper:** [Comparing Goal-Oriented Action Planning and Flocking Behavior for Multi-Agent Combat AI](https://drive.google.com/file/d/16Uubguw6Pl_hGaOe-cdDSrqjBcLYs7BK/view?usp=sharing)

## What this is

A Unity hack-and-slash benchmark that pits enemy swarms against an automated player to answer one question: how should large groups of enemies make decisions? Planning (GOAP) produces smart individual choices but costs CPU per agent. Flocking (BOIDS) scales cheaply but has no real decision making. This project measures where each approach wins and whether a hybrid gets the best of both.

Three architectures are compared, and all of them express the same nine behaviors (wander, attack, ranged attack, flee, scatter, kite, flank, guard, regroup):

| Architecture | How decisions are made |
| --- | --- |
| Pure BOIDS | A flock-level state machine drives emergent swarm movement. No planning. |
| GOAP-directed BOIDS | One GOAP leader plans for each flock; followers flock around it. |
| Per-agent GOAP | Every agent runs its own GOAP planner, with BOIDS steering layered on top. |

Agent count, pooled HP, and player damage are matched across conditions so the architecture is the only variable.

## Results

- 1,080 seeded trials across six enemy counts (25 to 800) and two modes: **Combat** (normal fights) and **Stress** (invulnerable agents, so the agent count stays fixed for clean performance numbers).
- Measured frame rate, CPU time per frame, memory, reaction time, goal variety, and fight outcomes, reported with medians and IQR.
- **GOAP-directed BOIDS was the most decisive in combat while staying within 10% of the cheapest architecture's per-frame CPU cost.** A single planner per flock gave most of the tactical benefit of planning at close to flocking prices.

## Project layout

```
My project/Assets/Scripts/
  Boids/        Custom BOIDS steering and flock state machine
  GOAP/         Shared GOAP setup (built on CrashKonijn GOAP)
  Conditions/   One folder per architecture, plus shared goal priority logic
  Metrics/      Batch experiment runner, performance profiler, behavior metrics, automated player
analysis/       Python notebook that turns the batch CSVs into the thesis figures and tables
```

## Running it

1. Open the `My project` folder in **Unity 6000.3.8f1** (HDRP). The CrashKonijn GOAP package (v3.1.2) installs automatically through the Package Manager.
2. Open `Assets/Scenes/New Scene.unity` and press Play to watch a single fight.
3. To reproduce the data, use **Thesis → Run Batch Experiment**. It writes CSVs to the Desktop.
4. See [`analysis/README.md`](analysis/README.md) to generate the figures from those CSVs.

## Built with

Unity 6 (C#), [CrashKonijn GOAP](https://github.com/crashkonijn/GOAP), Python (pandas, Jupyter) for analysis.
