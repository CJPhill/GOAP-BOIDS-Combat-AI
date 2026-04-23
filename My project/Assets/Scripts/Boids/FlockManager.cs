using System.Collections.Generic;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

public class FlockManager : MonoBehaviour
{
    // Extended state machine for thesis parity: conditions 3 & 4 cover Flee/Scatter/
    // Kite/Flank/Guard/Regroup via GOAP; PureBOIDS now covers them at the flock level
    // via FlockStateResolver. See FlockStateResolver.Resolve for transition rules.
    public enum FlockState
    {
        Idle,
        Grouping,
        Engaging,
        Fleeing,
        Scattering,
        Kiting,
        Flanking,
        Guarding,
        Regrouping
    }
    public enum RangedAttackPhase { None, Forming, Locked, Firing, Recovering }
    public enum MeleeAttackPhase { None, WindUp, Charging, Recovering }

    [SerializeField] private BoidSettings settings;
    [SerializeField] private GameObject boidPrefab;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private List<BoidAgent> boids = new List<BoidAgent>();
    private List<BoidAgent> foreignBoids = new List<BoidAgent>();
    private Transform target;
    private SphereCollider aggroTrigger;
    private FlockState state = FlockState.Idle;
    private int originalFlockSize;
    private float currentFlockHealth;
    private int currentAttackerCount;

    // Scatter auto-expires after settings.scatterDuration; Regroup clears once the
    // flock tightens back within regroupRadius (see UpdateFlockState).
    private float scatterTimer;

    // Tuning-parity overrides set by ConditionManager before Start() fires so
    // conditions 2/3/4 spawn identical flock sizes + HP pools.
    private int flockSizeOverride = -1;
    private float maxHealthOverride = -1f;

    // Flock ranged attack state
    private RangedAttackPhase rangedPhase = RangedAttackPhase.None;
    private float rangedPhaseTimer;
    private float flockAttackCooldownTimer;
    private Vector3[] formationSlots;
    private float formationOrbitAngle;

    // Flock melee attack state
    private MeleeAttackPhase meleePhase = MeleeAttackPhase.None;
    private float meleePhaseTimer;
    private float flockMeleeCooldownTimer;
    private bool meleeWaveDamageDealt;

    public FlockType FlockType => settings.flockType;
    public BoidSettings Settings => settings;
    public IReadOnlyList<BoidAgent> Boids => boids;
    public int BoidCount => boids.Count;
    public Transform Target => target;
    public FlockState State => state;

    // Leader support for BOIDSWithGOAPLeader condition
    private BoidAgent leaderBoid;
    public BoidAgent LeaderBoid => leaderBoid;
    public void SetLeader(BoidAgent leader) { leaderBoid = leader; }

    public bool IsDead => currentFlockHealth <= 0f;
    /// <summary>Max HP, honoring the ConditionManager override when set.</summary>
    public float EffectiveMaxHealth => maxHealthOverride > 0f
        ? maxHealthOverride
        : (settings != null ? settings.maxHealth : 0f);
    public float HealthPercent => EffectiveMaxHealth > 0f ? currentFlockHealth / EffectiveMaxHealth : 0f;

    public void TakeDamage(float amount)
    {
        if (IsDead) return;
        currentFlockHealth = Mathf.Max(currentFlockHealth - amount, 0f);
        if (IsDead)
            KillAllBoids();
        else
            SyncBoidCountToHealth();
    }

    private void SyncBoidCountToHealth()
    {
        if (originalFlockSize == 0) return;

        // Scale live boid count from minSurvivorFraction..1 as health goes 0..1
        // Keeps the last minSurvivorFraction alive until health reaches 0
        float maxHP = EffectiveMaxHealth;
        float hp = maxHP > 0f ? currentFlockHealth / maxHP : 0f;
        int targetCount = Mathf.RoundToInt(
            Mathf.Lerp(settings.minSurvivorFraction, 1f, hp) * originalFlockSize);

        while (boids.Count > targetCount)
        {
            int last = boids.Count - 1;
            BoidAgent dying = boids[last];
            boids.RemoveAt(last);
            Destroy(dying.gameObject);
        }

        // Cancel melee attack if too few boids remain
        if (meleePhase != MeleeAttackPhase.None && boids.Count < 2)
            ResetMeleeAttack();

        // Recompute formation if boids die mid-attack
        if (rangedPhase == RangedAttackPhase.Forming || rangedPhase == RangedAttackPhase.Locked)
        {
            if (boids.Count < 3)
            {
                ReleaseBoidFormations();
                rangedPhase = RangedAttackPhase.None;
            }
            else
            {
                ComputeFormationSlots(formationOrbitAngle);
                AssignFormationToBoids();
            }
        }
    }

    private void KillAllBoids()
    {
        for (int i = boids.Count - 1; i >= 0; i--)
            Destroy(boids[i].gameObject);
        boids.Clear();

        FlockCoordinator coordinator = FindFirstObjectByType<FlockCoordinator>();
        coordinator?.UnregisterFlock(this);
        Destroy(gameObject);
    }

    public float EffectiveBoundaryRadius
    {
        get
        {
            if (state != FlockState.Idle && settings.engageBoundaryRadius > 0f)
                return settings.engageBoundaryRadius;
            return settings.boundaryRadius;
        }
    }

    public float EffectiveAvoidanceRadius
    {
        get
        {
            if (state != FlockState.Idle && settings.engageAvoidanceRadius > 0f)
                return settings.engageAvoidanceRadius;
            return settings.avoidanceRadius;
        }
    }

    /// <summary>
    /// Read-only slot check used by GOAP sensors.
    /// Does NOT consume a slot — call RequestAttack() in the action's Start() to do that.
    /// </summary>
    public bool CanAttack => currentAttackerCount < settings.maxSimultaneousAttackers;

    public bool RequestAttack()
    {
        if (currentAttackerCount >= settings.maxSimultaneousAttackers)
            return false;
        currentAttackerCount++;
        return true;
    }

    public void ReleaseAttack()
    {
        currentAttackerCount = Mathf.Max(currentAttackerCount - 1, 0);
    }

    /// <summary>
    /// Called by ConditionManager BEFORE Start() fires (via AddComponent ordering)
    /// so the manager can spawn the requested flock size and HP pool for thesis
    /// parity. -1 on either field means "use settings value."
    /// </summary>
    public void ApplyComparisonOverrides(int flockSize, float maxHealth)
    {
        if (flockSize > 0) flockSizeOverride = flockSize;
        if (maxHealth > 0f) maxHealthOverride = maxHealth;
    }

    /// <summary>
    /// True when the state permits attack machines (melee wave + ranged volley)
    /// to advance. Only Engaging/Flanking permit both; Kiting permits ranged only
    /// (the caller checks flockType). All other states suppress attacks entirely.
    /// </summary>
    public bool AttackMachineAllowed
    {
        get
        {
            switch (state)
            {
                case FlockState.Engaging:
                case FlockState.Flanking:
                case FlockState.Kiting:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Max distance any boid sits from the flock centroid. Feeds the Regrouping
    /// transition (fires when this exceeds settings.isolationThreshold).
    /// </summary>
    public float GetMaxBoidDistanceFromCentroid()
    {
        if (boids.Count == 0) return 0f;
        Vector3 center = GetFlockCenter();
        float max = 0f;
        for (int i = 0; i < boids.Count; i++)
        {
            float d = Vector3.Distance(boids[i].Position, center);
            if (d > max) max = d;
        }
        return max;
    }

    public void SetTarget(Transform t)
    {
        bool wasNull = target == null;
        target = t;
        if (t != null)
        {
            state = FlockState.Grouping;
            // First target-acquisition of this trial is the stimulus event the
            // reaction-time metric measures against. Fires once per flock per trial.
            if (wasNull)
                BehavioralMetricsCollector.Instance?.LogEvent(
                    "StimulusAcquired", gameObject.name,
                    (int)settings.flockType, $"Target={t.name}");
        }
    }

    public void ClearTarget()
    {
        target = null;
        state = FlockState.Idle;
        ReleaseBoidFormations();
        rangedPhase = RangedAttackPhase.None;
        ResetMeleeAttack();
    }

    public void SetForeignBoids(List<BoidAgent> foreign)
    {
        foreignBoids = foreign;
    }

    private void Awake()
    {
        if (settings != null && settings.aggroRadius > 0f)
        {
            aggroTrigger = gameObject.AddComponent<SphereCollider>();
            aggroTrigger.radius = settings.aggroRadius;
            aggroTrigger.isTrigger = true;

            if (GetComponent<Rigidbody>() == null)
            {
                Rigidbody rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    private void Start()
    {
        if (boidPrefab != null && settings != null)
        {
            SpawnFlock();
            originalFlockSize = boids.Count;
            // Honour HP override before falling back to settings.maxHealth.
            currentFlockHealth = maxHealthOverride > 0f ? maxHealthOverride : settings.maxHealth;
        }
    }

    private void Update()
    {
        if (settings == null) return;

        // Leader-driven conditions (BOIDSWithGOAPLeader) suppress flock-level
        // decision making — the leader's GOAP owns everything.
        bool useGoap = ConditionManager.Instance != null && ConditionManager.Instance.UsesGoapForBoids;

        if (!useGoap)
            UpdateFlockState();

        if (target == null) return;

        // Attack machines gated by state — Fleeing/Scattering/Guarding/Regrouping/Grouping/Idle suppress.
        if (!useGoap && AttackMachineAllowed)
        {
            if (settings.flockType == FlockType.Ranged)
                UpdateRangedFlockAttack();
            if (settings.flockType == FlockType.Melee)
                UpdateMeleeFlockAttack();
        }

        // Anchor movement — chase target for Engaging/Flanking/Guarding/Grouping,
        // retreat for Fleeing/Kiting/Scattering, hold for Regrouping/Idle.
        MoveAnchor();
    }

    private void MoveAnchor()
    {
        if (target == null) return;

        switch (state)
        {
            case FlockState.Engaging:
            case FlockState.Flanking:
            case FlockState.Grouping:
            {
                Vector3 direction = target.position - transform.position;
                float distance = direction.magnitude;
                if (distance < settings.targetStopDistance) return;
                float speed = settings.targetFollowSpeed;
                if (distance < settings.targetSlowDistance)
                {
                    float t = (distance - settings.targetStopDistance) / (settings.targetSlowDistance - settings.targetStopDistance);
                    speed *= Mathf.Clamp01(t);
                }
                transform.position = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);
                break;
            }

            case FlockState.Guarding:
            {
                // Hold position at guardInnerRange — close the gap if beyond, back off if closer.
                float dist = Vector3.Distance(transform.position, target.position);
                Vector3 toTarget = (target.position - transform.position).normalized;
                float step = settings.targetFollowSpeed * 0.5f * Time.deltaTime;
                if (dist > settings.guardInnerRange + 1f)
                    transform.position += toTarget * step;
                else if (dist < settings.guardInnerRange - 1f)
                    transform.position -= toTarget * step;
                break;
            }

            case FlockState.Fleeing:
            case FlockState.Kiting:
            case FlockState.Scattering:
            {
                Vector3 awayDir = (transform.position - target.position).normalized;
                float speed = settings.targetFollowSpeed * settings.fleeSpeedMultiplier;
                transform.position += awayDir * speed * Time.deltaTime;
                break;
            }

            // Idle / Regrouping: hold anchor, let cohesion tighten the flock.
        }
    }

    /// <summary>
    /// Flock-level state transition. Mirrors GoalPriorityResolver priority via
    /// FlockStateResolver so PureBOIDS expresses the same behavior set that the
    /// GOAP conditions express through planning.
    /// </summary>
    private void UpdateFlockState()
    {
        // Scattering auto-expires so the flock can recover to Fleeing/Engaging/etc.
        if (state == FlockState.Scattering)
        {
            scatterTimer -= Time.deltaTime;
            if (scatterTimer > 0f) return; // stay scattering
        }

        float playerDist = target != null
            ? Vector3.Distance(target.position, transform.position)
            : float.MaxValue;
        bool hasTarget = target != null;
        bool playerNearby = hasTarget && playerDist <= settings.aggroRadius;

        // Clustered-enough check (existing Grouping → Engaging transition criterion).
        float effectiveRadius = EffectiveBoundaryRadius;
        float radiusSqr = effectiveRadius * effectiveRadius;
        int clusteredCount = 0;
        for (int i = 0; i < boids.Count; i++)
        {
            Vector3 offset = boids[i].Position - transform.position;
            if (offset.sqrMagnitude <= radiusSqr) clusteredCount++;
        }
        float clusterFraction = boids.Count > 0 ? (float)clusteredCount / boids.Count : 1f;
        bool clusteredEnough = clusterFraction >= settings.groupUpThreshold;

        float maxDistFromCentroid = GetMaxBoidDistanceFromCentroid();
        bool rangedActive = rangedPhase != RangedAttackPhase.None && rangedPhase != RangedAttackPhase.Recovering;
        bool meleeActive = meleePhase != MeleeAttackPhase.None && meleePhase != MeleeAttackPhase.Recovering;
        bool slotsSaturated = currentAttackerCount >= settings.maxSimultaneousAttackers;

        var behavior = FlockStateResolver.Resolve(
            healthPercent: HealthPercent,
            hasTarget: hasTarget,
            playerNearby: playerNearby,
            playerDist: playerDist,
            isRanged: settings.flockType == FlockType.Ranged,
            attackSlotsSaturated: slotsSaturated,
            meleeAttackActive: meleeActive,
            rangedAttackActive: rangedActive,
            maxDistanceFromCentroid: maxDistFromCentroid,
            boidCount: boids.Count,
            clusteredEnoughToEngage: clusteredEnough,
            criticalHealthThreshold: settings.criticalHealthThreshold,
            fleeHealthThreshold: settings.fleeHealthThreshold,
            kiteMinDistance: settings.kiteMinDistance,
            isolationThreshold: settings.isolationThreshold,
            guardInnerRange: settings.guardInnerRange,
            guardOuterRange: settings.guardOuterRange);

        // FlockBehavior and FlockState are declared in the same order so a cast is safe.
        FlockState newState = (FlockState)(int)behavior;

        // Scattering entry: arm the timer and cancel in-flight attacks.
        if (newState == FlockState.Scattering && state != FlockState.Scattering)
        {
            scatterTimer = settings.scatterDuration;
            ResetMeleeAttack();
            if (rangedPhase != RangedAttackPhase.None)
            {
                ReleaseBoidFormations();
                rangedPhase = RangedAttackPhase.None;
            }
        }

        // Log state transitions as GoalChange events so PureBOIDS shows up in the
        // same reaction-time + event-log pipeline as GOAP conditions.
        if (newState != state)
        {
            BehavioralMetricsCollector.Instance?.LogEvent(
                "GoalChange", gameObject.name, (int)settings.flockType, $"{state}→{newState}");
        }

        state = newState;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (target == null && settings != null && other.CompareTag(settings.aggroTag))
            SetTarget(other.transform);
    }

    public void SpawnFlock()
    {
        // Cache the GOAP behaviour lookup once, outside the per-boid loop.
        var goapBehaviour = FindFirstObjectByType<GoapBehaviour>();

        int flockSize = flockSizeOverride > 0 ? flockSizeOverride : settings.flockSize;
        for (int i = 0; i < flockSize; i++)
        {
            Vector3 spawnPos = transform.position + Random.insideUnitSphere * settings.spawnRadius;
            Vector3 startVelocity = Random.onUnitSphere * settings.maxSpeed * 0.5f;

            GameObject boidObj = Instantiate(boidPrefab, spawnPos, Quaternion.LookRotation(startVelocity), transform);
            BoidAgent agent = boidObj.GetComponent<BoidAgent>();
            agent.settings = settings;
            agent.manager = this;
            agent.Initialize(startVelocity);
            ApplyFlockColor(agent);

            // Phase 11 — GOAP agent-type wiring.
            if (goapBehaviour != null)
            {
                var provider = agent.GetComponent<GoapActionProvider>();
                if (provider != null)
                {
                    string agentTypeName = settings.flockType == FlockType.Melee ? "MeleeBoid" : "RangedBoid";
                    provider.AgentType = goapBehaviour.GetAgentType(agentTypeName);
                }
            }

            boids.Add(agent);
        }
    }

    public void AddBoids(List<BoidAgent> incoming)
    {
        for (int i = 0; i < incoming.Count; i++)
        {
            BoidAgent boid = incoming[i];
            boid.transform.SetParent(transform);
            boid.settings = settings;
            boid.manager = this;
            ApplyFlockColor(boid);
            boids.Add(boid);
        }

        // Recompute formation if merging during an attack
        if (rangedPhase == RangedAttackPhase.Forming || rangedPhase == RangedAttackPhase.Locked)
        {
            ComputeFormationSlots(formationOrbitAngle);
            AssignFormationToBoids();
        }
    }

    public List<BoidAgent> RemoveBoids(int count)
    {
        count = Mathf.Min(count, boids.Count);
        List<BoidAgent> removed = new List<BoidAgent>(count);

        // Remove from the end to avoid shifting
        int startIndex = boids.Count - count;
        for (int i = boids.Count - 1; i >= startIndex; i--)
        {
            removed.Add(boids[i]);
            boids.RemoveAt(i);
        }

        return removed;
    }

    public void InitializeFromSplit(BoidSettings sourceSettings, List<BoidAgent> splitBoids)
    {
        settings = sourceSettings;
        AddBoids(splitBoids);
    }

    public Vector3 GetFlockCenter()
    {
        if (boids.Count == 0)
            return transform.position;

        Vector3 center = Vector3.zero;
        for (int i = 0; i < boids.Count; i++)
            center += boids[i].Position;

        return center / boids.Count;
    }

    private void ApplyFlockColor(BoidAgent boid)
    {
        Renderer renderer = boid.GetComponentInChildren<Renderer>();
        if (renderer != null)
            renderer.material.color = settings.flockColor;
    }

    private void LateUpdate()
    {
        float perceptionSqr = settings.perceptionRadius * settings.perceptionRadius;
        float effectiveAvoidance = EffectiveAvoidanceRadius;
        float avoidanceSqr = effectiveAvoidance * effectiveAvoidance;

        for (int i = 0; i < boids.Count; i++)
        {
            BoidAgent boid = boids[i];
            Vector3 separationHeading = Vector3.zero;
            Vector3 alignmentHeading = Vector3.zero;
            Vector3 cohesionCenter = Vector3.zero;
            int neighborCount = 0;

            for (int j = 0; j < boids.Count; j++)
            {
                if (i == j) continue;

                BoidAgent other = boids[j];
                Vector3 offset = other.Position - boid.Position;
                float sqrDist = offset.sqrMagnitude;

                if (sqrDist < perceptionSqr)
                {
                    neighborCount++;
                    alignmentHeading += other.Velocity;
                    cohesionCenter += other.Position;

                    if (sqrDist < avoidanceSqr)
                    {
                        // Weight separation inversely by distance
                        separationHeading -= offset / Mathf.Max(offset.magnitude, 0.001f);
                    }
                }
            }

            if (neighborCount > 0)
            {
                alignmentHeading /= neighborCount;

                // Leader redirect: followers cohese toward leader instead of average neighbor
                if (leaderBoid != null && boid != leaderBoid && leaderBoid.gameObject != null)
                    cohesionCenter = leaderBoid.Position - boid.Position;
                else
                    cohesionCenter = (cohesionCenter / neighborCount) - boid.Position;
            }

            // Cross-flock separation (separation only, no alignment/cohesion)
            Vector3 crossFlockSeparation = Vector3.zero;
            for (int f = 0; f < foreignBoids.Count; f++)
            {
                Vector3 offset = foreignBoids[f].Position - boid.Position;
                float sqrDist = offset.sqrMagnitude;

                if (sqrDist < avoidanceSqr)
                {
                    crossFlockSeparation -= offset / Mathf.Max(offset.magnitude, 0.001f);
                }
            }

            boid.UpdateBoid(separationHeading, alignmentHeading, cohesionCenter, neighborCount, crossFlockSeparation);
        }
    }

    // ── Flock Ranged Attack ──────────────────────────────────────────

    private void UpdateRangedFlockAttack()
    {
        if (flockAttackCooldownTimer > 0f)
            flockAttackCooldownTimer -= Time.deltaTime;

        switch (rangedPhase)
        {
            case RangedAttackPhase.None:
            {
                if (flockAttackCooldownTimer > 0f || boids.Count < 3) break;

                float dist = (target.position - transform.position).magnitude;
                if (dist <= settings.flockAttackTriggerDistance)
                {
                    rangedPhase = RangedAttackPhase.Forming;
                    rangedPhaseTimer = 0f;
                    formationOrbitAngle = 0f;
                    ComputeFormationSlots(0f);
                    AssignFormationToBoids();
                }
                break;
            }

            case RangedAttackPhase.Forming:
            {
                // Recompute slots each frame so ring stays at flock anchor
                ComputeFormationSlots(0f);
                UpdateBoidFormationTargets();

                // Check convergence
                int onSlot = 0;
                for (int i = 0; i < boids.Count; i++)
                {
                    int slotIndex = i % formationSlots.Length;
                    float d = (boids[i].Position - formationSlots[slotIndex]).magnitude;
                    if (d <= settings.formationSlotTolerance)
                        onSlot++;
                }

                float fraction = boids.Count > 0 ? (float)onSlot / boids.Count : 0f;

                // Safety timeout: 6 seconds max for forming
                rangedPhaseTimer += Time.deltaTime;
                if (fraction >= settings.formationConvergeThreshold || rangedPhaseTimer > 6f)
                {
                    rangedPhase = RangedAttackPhase.Locked;
                    rangedPhaseTimer = settings.formationDuration;
                }
                break;
            }

            case RangedAttackPhase.Locked:
            {
                formationOrbitAngle += settings.formationOrbitSpeed * Time.deltaTime;
                ComputeFormationSlots(formationOrbitAngle);
                UpdateBoidFormationTargets();

                rangedPhaseTimer -= Time.deltaTime;
                if (rangedPhaseTimer <= 0f)
                {
                    rangedPhase = RangedAttackPhase.Firing;
                }
                break;
            }

            case RangedAttackPhase.Firing:
            {
                // Spawn a single projectile at the ring center aimed at the player
                if (settings.projectilePrefab != null && target != null)
                {
                    Vector3 center = transform.position;
                    Vector3 dir = (target.position - center).normalized;
                    GameObject proj = Object.Instantiate(
                        settings.projectilePrefab,
                        center,
                        Quaternion.LookRotation(dir)
                    );
                    BoidProjectile bp = proj.GetComponent<BoidProjectile>();
                    bp?.Initialize(dir, settings.projectileSpeed, settings.flockProjectileDamage, target, settings.projectileTurnSpeed);
                }

                // Knockback — push boids outward from ring center
                for (int i = 0; i < boids.Count; i++)
                {
                    Vector3 outward = (boids[i].Position - transform.position).normalized;
                    boids[i].ApplyKnockback(outward * settings.formationKnockbackForce);
                }

                ReleaseBoidFormations();
                rangedPhase = RangedAttackPhase.Recovering;
                rangedPhaseTimer = 0.5f;
                break;
            }

            case RangedAttackPhase.Recovering:
            {
                rangedPhaseTimer -= Time.deltaTime;
                if (rangedPhaseTimer <= 0f)
                {
                    rangedPhase = RangedAttackPhase.None;
                    flockAttackCooldownTimer = settings.flockAttackCooldown;
                }
                break;
            }
        }
    }

    private void ComputeFormationSlots(float orbitOffset = 0f)
    {
        int count = boids.Count;
        if (count == 0) return;

        formationSlots = new Vector3[count];
        float angleStep = 360f / count;
        Vector3 center = transform.position;

        // Build a vertical ring that faces the player
        Vector3 toTarget = target != null ? target.position - center : transform.forward;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f) toTarget = transform.forward;
        toTarget.Normalize();

        Vector3 ringRight = Vector3.Cross(Vector3.up, toTarget).normalized;
        Vector3 ringUp = Vector3.up;

        for (int i = 0; i < count; i++)
        {
            float angle = (angleStep * i + orbitOffset) * Mathf.Deg2Rad;
            formationSlots[i] = center
                + ringRight * Mathf.Cos(angle) * settings.formationRingRadius
                + ringUp * Mathf.Sin(angle) * settings.formationRingRadius;
        }
    }

    private void AssignFormationToBoids()
    {
        if (formationSlots == null) return;
        for (int i = 0; i < boids.Count; i++)
        {
            int slotIndex = i % formationSlots.Length;
            boids[i].SetFormationTarget(formationSlots[slotIndex], true);
        }
    }

    private void UpdateBoidFormationTargets()
    {
        if (formationSlots == null) return;
        for (int i = 0; i < boids.Count; i++)
        {
            int slotIndex = i % formationSlots.Length;
            boids[i].SetFormationTarget(formationSlots[slotIndex], true);
        }
    }

    private void ReleaseBoidFormations()
    {
        for (int i = 0; i < boids.Count; i++)
            boids[i].SetFormationTarget(Vector3.zero, false);
    }

    // ── Flock Melee Attack ────────────────────────────────────────

    private void UpdateMeleeFlockAttack()
    {
        if (flockMeleeCooldownTimer > 0f)
            flockMeleeCooldownTimer -= Time.deltaTime;

        switch (meleePhase)
        {
            case MeleeAttackPhase.None:
            {
                if (flockMeleeCooldownTimer > 0f || boids.Count < 2) break;

                float dist = (target.position - transform.position).magnitude;
                if (dist <= settings.meleeFlockTriggerDistance)
                {
                    meleePhase = MeleeAttackPhase.WindUp;
                    meleePhaseTimer = settings.attackWindUpDuration;
                    meleeWaveDamageDealt = false;
                    for (int i = 0; i < boids.Count; i++)
                        boids[i].BeginMeleeWindUp();
                }
                break;
            }

            case MeleeAttackPhase.WindUp:
            {
                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.Charging;
                    meleePhaseTimer = settings.attackSweepDuration;
                    for (int i = 0; i < boids.Count; i++)
                        boids[i].BeginInfinitySweep();
                }
                break;
            }

            case MeleeAttackPhase.Charging:
            {
                if (!meleeWaveDamageDealt && target != null)
                {
                    for (int i = 0; i < boids.Count; i++)
                    {
                        float dist = (target.position - boids[i].Position).magnitude;
                        if (dist <= settings.attackContactDistance)
                        {
                            target.GetComponent<PlayerHealth>()?.TakeDamage(settings.attackDamage);
                            meleeWaveDamageDealt = true;
                            break;
                        }
                    }
                }

                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.Recovering;
                    meleePhaseTimer = settings.meleeRecoveryDuration;
                    for (int i = 0; i < boids.Count; i++)
                        boids[i].EndMeleeAttack();
                }
                break;
            }

            case MeleeAttackPhase.Recovering:
            {
                meleePhaseTimer -= Time.deltaTime;
                if (meleePhaseTimer <= 0f)
                {
                    meleePhase = MeleeAttackPhase.None;
                    flockMeleeCooldownTimer = settings.meleeFlockCooldown;
                }
                break;
            }
        }
    }

    private void ResetMeleeAttack()
    {
        if (meleePhase == MeleeAttackPhase.None) return;
        for (int i = 0; i < boids.Count; i++)
            boids[i].EndMeleeAttack();
        meleePhase = MeleeAttackPhase.None;
    }

    // ── Gizmos ──────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        if (!drawGizmos || settings == null) return;

        // Boundary sphere
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, EffectiveBoundaryRadius);

        // Spawn area
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, settings.spawnRadius);

        // Aggro radius
        if (settings.aggroRadius > 0f)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, settings.aggroRadius);
        }

        // Formation ring (during ranged attack)
        if (rangedPhase != RangedAttackPhase.None && settings.flockType == FlockType.Ranged)
        {
            Gizmos.color = rangedPhase == RangedAttackPhase.Locked
                ? new Color(1f, 0.5f, 0f, 0.6f)
                : new Color(0f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, settings.formationRingRadius);

            if (formationSlots != null)
            {
                for (int i = 0; i < formationSlots.Length; i++)
                    Gizmos.DrawSphere(formationSlots[i], 0.3f);
            }
        }
    }
}
