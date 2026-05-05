using UnityEditor;
using UnityEngine;

/// <summary>
/// Toggle for the FlockManager.DiagnosticLogging flag — investigates the
/// 20-agent equilibrium floor seen in BOIDSWithGOAPLeader (see
/// wiki/methodology-revisions-2026-04.md item 3a result, point 3).
///
/// When ON, each FlockManager logs:
///   - one [FloorDiag/Spawn] line per flock at Start with originalFlockSize,
///     overrides, EffectiveMaxHP, and computed floor.
///   - [FloorDiag/Cull] each time SyncBoidCountToHealth picks a new target,
///     showing hp%, originalFlockSize, and delta to be culled.
///   - [FloorDiag/Wipe] when KillAllBoids fires, showing boidsAtWipe + hp.
///
/// Remove this menu and the flag once the root cause is pinned down.
/// </summary>
public static class FloorDiagnosticMenu
{
    private const string MenuPath = "Thesis/Toggle Floor Diagnostic";

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        FlockManager.DiagnosticLogging = !FlockManager.DiagnosticLogging;
        Debug.Log($"[FloorDiag] FlockManager.DiagnosticLogging = {FlockManager.DiagnosticLogging}");
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, FlockManager.DiagnosticLogging);
        return true;
    }
}
