# Comprehensive Guide to Building GOAP Agents with crashkonijn/GOAP (v3.x)

> A deep-dive reference for implementing Goal-Oriented Action Planning agents in Unity using the [crashkonijn/GOAP](https://github.com/crashkonijn/GOAP) library. Written for academic and practical use.

---

## Table of Contents

1. [Introduction to GOAP](#1-introduction-to-goap)
2. [Core Concepts](#2-core-concepts)
3. [Architecture Overview](#3-architecture-overview)
4. [AgentTypes & Capabilities](#4-agenttypes--capabilities)
5. [Installation & Setup](#5-installation--setup)
6. [Configuration Approaches](#6-configuration-approaches)
7. [Building Your First Agent](#7-building-your-first-agent)
8. [Sensors Deep Dive](#8-sensors-deep-dive)
9. [Actions Deep Dive](#9-actions-deep-dive)
10. [Goals Deep Dive](#10-goals-deep-dive)
11. [Conditions & Effects System](#11-conditions--effects-system)
12. [Controllers](#12-controllers)
13. [Dependency Injection](#13-dependency-injection)
14. [Debugging & Visualization](#14-debugging--visualization)
15. [Practical Examples](#15-practical-examples)
16. [Performance Considerations](#16-performance-considerations)
17. [Best Practices & Common Pitfalls](#17-best-practices--common-pitfalls)
18. [GOAP vs Other AI Systems](#18-goap-vs-other-ai-systems)
19. [Resources & References](#19-resources--references)

---

## 1. Introduction to GOAP

### What is GOAP?

**Goal-Oriented Action Planning (GOAP)** is an AI architecture originally developed by Jeff Orkin for the game *F.E.A.R.* (2005). Unlike traditional state machines or behavior trees that hardcode transitions between behaviors, GOAP lets agents dynamically **plan** sequences of actions to achieve goals based on the current world state.

In GOAP, you define:
- **Goals** — desired world states the agent wants to achieve
- **Actions** — discrete behaviors with preconditions and effects
- **World State** — a set of key-value pairs representing the current state of the world

At runtime, a **planner** (resolver) searches backward from a goal through the action library, chaining actions whose effects satisfy other actions' preconditions, until it finds a viable sequence starting from the current world state. This produces **emergent behavior** — agents create novel action sequences without the developer explicitly authoring every possible transition.

### How GOAP Differs from Other AI Approaches

| Aspect | FSM | Behavior Tree | GOAP |
|--------|-----|---------------|------|
| **Transition authoring** | Every state-to-state transition manually defined | Tree structure manually authored | Planner discovers valid action chains automatically |
| **Scalability** | O(n^2) transitions as states grow | Deep trees become unwieldy | Adding actions linearly expands possibilities |
| **Emergent behavior** | None — only pre-authored paths | Limited — follows tree structure | High — novel plans emerge from action composition |
| **Adaptability** | Must anticipate every scenario | Must anticipate branch conditions | Replans dynamically when world state changes |
| **Debugging** | Easy — follow state transitions | Moderate — trace tree traversal | Requires graph visualization tools |

### Why GOAP Produces Emergent Behavior

Because the planner discovers action chains at runtime, adding a single new action can create entirely new behavioral possibilities across all goals. For example, adding a "pick lock" action automatically enables agents to plan paths through locked doors — without any explicit transition authoring. This combinatorial expansion is what makes GOAP powerful for complex game AI.

---

## 2. Core Concepts

### WorldKeys

**WorldKeys** are marker types that represent integer-based world state values. They act as identifiers — sensors provide the actual values at runtime.

```csharp
using CrashKonijn.Goap.Runtime;

public class IsIdle : WorldKeyBase { }
public class PearCount : WorldKeyBase { }
public class Hunger : WorldKeyBase { }
public class HasWeapon : WorldKeyBase { }
```

WorldKeys are used in conditions and effects to connect goals and actions in the planning graph. They contain no data themselves.

### TargetKeys

**TargetKeys** are marker types that represent `Vector3`-based positional references. They tell actions *where* to go.

```csharp
using CrashKonijn.Goap.Runtime;

public class IdleTarget : TargetKeyBase { }
public class ClosestPear : TargetKeyBase { }
public class ClosestEnemy : TargetKeyBase { }
```

Target sensors resolve these keys to actual positions or transforms at runtime.

### Goals

Goals define desired world states via conditions on WorldKeys. The planner works backward from goals to find action chains. A goal like "Hunger <= 0" tells the planner to find actions whose effects can reduce the Hunger WorldKey to zero.

### Actions

Actions are the discrete behaviors agents can perform. Each action declares:
- **Preconditions** — WorldKey conditions that must be true before the action can execute
- **Effects** — WorldKey changes the action claims to produce (used for planning only)
- **Target** — a TargetKey indicating where the agent needs to be
- **Cost** — how expensive the action is (the planner minimizes total plan cost)

### Sensors

Sensors feed data into the world state. They come in four varieties:
- **World Sensors** — provide integer values for WorldKeys
- **Target Sensors** — provide positions/transforms for TargetKeys
- **Local Sensors** — per-agent data (e.g., "my hunger level")
- **Global Sensors** — shared data (e.g., "time of day")

### Conditions & Effects

**Conditions** are requirements on WorldKeys using comparison operators (`GreaterThan`, `SmallerThan`, etc.). **Effects** declare whether an action increases or decreases a WorldKey. The planner uses these declarations to build a graph connecting goals to actions.

**Critical**: Effects are declarative for graph-building only. The system does **not** automatically modify world state. You must implement actual state changes in your action code.

### SenseValue

WorldSensors return `SenseValue`, a struct wrapping `int` with implicit conversions:

```csharp
public struct SenseValue
{
    public SenseValue(int value);
    public SenseValue(bool value);  // true = 1, false = 0
    public static implicit operator int(SenseValue v);
    public static implicit operator SenseValue(int v);
    public static implicit operator SenseValue(bool v);
}
```

---

## 3. Architecture Overview

### System Components

```
┌─────────────────────────────────────────────────────────┐
│                    GoapBehaviour                         │
│  (Central manager — one per scene)                      │
│  ┌───────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │  Controller    │  │  AgentTypes  │  │  Graph       │ │
│  │  (Reactive/    │  │  (behavior   │  │  Builder     │ │
│  │   Proactive/   │  │   libraries) │  │  (connects   │ │
│  │   Manual)      │  │              │  │   nodes)     │ │
│  └───────────────┘  └──────────────┘  └──────────────┘ │
└─────────────┬───────────────────────────────────────────┘
              │ manages
              ▼
┌─────────────────────────────────────────────────────────┐
│              Per-Agent Components                        │
│  ┌───────────────────┐  ┌────────────────────────────┐  │
│  │  AgentBehaviour    │  │  GoapActionProvider        │  │
│  │  (executes actions,│  │  (goal requests, resolver  │  │
│  │   handles movement)│  │   interface, action state) │  │
│  └───────────────────┘  └────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
              │ uses
              ▼
┌─────────────────────────────────────────────────────────┐
│  Resolver (A* / Job System)                             │
│  - Searches backward from goal                          │
│  - Chains actions via condition/effect matching          │
│  - Finds lowest-cost action path                        │
│  - Multi-threaded via Unity Job System                  │
└─────────────────────────────────────────────────────────┘
```

### GoapBehaviour

The central manager component. One per scene. Holds all AgentType configurations and the controller that drives the planning loop.

### AgentBehaviour

Attached to each agent GameObject. Executes the currently assigned action, handles movement toward action targets, and fires events for action lifecycle (start, stop, complete, target in/out of range).

### GoapActionProvider

Also attached to each agent. Interfaces between the agent and the GOAP system. Handles:
- Goal requests (what the agent wants to achieve)
- Resolver interface (triggers planning)
- Action assignment (receives planned actions from the resolver)
- Action enable/disable

### Controllers

Controllers determine **when** sensors run and **when** the resolver plans:

| Controller | Behavior | Best For |
|-----------|----------|----------|
| **ReactiveController** | Runs sensors/resolver only when agents need new actions | Agents reacting to immediate needs |
| **ProactiveController** | Runs sensors/resolver periodically regardless of need | Rapidly changing environments |
| **ManualController** | You trigger sensors/resolver explicitly | Precision-critical scenarios |

### Graph Builder

At initialization, the Graph Builder connects Goals, Actions, Conditions, and Effects based on WorldKey relationships. This creates the planning graph that the Resolver searches at runtime.

### Resolver

The Resolver uses an A*-based algorithm (running on Unity's Job System for multi-threading) to search the planning graph backward from the requested goal, finding the lowest-cost sequence of actions whose effects chain together to satisfy all conditions.

---

## 4. AgentTypes & Capabilities

### AgentTypes

An **AgentType** groups goals, actions, and sensors into a shared behavior library. All agents of the same type share the same class instances — this is why actions and goals must be **stateless**.

```csharp
public interface IAgentType
{
    string Id { get; }
    IGoapConfig GoapConfig { get; }
    IAgentCollection Agents { get; }
    ISensorRunner SensorRunner { get; }
    IAgentTypeEvents Events { get; }
    IGlobalWorldData WorldData { get; }
    void Register(IMonoGoapActionProvider actionProvider);
    void Unregister(IMonoGoapActionProvider actionProvider);
    List<IConnectable> GetAllNodes();
    List<IGoapAction> GetActions();
    List<IGoal> GetGoals();
    TGoal ResolveGoal<TGoal>() where TGoal : IGoal;
}
```

### Capabilities (Modular Composition)

Capabilities are reusable modules of goals + actions + sensors. An AgentType is composed of one or more Capabilities. This enables modular behavior composition:

```
AgentType: "Villager"
├── Capability: "IdleBehavior"      (IdleGoal, IdleAction, IdleTargetSensor)
├── Capability: "GatheringBehavior" (GatherGoal, PickupAction, GatherSensor)
└── Capability: "HungerBehavior"    (EatGoal, EatAction, HungerSensor)
```

You can reuse capabilities across different AgentTypes:

```
AgentType: "Guard"
├── Capability: "IdleBehavior"      (same as Villager)
├── Capability: "PatrolBehavior"    (PatrolGoal, PatrolAction, PatrolSensor)
└── Capability: "CombatBehavior"    (AttackGoal, AttackAction, EnemySensor)
```

### CapabilityFactoryBase (Code-Based)

```csharp
public class IdleCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("IdleCapability");

        builder.AddGoal<IdleGoal>()
            .AddCondition<IsIdle>(Comparison.GreaterThanOrEqual, 1)
            .SetBaseCost(2);

        builder.AddAction<IdleAction>()
            .AddEffect<IsIdle>(EffectType.Increase)
            .SetTarget<IdleTarget>();

        builder.AddTargetSensor<IdleTargetSensor>()
            .SetTarget<IdleTarget>();

        return builder.Build();
    }
}
```

### AgentTypeFactoryBase (Code-Based)

```csharp
public class VillagerAgentTypeFactory : AgentTypeFactoryBase
{
    public override IAgentTypeConfig Create()
    {
        var builder = new AgentTypeBuilder("Villager");
        builder.AddCapability<IdleCapabilityFactory>();
        builder.AddCapability<GatheringCapabilityFactory>();
        builder.AddCapability<HungerCapabilityFactory>();
        return builder.Build();
    }
}
```

---

## 5. Installation & Setup

### Requirements

- **Unity 2022.2+** (2021.3 also confirmed working)

### Installation Methods

**Unity Package Manager (recommended):**
1. Open Window > Package Manager
2. Click "+" > "Add package from git URL..."
3. Enter: `https://github.com/crashkonijn/GOAP.git?path=/Package#3.1.2`

**OpenUPM:**
```
openupm add com.crashkonijn.goap
```

**Unity Asset Store:**
Search for "GOAP" by crashkonijn (Package slug: 252687)

### Package Structure (5 Assemblies)

| Assembly | Purpose |
|----------|---------|
| `CrashKonijn.Agent.Core` | Core agent interfaces and enums |
| `CrashKonijn.Agent.Runtime` | Agent runtime (AgentBehaviour, ActionRunner) |
| `CrashKonijn.Goap.Core` | GOAP interfaces, enums, SenseValue |
| `CrashKonijn.Goap.Resolver` | Multi-threaded planner/resolver |
| `CrashKonijn.Goap.Runtime` | GOAP runtime (actions, sensors, builders) |

### Initial Scene Configuration

1. **Create GOAP Manager:**
   - Create an empty GameObject named `GOAP`
   - Add `GoapBehaviour` component
   - Add a controller component (`ReactiveControllerBehaviour`, `ProactiveControllerBehaviour`, or `ManualControllerBehaviour`)

2. **Configure AgentType:**
   - Add your `AgentTypeFactory` component (code-based) or `AgentTypeBehaviour` (ScriptableObject-based) to the GOAP GameObject

3. **Setup Agent Prefab:**
   - Create a GameObject for the agent
   - Add `AgentBehaviour` component
   - Add `GoapActionProvider` component
   - Wire `AgentBehaviour.ActionProviderBase` to point to the `GoapActionProvider`
   - Assign the AgentType

---

## 6. Configuration Approaches

The library supports two configuration approaches for defining goals, actions, sensors, and their relationships.

### Code-Based Configuration

Define behavior in C# using the builder API. Configuration lives in `CapabilityFactoryBase` and `AgentTypeFactoryBase` subclasses.

```csharp
public class GatherCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("GatherCapability");

        // Goals
        builder.AddGoal<GatherGoal>()
            .AddCondition<ResourceCount>(Comparison.GreaterThanOrEqual, 5);

        // Actions
        builder.AddAction<PickupResourceAction>()
            .SetTarget<ClosestResource>()
            .AddCondition<ResourceVisible>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<ResourceCount>(EffectType.Increase)
            .SetBaseCost(1)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming);

        // Sensors
        builder.AddMultiSensor<ResourceSensor>();

        return builder.Build();
    }
}
```

**Pros:**
- Full IDE support (autocomplete, refactoring, compile-time checks)
- Easy version control (plain C# files)
- Testable in isolation
- No serialization issues
- Cleaner for complex configurations

**Cons:**
- Requires recompilation for changes
- Less visual for designers
- No inspector-based editing

### ScriptableObject-Based Configuration

Define behavior via Unity ScriptableObjects configured in the Inspector.

1. Create `AgentTypeScriptable` asset (Right-click > Create > GOAP > AgentTypeScriptable)
2. Create `CapabilityScriptable` assets for each capability
3. Use the **Generator** tool to create class stubs
4. Configure conditions, effects, sensors, and targets via Inspector fields
5. Use the **Check Issues** button for validation

**Pros:**
- Visual configuration in Unity Inspector
- Designer-friendly (no code required for tuning)
- Hot-reloading of parameter changes
- Quick iteration

**Cons:**
- Harder to version control (binary assets)
- Serialization edge cases
- Less refactoring support
- Must keep ScriptableObjects and code in sync

### Recommendation

For most projects — especially those with programmer-driven AI design — **code-based configuration** is recommended. It provides stronger guarantees, better tooling support, and easier maintenance. Use ScriptableObject-based configuration when non-programmers need to tune agent behavior.

---

## 7. Building Your First Agent

This walkthrough creates a simple agent that wanders to random positions and idles.

### Step 1: Define Keys

```csharp
// WorldKeys
public class IsIdle : WorldKeyBase { }

// TargetKeys
public class WanderTarget : TargetKeyBase { }
```

### Step 2: Create the Goal

```csharp
public class WanderGoal : GoalBase { }
```

### Step 3: Create the Action

```csharp
using CrashKonijn.Agent.Core;
using CrashKonijn.Goap.Runtime;

[GoapId("WanderAction-a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
public class WanderAction : GoapActionBase<WanderAction.Data>
{
    public override void Start(IMonoAgent agent, Data data)
    {
        data.Timer = Random.Range(2f, 5f);
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        data.Timer -= context.DeltaTime;

        if (data.Timer <= 0f)
            return ActionRunState.Completed;

        return ActionRunState.Continue;
    }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        public float Timer { get; set; }
    }
}
```

### Step 4: Create the Sensor

```csharp
using CrashKonijn.Goap.Runtime;
using UnityEngine;

[GoapId("WanderTargetSensor-11223344-5566-7788-99aa-bbccddeeff00")]
public class WanderTargetSensor : LocalTargetSensorBase
{
    private static readonly float WanderRadius = 10f;

    public override void Created() { }
    public override void Update() { }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        var randomOffset = Random.insideUnitSphere * WanderRadius;
        randomOffset.y = 0f;
        var position = agent.Transform.position + randomOffset;

        if (existingTarget is PositionTarget posTarget)
            return posTarget.SetPosition(position);

        return new PositionTarget(position);
    }
}
```

### Step 5: Create the Capability

```csharp
using CrashKonijn.Goap.Runtime;

public class WanderCapabilityFactory : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("WanderCapability");

        builder.AddGoal<WanderGoal>()
            .AddCondition<IsIdle>(Comparison.GreaterThanOrEqual, 1)
            .SetBaseCost(1);

        builder.AddAction<WanderAction>()
            .SetTarget<WanderTarget>()
            .AddEffect<IsIdle>(EffectType.Increase)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming)
            .SetStoppingDistance(0.5f);

        builder.AddTargetSensor<WanderTargetSensor>()
            .SetTarget<WanderTarget>();

        return builder.Build();
    }
}
```

### Step 6: Create the AgentType

```csharp
using CrashKonijn.Goap.Runtime;

public class SimpleAgentTypeFactory : AgentTypeFactoryBase
{
    public override IAgentTypeConfig Create()
    {
        var builder = new AgentTypeBuilder("SimpleAgent");
        builder.AddCapability<WanderCapabilityFactory>();
        return builder.Build();
    }
}
```

### Step 7: Create the Brain (Goal Selector)

The library does **not** handle goal selection. You must implement your own "brain" that requests goals:

```csharp
using CrashKonijn.Goap.Runtime;
using UnityEngine;

public class SimpleBrain : MonoBehaviour
{
    private GoapActionProvider provider;

    private void Awake()
    {
        provider = GetComponent<GoapActionProvider>();
    }

    private void Start()
    {
        provider.RequestGoal<WanderGoal>();
    }
}
```

### Step 8: Create the Movement Handler

The library does not handle movement. Subscribe to agent events:

```csharp
using CrashKonijn.Agent.Core;
using UnityEngine;

[RequireComponent(typeof(AgentBehaviour))]
public class AgentMoveBehaviour : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 3f;
    private AgentBehaviour agent;
    private ITarget currentTarget;
    private bool shouldMove;

    private void Awake()
    {
        agent = GetComponent<AgentBehaviour>();
    }

    private void OnEnable()
    {
        agent.Events.OnTargetInRange += OnTargetInRange;
        agent.Events.OnTargetChanged += OnTargetChanged;
        agent.Events.OnTargetNotInRange += OnTargetNotInRange;
        agent.Events.OnTargetLost += OnTargetLost;
    }

    private void OnDisable()
    {
        agent.Events.OnTargetInRange -= OnTargetInRange;
        agent.Events.OnTargetChanged -= OnTargetChanged;
        agent.Events.OnTargetNotInRange -= OnTargetNotInRange;
        agent.Events.OnTargetLost -= OnTargetLost;
    }

    private void OnTargetChanged(ITarget target, bool inRange)
    {
        currentTarget = target;
        shouldMove = !inRange;
    }

    private void OnTargetInRange(ITarget target) => shouldMove = false;
    private void OnTargetNotInRange(ITarget target) => shouldMove = true;
    private void OnTargetLost() { currentTarget = null; shouldMove = false; }

    private void Update()
    {
        if (!shouldMove || currentTarget == null) return;

        var targetPos = new Vector3(
            currentTarget.Position.x,
            transform.position.y,
            currentTarget.Position.z);

        transform.position = Vector3.MoveTowards(
            transform.position, targetPos, moveSpeed * Time.deltaTime);
    }
}
```

### Step 9: Scene Setup

1. Create a `GOAP` GameObject, add `GoapBehaviour` + `ReactiveControllerBehaviour` + `SimpleAgentTypeFactory`
2. Create an `Agent` prefab, add `AgentBehaviour` + `GoapActionProvider` + `SimpleBrain` + `AgentMoveBehaviour`
3. Wire `AgentBehaviour.ActionProviderBase` → `GoapActionProvider`
4. Instantiate and play

---

## 8. Sensors Deep Dive

### Sensor Type Matrix

|  | Local (per-agent) | Global (shared across agents) |
|---|---|---|
| **World** (int values) | `LocalWorldSensorBase` | `GlobalWorldSensorBase` |
| **Target** (positions) | `LocalTargetSensorBase` | `GlobalTargetSensorBase` |
| **Multi** (combined) | `MultiSensorBase` | — |

### LocalWorldSensorBase

Provides per-agent integer values for WorldKeys.

```csharp
public abstract class LocalWorldSensorBase : ILocalWorldSensor
{
    public IWorldKey Key { get; }
    public virtual ISensorTimer Timer => SensorTimer.Always;
    public abstract void Created();
    public abstract void Update();
    public abstract SenseValue Sense(IActionReceiver agent, IComponentReference references);
}
```

Example:
```csharp
[GoapId("HungerSensor-aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
public class HungerSensor : LocalWorldSensorBase
{
    public override void Created() { }
    public override void Update() { }

    public override SenseValue Sense(IActionReceiver agent, IComponentReference references)
    {
        var data = references.GetCachedComponent<DataBehaviour>();
        return (int)data.hunger;
    }
}
```

### GlobalWorldSensorBase

Provides shared integer values across all agents of an AgentType.

```csharp
public abstract class GlobalWorldSensorBase : IGlobalWorldSensor
{
    public IWorldKey Key { get; }
    public virtual ISensorTimer Timer => SensorTimer.Always;
    public abstract void Created();
    public abstract SenseValue Sense();
}
```

Example:
```csharp
[GoapId("TimeSensor-11111111-2222-3333-4444-555555555555")]
public class DaytimeSensor : GlobalWorldSensorBase
{
    public override void Created() { }

    public override SenseValue Sense()
    {
        // 1 if daytime, 0 if night
        return (GameManager.Instance.Hour >= 6 && GameManager.Instance.Hour < 20);
    }
}
```

### LocalTargetSensorBase

Provides per-agent positions for TargetKeys.

```csharp
public abstract class LocalTargetSensorBase : ILocalTargetSensor
{
    public ITargetKey Key { get; }
    public virtual ISensorTimer Timer => SensorTimer.Always;
    public abstract void Created();
    public abstract void Update();
    public abstract ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget);
}
```

Example:
```csharp
[GoapId("ClosestPearSensor-aabbccdd-1122-3344-5566-778899001122")]
public class ClosestPearSensor : LocalTargetSensorBase
{
    private PearBehaviour[] pears;

    public override void Created() { }

    public override void Update()
    {
        pears = Object.FindObjectsOfType<PearBehaviour>();
    }

    public override ITarget Sense(IActionReceiver agent, IComponentReference references, ITarget existingTarget)
    {
        PearBehaviour closest = null;
        float closestDist = float.MaxValue;

        foreach (var pear in pears)
        {
            float dist = Vector3.Distance(agent.Transform.position, pear.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = pear;
            }
        }

        if (closest == null) return null;

        // Reuse existing target to reduce GC pressure
        if (existingTarget is TransformTarget transformTarget)
            return transformTarget.SetTransform(closest.transform);

        return new TransformTarget(closest.transform);
    }
}
```

### GlobalTargetSensorBase

Provides shared positions across all agents. Less common — used when all agents should target the same position.

```csharp
[GoapId("BaseCampSensor-ffeeddcc-bbaa-9988-7766-554433221100")]
public class BaseCampSensor : GlobalTargetSensorBase
{
    public override void Created() { }

    public override ITarget Sense(ITarget existingTarget)
    {
        var camp = Object.FindObjectOfType<BaseCampBehaviour>();
        if (camp == null) return null;

        if (existingTarget is TransformTarget t)
            return t.SetTransform(camp.transform);

        return new TransformTarget(camp.transform);
    }
}
```

### MultiSensorBase

Bundles multiple sensor lambdas in a single class. Reduces boilerplate when multiple related sensors share data.

**Critical**: Sensor lambdas must be registered in the **constructor**, not in `Created()`.

```csharp
[GoapId("PearSensor-d68c875d-29c0-43f3-9d79-054d4cc6505d")]
public class PearSensor : MultiSensorBase
{
    private PearBehaviour[] pears;

    public PearSensor()
    {
        // Register in constructor — NOT in Created()
        AddLocalWorldSensor<PearCount>((agent, references) =>
        {
            var data = references.GetCachedComponent<DataBehaviour>();
            return data.pearCount;
        });

        AddLocalWorldSensor<Hunger>((agent, references) =>
        {
            var data = references.GetCachedComponent<DataBehaviour>();
            return (int)data.hunger;
        });

        AddLocalTargetSensor<ClosestPear>((agent, references, existingTarget) =>
        {
            var closest = Closest(pears, agent.Transform.position);
            if (closest == null) return null;
            if (existingTarget is TransformTarget t)
                return t.SetTransform(closest.transform);
            return new TransformTarget(closest.transform);
        });
    }

    public override void Created() { }

    public override void Update()
    {
        pears = Object.FindObjectsOfType<PearBehaviour>();
    }

    private PearBehaviour Closest(PearBehaviour[] items, Vector3 position)
    {
        PearBehaviour closest = null;
        float closestDist = float.MaxValue;
        foreach (var item in items)
        {
            float dist = Vector3.Distance(position, item.transform.position);
            if (dist < closestDist) { closestDist = dist; closest = item; }
        }
        return closest;
    }
}
```

### Sensor Timer

Controls how frequently sensors run:

```csharp
SensorTimer.Always              // Every frame (default)
SensorTimer.Once                // Only once at initialization
SensorTimer.Interval(0.5f)     // Every 0.5 seconds
```

Override the `Timer` property:
```csharp
public override ISensorTimer Timer => SensorTimer.Interval(0.5f);
```

### Target Types

| Type | Wraps | `IsValid()` | Use Case |
|------|-------|-------------|----------|
| `PositionTarget` | `Vector3` | Always `true` | Static positions, random wander points |
| `TransformTarget` | `Transform` | `Transform != null` | Tracking GameObjects (items, enemies) |

```csharp
// Creating targets
new PositionTarget(new Vector3(10, 0, 5));
new TransformTarget(someGameObject.transform);

// Reusing targets (fluent API)
positionTarget.SetPosition(newVector3);     // returns self
transformTarget.SetTransform(newTransform); // returns self
```

**Best Practice**: Always reuse existing target instances via the `existingTarget` parameter to minimize garbage collection.

---

## 9. Actions Deep Dive

### Stateless Design

Actions are **shared instances** across all agents of the same AgentType. This means:
- **Never store per-agent state as class fields**
- All per-agent data must go in the `Data` class (which implements `IActionData`)
- Each agent gets their own `Data` instance; the action class itself is shared

### GoapActionBase

The base class you extend for all actions:

```csharp
// Simple (no custom properties):
public abstract class GoapActionBase<TActionData>
    where TActionData : IActionData, new()

// With custom properties:
public abstract class GoapActionBase<TActionData, TActionProperties>
    where TActionData : IActionData, new()
    where TActionProperties : class, IActionProperties, new()
```

### Action Lifecycle

```
Created()          ← Called once when action instance is created
    │
Start()            ← Called when action is assigned to an agent
    │
BeforePerform()    ← Called once before the first Perform
    │
Perform()          ← Called every frame while action is active
    │                 Returns IActionRunState:
    │                   Continue        → keep performing
    │                   Completed       → triggers Complete()
    │                   Stop            → triggers Stop()
    │                   Wait(time)      → pause, then continue
    │                   WaitThenComplete(time) → pause, then complete
    │
Complete()         ← Action finished successfully
    │              OR
Stop()             ← Action was interrupted/stopped
```

### IActionRunState (Return Values)

```csharp
// Static instances
ActionRunState.Continue              // Keep performing (MayResolve = false)
ActionRunState.ContinueOrResolve     // Keep performing (MayResolve = true)
ActionRunState.Stop                  // Stop action → triggers Stop()
ActionRunState.Completed             // Complete action → triggers Complete()

// Factory methods
ActionRunState.Wait(float seconds, bool mayResolve = false)
ActionRunState.WaitThenComplete(float seconds, bool mayResolve = false)
ActionRunState.WaitThenStop(float seconds, bool mayResolve = false)
ActionRunState.StopAndLog(string message)
```

### ActionMoveMode

Configured in the builder, determines when the agent moves toward the target:

```csharp
ActionMoveMode.MoveBeforePerforming  // Move to target, then perform (most common)
ActionMoveMode.PerformWhileMoving    // Perform while moving to target
```

### Data Class & Component Injection

The `Data` class holds per-agent state. Use attributes for automatic component injection from the agent's GameObject:

```csharp
public class Data : IActionData
{
    public ITarget Target { get; set; }  // Required — set by the system

    // Auto-injected from agent GameObject
    [GetComponent]
    public DataBehaviour DataBehaviour { get; set; }

    [GetComponentInChildren]
    public Renderer AgentRenderer { get; set; }

    [GetComponentInParent]
    public TeamController TeamController { get; set; }

    // Custom per-agent state
    public float Timer { get; set; }
    public int RepeatCount { get; set; }
}
```

### ActionProperties

For configurable action parameters shared across all agents:

```csharp
public class ChopTreeAction : GoapActionBase<ChopTreeAction.Data, ChopTreeAction.Props>
{
    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        data.Timer -= context.DeltaTime;
        if (data.Timer <= 0f) return ActionRunState.Completed;
        return ActionRunState.Continue;
    }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.Timer = Properties.chopDuration;
    }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        public float Timer { get; set; }
    }

    [Serializable]
    public class Props : IActionProperties
    {
        public float chopDuration = 3f;
    }
}
```

Set properties in the builder:
```csharp
builder.AddAction<ChopTreeAction>()
    .SetProperties(new ChopTreeAction.Props { chopDuration = 5f });
```

### Overridable Methods

| Method | Purpose | Default |
|--------|---------|---------|
| `GetCost(agent, refs, target)` | Dynamic cost calculation | Returns config BaseCost |
| `GetStoppingDistance()` | Dynamic stopping distance | Returns config value |
| `IsInRange(agent, distance, data, refs)` | Custom range check | `distance <= StoppingDistance` |
| `IsValid(agent, data)` | Custom validity check | `true` |
| `IsEnabled(agent, refs)` | Custom enable/disable | `true` |

### Disabling/Enabling Actions at Runtime

```csharp
// Disable an action
provider.Disable<AttackAction>(new ForeverActionDisabler());  // permanent
provider.Disable<AttackAction>(new ForTimeActionDisabler(5f)); // for 5 seconds

// Re-enable
provider.Enable<AttackAction>();
```

### Complete Action Examples

**Timer-based idle:**
```csharp
[GoapId("Idle-ccc6f46c-1626-44aa-b90d-1b2741642166")]
public class IdleAction : GoapActionBase<IdleAction.Data>
{
    public override void Start(IMonoAgent agent, Data data)
    {
        data.Timer = Random.Range(0.5f, 1.5f);
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Timer <= 0f) return ActionRunState.Completed;
        data.Timer -= context.DeltaTime;
        return ActionRunState.Continue;
    }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        public float Timer { get; set; }
    }
}
```

**Pickup with destruction:**
```csharp
[GoapId("PickupPear-06ef21a4-059b-4314-800a-e7c2622637fb")]
public class PickupPearAction : GoapActionBase<PickupPearAction.Data>
{
    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        return ActionRunState.WaitThenComplete(0.5f);
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        if (data.Target is not TransformTarget transformTarget) return;
        data.DataBehaviour.pearCount++;
        GameObject.Destroy(transformTarget.Transform.gameObject);
    }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public DataBehaviour DataBehaviour { get; set; }
    }
}
```

**No-target action (eating):**
```csharp
[GoapId("Eat-b235695c-727b-41a5-aa66-4757ce65719d")]
public class EatAction : GoapActionBase<EatAction.Data>
{
    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        return ActionRunState.WaitThenComplete(5f);
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.DataBehaviour.pearCount--;
        data.DataBehaviour.hunger = 0f;
    }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }
        [GetComponent] public DataBehaviour DataBehaviour { get; set; }
    }
}
```

---

## 10. Goals Deep Dive

### Goal Structure

Goals are simple classes — all configuration is done via the builder:

```csharp
public class IdleGoal : GoalBase { }
public class GatherGoal : GoalBase { }
public class EatGoal : GoalBase { }
public class AttackGoal : GoalBase { }
```

### Goal Conditions

Goals define desired world states via conditions:

```csharp
builder.AddGoal<EatGoal>()
    .AddCondition<Hunger>(Comparison.SmallerThanOrEqual, 0);

builder.AddGoal<GatherGoal>()
    .AddCondition<ResourceCount>(Comparison.GreaterThanOrEqual, 5);

// Multiple conditions (all must be satisfiable)
builder.AddGoal<SurviveGoal>()
    .AddCondition<Hunger>(Comparison.SmallerThanOrEqual, 20)
    .AddCondition<Health>(Comparison.GreaterThanOrEqual, 50);
```

### BaseCost

BaseCost influences goal selection when multiple goals are requested simultaneously. Lower cost goals are preferred (all else being equal):

```csharp
builder.AddGoal<IdleGoal>()
    .AddCondition<IsIdle>(Comparison.GreaterThanOrEqual, 1)
    .SetBaseCost(10);  // Low priority fallback

builder.AddGoal<EatGoal>()
    .AddCondition<Hunger>(Comparison.SmallerThanOrEqual, 0)
    .SetBaseCost(1);   // High priority
```

### Dynamic Cost

Override `GetCost()` for context-sensitive goal priority:

```csharp
public class EatGoal : GoalBase
{
    public override float GetCost(IActionReceiver agent, IComponentReference references)
    {
        var data = references.GetCachedComponent<DataBehaviour>();
        // More urgent (lower cost) when hungrier
        return Mathf.Max(0, 10f - data.hunger);
    }
}
```

### Requesting Goals

Goals are requested through `GoapActionProvider`:

```csharp
// Request a single goal
provider.RequestGoal<WanderGoal>();

// Request multiple goals (resolver picks the cheapest achievable one)
provider.RequestGoal<IdleGoal, GatherGoal>();
provider.RequestGoal<IdleGoal, GatherGoal, EatGoal>();

// Request via type array
provider.RequestGoal(new[] { typeof(IdleGoal), typeof(GatherGoal) });

// Clear current goal
provider.ClearGoal();
```

When multiple goals are requested, the resolver evaluates all of them and selects the one with the lowest total plan cost (goal BaseCost + sum of action costs along the plan).

### Goal Selection Strategy

The library intentionally does **not** handle goal selection. You implement a "Brain" script using whatever decision-making approach fits your game:

```csharp
public class AgentBrain : MonoBehaviour
{
    private GoapActionProvider provider;
    private DataBehaviour data;

    private void Awake()
    {
        provider = GetComponent<GoapActionProvider>();
        data = GetComponent<DataBehaviour>();
    }

    private void Update()
    {
        // Simple priority-based selection
        if (data.hunger > 80f)
            provider.RequestGoal<EatGoal>();
        else if (data.pearCount < 3)
            provider.RequestGoal<GatherGoal>();
        else
            provider.RequestGoal<IdleGoal>();
    }
}
```

More sophisticated approaches:
- **Utility AI brain** — score each goal based on multiple factors
- **FSM brain** — state machine transitions between goal sets
- **Multi-goal request** — let the resolver pick the cheapest

---

## 11. Conditions & Effects System

### How the Planner Chains Actions

The planner builds a directed graph by matching action **effects** to goal/action **conditions** that share the same WorldKey. It then searches this graph backward from the goal using A*.

```
Goal: EatGoal
  Condition: Hunger <= 0
      │
      │ Matches WorldKey "Hunger" with EffectType.Decrease
      ▼
Action: EatAction
  Effect: Hunger ↓ (Decrease)
  Condition: PearCount >= 1
      │
      │ Matches WorldKey "PearCount" with EffectType.Increase
      ▼
Action: PickupPearAction
  Effect: PearCount ↑ (Increase)
  Condition: (none, or conditions already met by current world state)
```

### Comparison Operators

```csharp
public enum Comparison
{
    SmallerThan,           // <
    SmallerThanOrEqual,    // <=
    GreaterThan,           // >
    GreaterThanOrEqual,    // >=
}
```

### Effect Types

```csharp
public enum EffectType
{
    Decrease = 0,  // Action claims to decrease the WorldKey
    Increase = 1,  // Action claims to increase the WorldKey
}
```

### Matching Logic

The planner connects effects to conditions based on:
1. **Same WorldKey** — the effect and condition must reference the same WorldKey type
2. **Direction compatibility** — an `Increase` effect can satisfy `GreaterThan`/`GreaterThanOrEqual` conditions; a `Decrease` effect can satisfy `SmallerThan`/`SmallerThanOrEqual` conditions

### Value Conditions vs Reference Conditions

**Value Conditions** compare against a fixed integer:
```csharp
builder.AddGoal<GatherGoal>()
    .AddCondition<ResourceCount>(Comparison.GreaterThanOrEqual, 5);
```

**Reference Conditions** compare two WorldKeys against each other:
```csharp
builder.AddAction<ReloadAction>()
    .AddCondition<CurrentAmmo, MaxAmmo>(Comparison.SmallerThan);
    // Triggers when CurrentAmmo < MaxAmmo
```

### Effects Are Declarative Only

This is one of the most important concepts to understand:

```csharp
// This declares that the action CLAIMS to increase PearCount
// for graph-building purposes ONLY
builder.AddAction<PickupPearAction>()
    .AddEffect<PearCount>(EffectType.Increase);

// The actual state change must happen in your action code:
public override void Complete(IMonoAgent agent, Data data)
{
    data.DataBehaviour.pearCount++;  // THIS is what actually changes the world
}
```

The system never automatically modifies world state based on effects. Effects are purely for the planner to build the action graph.

---

## 12. Controllers

Controllers are components added to the `GoapBehaviour` GameObject. They determine when sensors run and when the resolver plans.

### ReactiveController

```csharp
// Add to GoapBehaviour GameObject:
// ReactiveControllerBehaviour component
```

**Behavior**: Runs sensors and resolver **only when agents need new actions** (when their current action completes, stops, or is invalidated).

**When to use**:
- Agents primarily react to immediate needs
- Performance-sensitive scenarios (less frequent planning)
- Simple agents that don't need to reconsider plans mid-action

### ProactiveController

```csharp
// Add to GoapBehaviour GameObject:
// ProactiveControllerBehaviour component
```

**Behavior**: Runs sensors and resolver **periodically** regardless of whether agents need new actions. Can discover better plans proactively.

**When to use**:
- Rapidly changing environments
- Agents that should reconsider plans when better options emerge
- More "intelligent" feeling agents

**MayResolve**: Actions can control whether the resolver runs during their execution:

```csharp
public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
{
    // Allow resolver to check for better plans while this action runs
    return ActionRunState.ContinueOrResolve;  // MayResolve = true

    // Prevent re-planning during critical actions
    return ActionRunState.Continue;             // MayResolve = false

    // Wait with optional re-planning
    return ActionRunState.Wait(2f, mayResolve: true);
}
```

### ManualController

```csharp
// Add to GoapBehaviour GameObject:
// ManualControllerBehaviour component
```

**Behavior**: You explicitly trigger sensors and resolver via code. Nothing happens automatically.

**When to use**:
- Turn-based games
- Precision-critical scenarios where you need exact control over planning timing
- Custom update loops

---

## 13. Dependency Injection

### Built-in IGoapInjector

The library provides an injection interface for wiring dependencies into actions, goals, and sensors:

```csharp
public interface IGoapInjector
{
    void Inject(IActionBase action);
    void Inject(IGoalBase goal);
    void Inject(IWorldSensor worldSensor);
    void Inject(ITargetSensor targetSensor);
}
```

### Custom Injector Implementation

```csharp
public class GoapInjector : MonoBehaviour, IGoapInjector
{
    public ItemFactory itemFactory;
    public GameConfig gameConfig;

    public void Inject(IActionBase action)
    {
        if (action is IInjectable injectable)
            injectable.Inject(this);
    }

    public void Inject(IGoalBase goal)
    {
        if (goal is IInjectable injectable)
            injectable.Inject(this);
    }

    public void Inject(IWorldSensor worldSensor)
    {
        if (worldSensor is IInjectable injectable)
            injectable.Inject(this);
    }

    public void Inject(ITargetSensor targetSensor)
    {
        if (targetSensor is IInjectable injectable)
            injectable.Inject(this);
    }
}

// Interface for injectable GOAP classes
public interface IInjectable
{
    void Inject(GoapInjector injector);
}
```

Register via `GoapConfigInitializerBase`:

```csharp
public class GoapConfigInitializer : GoapConfigInitializerBase
{
    public override void InitConfig(GoapConfig config)
    {
        config.GoapInjector = GetComponent<GoapInjector>();
    }
}
```

### Zenject Integration

```csharp
public class ZenjectGoapInjector : MonoBehaviour, IGoapInjector
{
    private DiContainer container;

    [Inject]
    private void Construct(DiContainer container)
    {
        this.container = container;
    }

    public void Inject(IActionBase action) => container.Inject(action);
    public void Inject(IGoalBase goal) => container.Inject(goal);
    public void Inject(IWorldSensor worldSensor) => container.Inject(worldSensor);
    public void Inject(ITargetSensor targetSensor) => container.Inject(targetSensor);
}
```

### Data Class Injection (Agent-Level)

For per-agent dependencies, use attributes on `IActionData` properties. These are resolved from the agent's GameObject:

```csharp
public class Data : IActionData
{
    public ITarget Target { get; set; }

    [GetComponent]          public DataBehaviour Data { get; set; }
    [GetComponentInChildren] public Renderer Renderer { get; set; }
    [GetComponentInParent]   public TeamManager Team { get; set; }
}
```

This is the recommended approach for action-level dependencies since it's automatic and agent-scoped.

---

## 14. Debugging & Visualization

### Graph Viewer

Access via: **Tools > GOAP > Graph Viewer** (or **Ctrl+G** / **Cmd+G**)

The Graph Viewer provides a visual representation of the AgentType behavior graph:

- **Goals** displayed with their conditions
- **Actions** displayed with their conditions and effects
- **Connections** between nodes based on WorldKey relationships
- **Sensor assignments** visible per key
- **Current agent state** visible when selecting an agent at runtime

### Runtime Debugging Tips

1. **Graph Viewer at runtime**: Select an agent in the hierarchy, then open Graph Viewer to see its current plan highlighted
2. **Agent events**: Subscribe to events for logging:
   ```csharp
   provider.Events.OnNoActionFound += goal => Debug.LogWarning($"No plan found for {goal}");
   provider.Events.OnGoalStart += goal => Debug.Log($"Pursuing: {goal}");
   provider.Events.OnActionStart += action => Debug.Log($"Executing: {action}");
   ```
3. **Check Issues button**: In ScriptableObject config, use "Check Issues" to validate configuration

### Common Debug Scenarios

| Symptom | Likely Cause |
|---------|-------------|
| Agent does nothing | No goal requested, or no plan found (check `OnNoActionFound`) |
| Agent stuck in one action | Action never returns `Completed` or `Stop` |
| Wrong action selected | Check costs, conditions, and sensor values in Graph Viewer |
| "No action found" for valid goal | Missing condition/effect chain, or sensor returning bad values |
| Action target is null | Sensor returning null, or target GameObject destroyed |

---

## 15. Practical Examples

### Example 1: Wandering Agent

The simplest GOAP agent — wanders to random positions.

**Keys:**
```csharp
public class IsIdle : WorldKeyBase { }
public class WanderTarget : TargetKeyBase { }
```

**Configuration:**
```csharp
public class WanderCapability : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("Wander");

        builder.AddGoal<WanderGoal>()
            .AddCondition<IsIdle>(Comparison.GreaterThanOrEqual, 1);

        builder.AddAction<WanderAction>()
            .SetTarget<WanderTarget>()
            .AddEffect<IsIdle>(EffectType.Increase)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming);

        builder.AddTargetSensor<RandomPositionSensor>()
            .SetTarget<WanderTarget>();

        return builder.Build();
    }
}
```

### Example 2: Hunger / Feeding System

Agent gets hungry over time, gathers food, and eats.

**Keys:**
```csharp
public class Hunger : WorldKeyBase { }
public class FoodCount : WorldKeyBase { }
public class IsIdle : WorldKeyBase { }
public class ClosestFood : TargetKeyBase { }
public class IdleTarget : TargetKeyBase { }
```

**Configuration:**
```csharp
public class HungerCapability : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("Hunger");

        // Eat when hungry
        builder.AddGoal<EatGoal>()
            .AddCondition<Hunger>(Comparison.SmallerThanOrEqual, 0)
            .SetBaseCost(1);

        // Idle as fallback
        builder.AddGoal<IdleGoal>()
            .AddCondition<IsIdle>(Comparison.GreaterThanOrEqual, 1)
            .SetBaseCost(5);

        // Eat action — requires food
        builder.AddAction<EatAction>()
            .AddCondition<FoodCount>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<Hunger>(EffectType.Decrease)
            .SetRequiresTarget(false);

        // Gather food action
        builder.AddAction<GatherFoodAction>()
            .SetTarget<ClosestFood>()
            .AddEffect<FoodCount>(EffectType.Increase)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming)
            .SetStoppingDistance(1.5f);

        // Idle action
        builder.AddAction<IdleAction>()
            .SetTarget<IdleTarget>()
            .AddEffect<IsIdle>(EffectType.Increase);

        // Sensors
        builder.AddMultiSensor<HungerFoodSensor>();

        builder.AddTargetSensor<IdleTargetSensor>()
            .SetTarget<IdleTarget>();

        return builder.Build();
    }
}
```

**Brain:**
```csharp
public class HungerBrain : MonoBehaviour
{
    private GoapActionProvider provider;
    private DataBehaviour data;

    private void Awake()
    {
        provider = GetComponent<GoapActionProvider>();
        data = GetComponent<DataBehaviour>();
    }

    private void Update()
    {
        if (data.hunger > 50f)
            provider.RequestGoal<EatGoal>();
        else
            provider.RequestGoal<IdleGoal>();
    }
}
```

**Plan chain when hungry**: `EatGoal` → needs `Hunger ↓` → `EatAction` → needs `FoodCount >= 1` → `GatherFoodAction` → agent walks to food, picks it up, then eats.

### Example 3: Resource Gathering

Agent collects resources and deposits them at a base.

**Keys:**
```csharp
public class CarriedResources : WorldKeyBase { }
public class DepositedResources : WorldKeyBase { }
public class ClosestResource : TargetKeyBase { }
public class BaseLocation : TargetKeyBase { }
```

**Configuration:**
```csharp
public class GatheringCapability : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("Gathering");

        builder.AddGoal<DepositGoal>()
            .AddCondition<DepositedResources>(Comparison.GreaterThanOrEqual, 10);

        // Deposit at base
        builder.AddAction<DepositAction>()
            .SetTarget<BaseLocation>()
            .AddCondition<CarriedResources>(Comparison.GreaterThanOrEqual, 1)
            .AddEffect<DepositedResources>(EffectType.Increase)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming);

        // Gather from world
        builder.AddAction<GatherAction>()
            .SetTarget<ClosestResource>()
            .AddEffect<CarriedResources>(EffectType.Increase)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming)
            .SetStoppingDistance(1f);

        builder.AddMultiSensor<ResourceSensor>();

        builder.AddTargetSensor<BaseSensor>()
            .SetTarget<BaseLocation>();

        return builder.Build();
    }
}
```

**Plan chain**: `DepositGoal` → needs `DepositedResources ↑` → `DepositAction` → needs `CarriedResources >= 1` → `GatherAction` → agent walks to resource, gathers, walks to base, deposits.

### Example 4: Combat Patterns

Agent attacks enemies, retreating to heal when low on health.

**Keys:**
```csharp
public class EnemyAlive : WorldKeyBase { }
public class Health : WorldKeyBase { }
public class HasAmmo : WorldKeyBase { }
public class ClosestEnemy : TargetKeyBase { }
public class HealingStation : TargetKeyBase { }
public class AmmoStation : TargetKeyBase { }
```

**Configuration:**
```csharp
public class CombatCapability : CapabilityFactoryBase
{
    public override ICapabilityConfig Create()
    {
        var builder = new CapabilityBuilder("Combat");

        // Kill enemy goal
        builder.AddGoal<KillEnemyGoal>()
            .AddCondition<EnemyAlive>(Comparison.SmallerThanOrEqual, 0)
            .SetBaseCost(1);

        // Stay alive goal (higher priority when health is low via dynamic cost)
        builder.AddGoal<StayAliveGoal>()
            .AddCondition<Health>(Comparison.GreaterThanOrEqual, 80);

        // Attack action
        builder.AddAction<AttackAction>()
            .SetTarget<ClosestEnemy>()
            .AddCondition<HasAmmo>(Comparison.GreaterThanOrEqual, 1)
            .AddCondition<Health>(Comparison.GreaterThanOrEqual, 30)
            .AddEffect<EnemyAlive>(EffectType.Decrease)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming)
            .SetStoppingDistance(5f);

        // Heal action
        builder.AddAction<HealAction>()
            .SetTarget<HealingStation>()
            .AddEffect<Health>(EffectType.Increase)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming);

        // Reload action
        builder.AddAction<ReloadAction>()
            .SetTarget<AmmoStation>()
            .AddEffect<HasAmmo>(EffectType.Increase)
            .SetMoveMode(ActionMoveMode.MoveBeforePerforming);

        builder.AddMultiSensor<CombatSensor>();

        return builder.Build();
    }
}
```

**Emergent behavior**: If the agent is low on health, the `AttackAction`'s condition `Health >= 30` blocks it, so the planner finds `HealAction` first. If out of ammo, the planner chains `ReloadAction` before `AttackAction`. All without explicit transition authoring.

---

## 16. Performance Considerations

### Multi-Threading

The resolver runs on Unity's **Job System**, leveraging multiple CPU cores. Planning does not block the main thread, which is critical for games with many agents.

### Graph Building

The behavior graph is built **once** at initialization (or when AgentType configuration changes), not every frame. This means:
- Adding/removing agent instances is cheap
- The graph structure is shared across all agents of the same type
- Only the resolver (A* search) runs per-agent at planning time

### Sensor Timing

Use `SensorTimer` to control how frequently sensors update:

```csharp
// Expensive operations (FindObjectsOfType) — run less often
public override ISensorTimer Timer => SensorTimer.Interval(0.5f);

// Cheap operations — every frame is fine
public override ISensorTimer Timer => SensorTimer.Always;

// One-time setup — run once
public override ISensorTimer Timer => SensorTimer.Once;
```

### Object Caching

- Use `references.GetCachedComponent<T>()` instead of `GetComponent<T>()` in sensors and actions
- Reuse `ITarget` instances via the `existingTarget` parameter in sensors
- Cache `FindObjectsOfType` results in sensor `Update()` methods (called once per sensor run, not per agent)

### Distance Multiplier

Control how much physical distance affects plan cost:

```csharp
provider.SetDistanceMultiplier(0.5f);      // Distance matters less
provider.SetDistanceMultiplierSpeed(2f);    // How fast the multiplier changes
```

### General Tips

1. Keep the number of WorldKeys minimal — each key adds complexity to the graph
2. Use Global sensors for shared data to avoid redundant per-agent computation
3. Use MultiSensor to batch related sensors and share cached data
4. Profile with the Graph Viewer to understand plan complexity
5. Avoid `FindObjectsOfType` every frame — cache in `Update()` with appropriate timer intervals

---

## 17. Best Practices & Common Pitfalls

### Best Practices

1. **Actions and Goals MUST be stateless.** All agents of the same AgentType share the same action/goal instances. Store per-agent data in the `Data` class only.

2. **Effects are declarative only.** The system does NOT auto-apply effects to world state. You must implement actual state changes in `Complete()` or `Perform()`.

3. **Goal selection is your responsibility.** The library resolves which actions to take for a given goal. Use a "Brain" script with your own decision logic.

4. **Every action needs a target** (or explicitly set `SetRequiresTarget(false)`). The system uses target positions to calculate movement costs between actions.

5. **Use `GetCachedComponent<T>()`** on `IComponentReference` instead of `GetComponent<T>()` for performance.

6. **MultiSensor registration must happen in the constructor**, not in `Created()`. This is a common source of bugs.

7. **Reuse target instances** in sensors via the `existingTarget` parameter to reduce garbage collection pressure.

8. **Use the Graph Viewer** (Ctrl+G / Cmd+G) to debug and visualize action/goal connections.

9. **Start simple.** Build the simplest possible agent first, verify it works, then layer on complexity.

10. **One capability per behavior domain.** Keep capabilities focused (idle, combat, gathering) for reusability.

### Common Pitfalls

| Pitfall | Problem | Solution |
|---------|---------|----------|
| Storing state in action fields | All agents share the instance — state bleeds between agents | Use the `Data` class for all per-agent state |
| Expecting effects to auto-apply | World state never changes; agent re-plans forever | Implement actual state changes in `Complete()`/`Perform()` |
| Registering MultiSensor lambdas in `Created()` | Sensors silently fail to register | Move registration to the constructor |
| Using `GetComponent<T>()` in sensors | Performance degradation with many agents | Use `GetCachedComponent<T>()` |
| Missing target sensor for action | Action can never execute (no target position) | Ensure every TargetKey has a corresponding sensor |
| No fallback goal | Agent freezes when primary goal is unachievable | Always provide a low-priority fallback (e.g., IdleGoal) |
| Overly complex condition chains | Planner can't find valid plans | Start simple, verify with Graph Viewer, add complexity gradually |
| Using WorldState as source of truth | Stale data, inconsistencies | WorldState is a planning aid; keep authoritative state in your own components |
| Not handling null targets | NullReferenceException at runtime | Check for null in `Perform()`/`Complete()` and return `ActionRunState.Stop` |

### Configuration Selection Guide

| Scenario | Recommended Approach |
|----------|---------------------|
| Programmer-driven AI design | Code-based (CapabilityFactory) |
| Designer needs to tune parameters | ScriptableObject-based |
| Complex projects with many AgentTypes | Code-based (better refactoring) |
| Rapid prototyping | Either works; code-based is faster to iterate |
| Team with mixed technical levels | ScriptableObject for designers, code for programmers |

---

## 18. GOAP vs Other AI Systems

### Comparison Table

| Feature | FSM | Behavior Tree | Utility AI | GOAP |
|---------|-----|---------------|------------|------|
| **Architecture** | States + transitions | Tree of nodes (selector/sequence/leaf) | Scored actions | Goals + actions + planner |
| **Authoring effort** | Low (small), High (large) | Medium | Medium | Medium-Low |
| **Scalability** | Poor (O(n^2) transitions) | Moderate (deep trees) | Good | Excellent |
| **Emergent behavior** | None | None | Limited | High |
| **Runtime adaptability** | Low | Low-Medium | High | High |
| **Debugging** | Easy | Easy-Medium | Medium | Medium (needs Graph Viewer) |
| **Action composition** | Manual | Manual (tree structure) | Independent scoring | Automatic (planner chains) |
| **Adding new behaviors** | Requires new transitions | Requires tree restructuring | Add action + scoring | Add action + conditions/effects |
| **Best for** | Simple agents, UI | Moderate complexity, well-defined behaviors | Many competing priorities | Complex agents, emergent behavior |
| **Computational cost** | Minimal | Low | Low-Medium | Medium (planner overhead) |
| **Learning curve** | Low | Low-Medium | Medium | Medium-High |

### When to Choose GOAP

**Choose GOAP when:**
- Agents need to exhibit complex, emergent behavior
- You want new capabilities to automatically integrate with existing behaviors
- The game involves agents with many possible actions and goals
- You want agents to dynamically adapt to changing conditions
- You need to scale the number of possible behaviors without exponential authoring effort

**Consider alternatives when:**
- Agent behavior is simple and well-defined (use FSM)
- You need precise control over behavior priority ordering (use BT)
- Actions are independent and don't form chains (use Utility AI)
- Performance budget is extremely tight and agents are simple

### Hybrid Approaches

GOAP works well in combination with other systems:
- **FSM + GOAP**: Use an FSM as the "Brain" to select between high-level states (combat, peaceful, fleeing), each requesting different GOAP goals
- **Utility AI + GOAP**: Use utility scoring to dynamically select which goals to request
- **BT + GOAP**: Use a behavior tree for high-level decision making, with GOAP handling complex action planning within specific tree branches

---

## 19. Resources & References

### Official Resources

- **GitHub Repository**: [https://github.com/crashkonijn/GOAP](https://github.com/crashkonijn/GOAP)
- **Documentation Site**: [https://goap.crashkonijn.com/](https://goap.crashkonijn.com/)
- **Unity Asset Store**: Search "GOAP crashkonijn"

### Documentation Pages

| Topic | URL |
|-------|-----|
| Getting Started | [https://goap.crashkonijn.com/general/getting-started](https://goap.crashkonijn.com/general/getting-started) |
| GOAP Overview | [https://goap.crashkonijn.com/general/goap](https://goap.crashkonijn.com/general/goap) |
| Keys | [https://goap.crashkonijn.com/general/keys](https://goap.crashkonijn.com/general/keys) |
| Goals | [https://goap.crashkonijn.com/general/goals](https://goap.crashkonijn.com/general/goals) |
| Actions | [https://goap.crashkonijn.com/general/actions](https://goap.crashkonijn.com/general/actions) |
| Sensors | [https://goap.crashkonijn.com/general/sensors](https://goap.crashkonijn.com/general/sensors) |
| Conditions & Effects | [https://goap.crashkonijn.com/general/conditions-and-effects](https://goap.crashkonijn.com/general/conditions-and-effects) |
| Agent Types | [https://goap.crashkonijn.com/general/agent-type](https://goap.crashkonijn.com/general/agent-type) |
| Controllers | [https://goap.crashkonijn.com/general/controllers](https://goap.crashkonijn.com/general/controllers) |
| Dependency Injection | [https://goap.crashkonijn.com/general/injection](https://goap.crashkonijn.com/general/injection) |
| Graph Viewer | [https://goap.crashkonijn.com/general/graph-viewer](https://goap.crashkonijn.com/general/graph-viewer) |
| World State | [https://goap.crashkonijn.com/general/world-state](https://goap.crashkonijn.com/general/world-state) |

### Academic Papers

- **Orkin, J. (2003)**. "Applying Goal-Oriented Action Planning to Games." *AI Game Programming Wisdom 2*. — The original GOAP paper by Jeff Orkin, describing the architecture as implemented in F.E.A.R.
- **Orkin, J. (2006)**. "Three States and a Plan: The A.I. of F.E.A.R." *GDC 2006*. — GDC presentation detailing the practical implementation and lessons learned.

### Community Resources

- **crashkonijn Discord**: Community support and discussion (link available on GitHub repo)
- **Unity Forums**: Search for "crashkonijn GOAP" for community discussions
- **YouTube**: Search for "crashkonijn GOAP tutorial" for video walkthroughs

---

*This guide covers crashkonijn/GOAP v3.x. API details may change in future versions. Always refer to the [official documentation](https://goap.crashkonijn.com/) for the latest information.*
