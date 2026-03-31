using CrashKonijn.Goap.Runtime;
using UnityEngine;
using UnityEngine.Profiling;
using System.Collections.Generic;

/// <summary>
/// Central manager for switching between the four experimental agent conditions.
/// Handles agent spawning, scene configuration, and metrics collection per condition.
///
/// Usage: Attach to a GameObject in the scene, configure settings, set CurrentCondition.
/// </summary>
public class ConditionManager : MonoBehaviour
{
    [Header("Condition Selection")]
    [Tooltip("The currently active experimental condition. Change this to switch AI architectures.")]
    public AgentCondition CurrentCondition = AgentCondition.PureGOAP;

    [Header("Agent Configuration")]
    [Tooltip("Number of agents to spawn for this test run.")]
    [SerializeField] private int agentCount = 10;

    [Tooltip("Spawn radius around the manager's position.")]
    [SerializeField] private float spawnRadius = 15f;

    [Header("Prefabs (One per Condition)")]
    [SerializeField] private GameObject pureGOAPAgentPrefab;
    [SerializeField] private GameObject pureBOIDSAgentPrefab;
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

    /// <summary>
    /// Spawns agents for the currently selected condition.
    /// Clears any existing agents first.
    /// </summary>
    public void SpawnAgentsForCondition()
    {
        Profiler.BeginSample("ConditionManager.SpawnAgentsForCondition");

        // Clear existing agents
        ClearAgents();

        GameObject prefab = GetPrefabForCondition(CurrentCondition);
        if (prefab == null)
        {
            Debug.LogError($"[ConditionManager] No prefab assigned for condition: {CurrentCondition}");
            Profiler.EndSample();
            return;
        }

        // Spawn agents in a circle around the manager
        for (int i = 0; i < agentCount; i++)
        {
            Vector3 spawnPos = transform.position + GetSpawnOffset(i);
            Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            GameObject agent = Instantiate(prefab, spawnPos, spawnRot, transform);
            agent.name = $"{CurrentCondition}_Agent_{i}";

            InitializeAgentForCondition(agent, CurrentCondition);

            spawnedAgents.Add(agent);
        }

        Debug.Log($"[ConditionManager] Spawned {agentCount} agents for condition: {CurrentCondition}");
        Profiler.EndSample();
    }

    /// <summary>
    /// Clears all currently spawned agents.
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

    /// <summary>
    /// Returns the prefab for the specified condition.
    /// </summary>
    private GameObject GetPrefabForCondition(AgentCondition condition)
    {
        return condition switch
        {
            AgentCondition.PureGOAP => pureGOAPAgentPrefab,
            AgentCondition.PureBOIDS => pureBOIDSAgentPrefab,
            AgentCondition.BOIDSWithGOAPLeader => boidsWithGOAPLeaderPrefab,
            AgentCondition.GOAPWithBOIDSMovement => goapWithBOIDSMovementPrefab,
            _ => null
        };
    }

    /// <summary>
    /// Performs condition-specific initialization on the spawned agent.
    /// </summary>
    private void InitializeAgentForCondition(GameObject agent, AgentCondition condition)
    {
        // Common: set player reference if agent has a component that needs it
        // Condition-specific initialization will be handled by the agent's own Awake/Start

        switch (condition)
        {
            case AgentCondition.PureGOAP:
                var provider = agent.GetComponent<GoapActionProvider>();
                if (provider != null && goapBehaviour != null)
                {
                    provider.AgentType = goapBehaviour.GetAgentType("PureGOAPAgent");
                }
                break;

            case AgentCondition.PureBOIDS:
                // PureBOIDSAgent initializes itself
                break;

            case AgentCondition.BOIDSWithGOAPLeader:
                // Mark first agent as leader
                if (spawnedAgents.Count == 0)
                {
                    // This is the first agent — make it the leader
                    var leaderComponent = agent.AddComponent<FlockLeader>();
                    // Leader component will handle its own initialization
                }
                break;

            case AgentCondition.GOAPWithBOIDSMovement:
                // Hybrid agent initializes itself
                break;
        }
    }

    /// <summary>
    /// Calculates spawn offset for agent i in a circular pattern.
    /// </summary>
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
        // Clamp agent count to reasonable range
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
