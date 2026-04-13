using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.Profiling;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Central manager for switching between the four experimental agent conditions.
/// Handles agent spawning, scene configuration, and metrics collection per condition.
///
/// Usage: Attach to a GameObject in the scene, configure settings, set CurrentCondition.
/// </summary>
public class ConditionManager : MonoBehaviour
{
    public static ConditionManager Instance { get; private set; }

    /// <summary>
    /// True when the active condition uses GOAP for boid-level combat (BOIDSWithGOAPLeader).
    /// Used by BoidAgent and FlockManager to decide FSM vs GOAP path.
    /// </summary>
    public bool UsesGoapForBoids => CurrentCondition == AgentCondition.BOIDSWithGOAPLeader;

    [Header("Condition Selection")]
    [Tooltip("The currently active experimental condition. Change this to switch AI architectures.")]
    public AgentCondition CurrentCondition = AgentCondition.PureGOAP;

    [Header("Agent Configuration")]
    [Tooltip("Number of agents to spawn (used by PureGOAP and hybrid conditions).")]
    [SerializeField] private int agentCount = 10;

    [Tooltip("Spawn radius around the manager's position.")]
    [SerializeField] private float spawnRadius = 15f;

    [Tooltip("Fraction of agents that are ranged (0 = all melee, 1 = all ranged).")]
    [Range(0f, 1f)]
    [SerializeField] private float rangedAgentRatio = 0.3f;

    [Header("PureGOAP Prefab")]
    [SerializeField] private GameObject pureGOAPAgentPrefab;

    [Header("PureBOIDS Prefabs (FlockManagers)")]
    [Tooltip("FlockManager prefab with MeleeBoidSettings assigned.")]
    [SerializeField] private GameObject meleeFlockManagerPrefab;
    [Tooltip("FlockManager prefab with RangedBoidSettings assigned. Leave empty for melee-only.")]
    [SerializeField] private GameObject rangedFlockManagerPrefab;

    [Header("Hybrid Prefabs (Future)")]
    [SerializeField] private GameObject boidsWithGOAPLeaderPrefab;
    [SerializeField] private GameObject goapWithBOIDSMovementPrefab;

    [Header("Runtime State")]
    [SerializeField] private List<GameObject> spawnedAgents = new List<GameObject>();

    [Header("GOAP Reference")]
    [Tooltip("The GoapBehaviour in the scene (GOAP Manager).")]
    [SerializeField] private GoapBehaviour goapBehaviour;

    [Header("Player Reference")]
    [Tooltip("Reference to the player transform (target for all agents).")]
    public Transform playerTransform;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        SpawnAgentsForCondition();
    }

    /// <summary>
    /// Spawns agents for the currently selected condition.
    /// Clears any existing agents first.
    /// </summary>
    public void SpawnAgentsForCondition()
    {
        Profiler.BeginSample("ConditionManager.SpawnAgentsForCondition");

        ClearAgents();

        switch (CurrentCondition)
        {
            case AgentCondition.PureBOIDS:
                SpawnPureBOIDSFlocks();
                break;

            case AgentCondition.BOIDSWithGOAPLeader:
                StartCoroutine(SpawnBOIDSWithGOAPLeaderFlocks());
                break;

            case AgentCondition.GOAPWithBOIDSMovement:
                SpawnGOAPWithBOIDSMovementFlocks();
                break;

            default:
                SpawnIndividualAgents();
                break;
        }

        Debug.Log($"[ConditionManager] Spawned agents for condition: {CurrentCondition}");
        Profiler.EndSample();
    }

    /// <summary>
    /// Spawns FlockManager(s) for PureBOIDS condition.
    /// Each FlockManager spawns its own boids internally via Start().
    /// </summary>
    private void SpawnPureBOIDSFlocks()
    {
        if (meleeFlockManagerPrefab != null)
        {
            GameObject melee = Instantiate(meleeFlockManagerPrefab, transform.position, Quaternion.identity, transform);
            melee.name = "PureBOIDS_MeleeFlockManager";
            spawnedAgents.Add(melee);
        }

        if (rangedFlockManagerPrefab != null)
        {
            // Offset ranged flock slightly so they don't overlap at spawn
            Vector3 rangedPos = transform.position + Vector3.right * spawnRadius * 0.5f;
            GameObject ranged = Instantiate(rangedFlockManagerPrefab, rangedPos, Quaternion.identity, transform);
            ranged.name = "PureBOIDS_RangedFlockManager";
            spawnedAgents.Add(ranged);
        }

        if (meleeFlockManagerPrefab == null && rangedFlockManagerPrefab == null)
        {
            Debug.LogError("[ConditionManager] No FlockManager prefabs assigned for PureBOIDS condition!");
        }
    }

    /// <summary>
    /// Spawns FlockManager(s) for BOIDSWithGOAPLeader condition.
    /// Uses a coroutine to wait one frame so FlockManager.Start() finishes spawning boids,
    /// then designates boid[0] as the GOAP leader.
    /// </summary>
    private IEnumerator SpawnBOIDSWithGOAPLeaderFlocks()
    {
        // Spawn FlockManagers (same prefabs as PureBOIDS)
        List<FlockManager> managers = new List<FlockManager>();

        if (meleeFlockManagerPrefab != null)
        {
            GameObject melee = Instantiate(meleeFlockManagerPrefab, transform.position, Quaternion.identity, transform);
            melee.name = "LeaderBOIDS_MeleeFlockManager";
            spawnedAgents.Add(melee);
            managers.Add(melee.GetComponent<FlockManager>());
        }

        if (rangedFlockManagerPrefab != null)
        {
            Vector3 rangedPos = transform.position + Vector3.right * spawnRadius * 0.5f;
            GameObject ranged = Instantiate(rangedFlockManagerPrefab, rangedPos, Quaternion.identity, transform);
            ranged.name = "LeaderBOIDS_RangedFlockManager";
            spawnedAgents.Add(ranged);
            managers.Add(ranged.GetComponent<FlockManager>());
        }

        // Wait one frame for FlockManager.Start() → SpawnFlock() to complete
        yield return null;

        // Designate boid[0] in each flock as leader
        foreach (var mgr in managers)
        {
            if (mgr == null || mgr.Boids.Count == 0) continue;

            BoidAgent leader = mgr.Boids[0];
            mgr.SetLeader(leader);

            // Attach leader GOAP brain (disable the default BoidGoapBrain to avoid conflicts)
            var defaultBrain = leader.GetComponent<BoidGoapBrain>();
            if (defaultBrain != null)
                defaultBrain.enabled = false;

            leader.gameObject.AddComponent<LeaderGoapBrain>();

            // Wire GOAP agent type
            var provider = leader.GetComponent<GoapActionProvider>();
            if (provider != null && goapBehaviour != null)
            {
                provider.AgentType = goapBehaviour.GetAgentType("LeaderBoid");
            }

            // Visual distinction: gold color, slightly larger
            Renderer rend = leader.GetComponentInChildren<Renderer>();
            if (rend != null)
                rend.material.color = new Color(1f, 0.84f, 0f); // Gold

            leader.transform.localScale *= 1.3f;

            Debug.Log($"[ConditionManager] Designated leader in {mgr.name}: {leader.name}");
        }
    }

    /// <summary>
    /// Spawns individual agents for conditions that use per-agent prefabs (PureGOAP, hybrids).
    /// </summary>
    private void SpawnIndividualAgents()
    {
        GameObject prefab = GetPrefabForCondition(CurrentCondition);
        if (prefab == null)
        {
            Debug.LogError($"[ConditionManager] No prefab assigned for condition: {CurrentCondition}");
            return;
        }

        for (int i = 0; i < agentCount; i++)
        {
            Vector3 spawnPos = transform.position + GetSpawnOffset(i);
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject agent = Instantiate(prefab, spawnPos, spawnRot, transform);
            agent.name = $"{CurrentCondition}_Agent_{i}";

            InitializeAgentForCondition(agent, CurrentCondition);

            spawnedAgents.Add(agent);
        }
    }

    /// <summary>
    /// Spawns GOAPWithBOIDSMovement agents as separate melee and ranged flocks.
    /// Each flock has its own flockId so BOIDS forces only apply within the flock.
    /// </summary>
    private void SpawnGOAPWithBOIDSMovementFlocks()
    {
        GameObject prefab = goapWithBOIDSMovementPrefab;
        if (prefab == null)
        {
            Debug.LogError("[ConditionManager] No prefab assigned for GOAPWithBOIDSMovement!");
            return;
        }

        int rangedCount = Mathf.RoundToInt(agentCount * rangedAgentRatio);
        int meleeCount = agentCount - rangedCount;

        // Spawn melee flock (flockId = 0)
        for (int i = 0; i < meleeCount; i++)
        {
            Vector3 spawnPos = transform.position + GetSpawnOffset(i);
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject agent = Instantiate(prefab, spawnPos, spawnRot, transform);
            agent.name = $"GOAPBoid_Melee_{i}";

            var gbAgent = agent.GetComponent<GOAPBoidAgent>();
            if (gbAgent != null)
            {
                gbAgent.SetAttackType(AttackType.Melee);
                gbAgent.flockId = 0;
            }

            var gbProvider = agent.GetComponent<GoapActionProvider>();
            if (gbProvider != null && goapBehaviour != null)
                gbProvider.AgentType = goapBehaviour.GetAgentType("GOAPBoidAgent");

            spawnedAgents.Add(agent);
        }

        // Spawn ranged flock (flockId = 1), offset from melee
        Vector3 rangedOffset = Vector3.right * spawnRadius * 0.5f;
        for (int i = 0; i < rangedCount; i++)
        {
            Vector3 spawnPos = transform.position + rangedOffset + GetSpawnOffset(i);
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            GameObject agent = Instantiate(prefab, spawnPos, spawnRot, transform);
            agent.name = $"GOAPBoid_Ranged_{i}";

            var gbAgent = agent.GetComponent<GOAPBoidAgent>();
            if (gbAgent != null)
            {
                gbAgent.SetAttackType(AttackType.Ranged);
                gbAgent.flockId = 1;
            }

            var gbProvider = agent.GetComponent<GoapActionProvider>();
            if (gbProvider != null && goapBehaviour != null)
                gbProvider.AgentType = goapBehaviour.GetAgentType("GOAPBoidAgent");

            spawnedAgents.Add(agent);
        }

        Debug.Log($"[ConditionManager] Spawned GOAPWithBOIDSMovement: {meleeCount} melee (flock 0), {rangedCount} ranged (flock 1)");
    }

    /// <summary>
    /// Clears all currently spawned agents and flocks.
    /// </summary>
    public void ClearAgents()
    {
        foreach (GameObject agent in spawnedAgents)
        {
            if (agent != null)
                Destroy(agent);
        }
        spawnedAgents.Clear();
    }

    private GameObject GetPrefabForCondition(AgentCondition condition)
    {
        return condition switch
        {
            AgentCondition.PureGOAP => pureGOAPAgentPrefab,
            AgentCondition.BOIDSWithGOAPLeader => boidsWithGOAPLeaderPrefab,
            AgentCondition.GOAPWithBOIDSMovement => goapWithBOIDSMovementPrefab,
            _ => null
        };
    }

    private void InitializeAgentForCondition(GameObject agent, AgentCondition condition)
    {
        switch (condition)
        {
            case AgentCondition.PureGOAP:
                var provider = agent.GetComponent<GoapActionProvider>();
                if (provider != null && goapBehaviour != null)
                {
                    provider.AgentType = goapBehaviour.GetAgentType("PureGOAPAgent");
                }
                // Assign melee/ranged based on ratio
                var pureAgent = agent.GetComponent<PureGOAPAgent>();
                if (pureAgent != null)
                {
                    int rangedCount = Mathf.RoundToInt(agentCount * rangedAgentRatio);
                    bool isRanged = spawnedAgents.Count < rangedCount;
                    pureAgent.SetAttackType(isRanged ? AttackType.Ranged : AttackType.Melee);
                }
                break;

            case AgentCondition.BOIDSWithGOAPLeader:
                // Handled by SpawnBOIDSWithGOAPLeaderFlocks() coroutine — not individual agents
                break;

            case AgentCondition.GOAPWithBOIDSMovement:
                var gbProvider = agent.GetComponent<GoapActionProvider>();
                if (gbProvider != null && goapBehaviour != null)
                {
                    gbProvider.AgentType = goapBehaviour.GetAgentType("GOAPBoidAgent");
                }
                var gbAgent = agent.GetComponent<GOAPBoidAgent>();
                if (gbAgent != null)
                {
                    int rangedCount = Mathf.RoundToInt(agentCount * rangedAgentRatio);
                    bool isRanged = spawnedAgents.Count < rangedCount;
                    gbAgent.SetAttackType(isRanged ? AttackType.Ranged : AttackType.Melee);
                }
                break;
        }
    }

    private Vector3 GetSpawnOffset(int index)
    {
        float angle = (360f / agentCount) * index * Mathf.Deg2Rad;
        float randomRadius = Random.Range(spawnRadius * 0.5f, spawnRadius);

        return new Vector3(
            Mathf.Cos(angle) * randomRadius,
            0f,
            Mathf.Sin(angle) * randomRadius
        );
    }

    private void OnValidate()
    {
        agentCount = Mathf.Clamp(agentCount, 1, 100);
    }

    #if UNITY_EDITOR
    [ContextMenu("Spawn Agents for Current Condition")]
    private void EditorSpawnAgents()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ConditionManager] Can only spawn agents during Play Mode.");
            return;
        }
        SpawnAgentsForCondition();
    }

    [ContextMenu("Clear All Agents")]
    private void EditorClearAgents()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ConditionManager] Can only clear agents during Play Mode.");
            return;
        }
        ClearAgents();
    }
    #endif
}
