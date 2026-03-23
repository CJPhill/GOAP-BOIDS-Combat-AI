using UnityEngine;

/// <summary>
/// Singleton toggle that switches all boids between the legacy FSM and GOAP at runtime.
/// Add to a "AI Toggle" GameObject in the scene. Flip the checkbox in the Inspector during Play Mode.
/// </summary>
public class GoapToggle : MonoBehaviour
{
    public static GoapToggle Instance { get; private set; }

    [Tooltip("When true, boids use GOAP for attack decisions. When false, the legacy state machine is used.")]
    public bool useGoap;

    public static bool UseGoap => Instance != null && Instance.useGoap;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
