using System.Text.RegularExpressions;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>Classifies humans staged by an authored cutscene rather than gameplay AI.</summary>
public static partial class CinematicActor
{
    /// <summary>Returns whether a task belongs to an authored intro/outro cutscene subtree.</summary>
    public static bool IsStaged(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        for (GameTask? ancestor = task.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is CutSceneTask)
            {
                return true;
            }

            // QVM parents cabin occupants under the airframe (M4 Heli 356 minds 1234–1238,
            // Heli 800 minds 540–544). That ancestry is the ride contract, not a label.
            if (IsVehicle(ancestor))
            {
                return true;
            }

            if (ancestor is ConditionalContainerTask container
                && (IsCutsceneContainer(container.Label)
                    || ActiveCutsceneCondition().IsMatch(container.Condition)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Vehicles whose child Humans inherit the ride (Heli / Plane / Train / Car).</summary>
    public static bool IsVehicle(GameTask task) =>
        task is DeclaredTask declared && declared.Type?.Name is "Train" or "Heli" or "Plane" or "Car";

    /// <summary>Whether a ground contact with this task can parent a staged Human.</summary>
    /// <remarks>
    /// Retail <c>Human_GroundCollision</c> (<c>IGI.EXE 0x462390</c>) tests the contacted task's
    /// moving-platform flag (<c>task+0x64</c>) before calling <c>Human_SetParent</c>
    /// (<c>0x463310</c>): a non-platform contact detaches through <c>0x463550</c> instead, and an
    /// already-parented Human keeps its slot without recapture. The flag is type-derived once at
    /// task init (init zeroes it, then <c>TaskType_IsDerivedFrom</c> at <c>0x401CF0</c> sets it),
    /// so it is immutable per task family. Boundaries confirmed in both Ghidra and r2; the
    /// <c>igi.pdb</c> (IGI1) publics independently confirm neighboring owners
    /// (<c>Human_DamageApply</c> at <c>0x412440</c>, <c>Human_RunUpdate</c>, <c>Human_ViewCam</c>).
    /// Port mapping, forced by the authored Level 1 landing (<c>land_ground_2</c> at
    /// <c>CutScene_1201.nTick &gt; 0.25*GAME_FREQUENCY</c> onto the passing wagon while the
    /// insertion heli hovers 6 m above it): <c>Train</c>/<c>Car</c>/<c>Elevator</c> are platforms;
    /// <c>Heli</c>/<c>Plane</c> airframes never capture through ground contact (their riders are
    /// direct QVM children, which bypass the ground pass entirely).
    /// </remarks>
    public static bool IsMovingPlatform(GameTask task) =>
        task is DeclaredTask declared && declared.Type?.Name is "Train" or "Car" or "Elevator";

    private static bool IsCutsceneContainer(string label) =>
        label.Contains("cutscene", StringComparison.OrdinalIgnoreCase)
        || label.Contains("cut scene", StringComparison.OrdinalIgnoreCase)
        || label.Contains("cut-scene", StringComparison.OrdinalIgnoreCase)
        || label.Contains("Friendly Forces", StringComparison.OrdinalIgnoreCase)
        || label.Contains("Guys running for heli", StringComparison.OrdinalIgnoreCase)
        || label.Contains("Harrison & Men in Heli", StringComparison.OrdinalIgnoreCase);

    // OBJECTS.QVM is authoritative; editor labels are not. Level 3 spells its parent
    // "Intro cutscnee", while the child explicitly requires CutScene_702.isRun.
    // A completion-only gate does not make ordinary post-intro gameplay cinematic.
    [GeneratedRegex(@"(?:^|[(&|])\s*CutScene_[0-9]+\.(?:isRun|nTick)\b|!\s*CutScene_[0-9]+\.isFinished\b", RegexOptions.CultureInvariant)]
    private static partial Regex ActiveCutsceneCondition();
}
