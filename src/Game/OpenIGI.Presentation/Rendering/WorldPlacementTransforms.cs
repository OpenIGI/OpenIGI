using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Rendering;
using System.Runtime.InteropServices;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Where and as what each placement is shown this frame: the variant it wears, the destroyed model it
/// swaps to, and the door, lift, cutscene, rotor, swivel and stand transforms that move it.
/// </summary>
/// <remarks>
/// One precedence for every reader -- the frame's resolve, the selection outline and the decal and
/// mark poses -- so none of them can drift from what the draw shows.
/// </remarks>
internal sealed class WorldPlacementTransforms
{
    /// <summary>Gets or sets the live door transforms applied to otherwise static placements.</summary>
    internal DoorRegistry? Doors { get; set; }

    /// <summary>Gets or sets the live elevator transforms applied to lift placements.</summary>
    internal ElevatorRegistry? Elevators { get; set; }

    /// <summary>Gets or sets the live destructible alarm objects that select destroyed model variants.</summary>
    internal AlarmSpeakerRegistry? AlarmSpeakers { get; set; }

    /// <summary>Gets or sets the live retail glass and explosive-object presentation state.</summary>
    internal DestructibleObjectRegistry? Destructibles { get; set; }

    /// <summary>Gets or sets the live articulated security-camera transforms.</summary>
    internal SecurityCameraRegistry? SecurityCameras { get; set; }

    /// <summary>Gets or sets the title's turning placements, fans and radar heads.</summary>
    internal IPlacementSpin? Spins { get; set; }

    /// <summary>Gets or sets the title's moving placements, the doors it swings.</summary>
    internal IPlacementMotion? Motions { get; set; }

    /// <summary>Gets or sets the title's destroyed placements.</summary>
    internal IPlacementDestruction? Destruction { get; set; }

    /// <summary>Gets or sets which variant each placement wears.</summary>
    internal IPlacementVariant? Variants { get; set; }

    /// <summary>Gets or sets the authored cinematic runtime that supplies live vehicle transforms.</summary>
    internal CutsceneRuntime? Cutscenes { get; set; }

    /// <summary>Gets or sets the level's interactions: turning gun stands and the charges a plant has moved.</summary>
    internal WorldInteractionRegistry? Interactions { get; set; }

    /// <summary>Gets or sets the live rotating world objects (radar dishes and rotating tasks).</summary>
    internal RotatingObjectRegistry? RotatingObjects { get; set; }

    /// <summary>Gets or sets the frame's position between the last two ticks, 0–1.</summary>
    internal float FrameAlpha { get; set; } = 1f;

    /// <summary>Gets or sets whether the frame's transforms are interpolated.</summary>
    internal bool FrameSmooth { get; set; }

    // A StationaryGun's stand turns with its gun: 0x474420 writes the traverse into the task's own
    // orientation (+112, the authored one times Z(traverse)), and that is what the stand model is drawn
    // with. Only the traverse -- the elevation belongs to the gun alone -- so the yoke stays square to
    // the gun it holds.
    private bool TryGetStandOrientation(WorldPlacement placement, out Matrix3f orientation)
    {
        // The type test first: TryGet walks the level's interactions, and this runs per placement.
        if (placement.Owner is { } owner
            && string.Equals(owner.Type.Name, "StationaryGun", StringComparison.Ordinal)
            && Interactions?.TryGet(owner, out WorldInteraction? interaction) == true
            && interaction?.StationaryGun is { } gun)
        {
            orientation = gun.TurnedOrientation;
            return true;
        }

        orientation = default;
        return false;
    }

    // Which of its variants a placement wears, or -1 for the model the walk placed: an index the title gives, whose binding resolved.
    internal int VariantOf(WorldPlacement placement) =>
        placement.Variants is { } variants
        && Variants?.VariantOf(placement) is int variant
        && variant >= 0
        && variant < variants.Count
        && variants[variant] is not null
            ? variant
            : -1;
    /// <summary>The placement as it is shown this frame: its worn variant, its destroyed model and its live transforms.</summary>
    internal WorldPlacement DisplayedPlacement(WorldPlacement placement)
    {
        WorldPlacement displayed = placement;
        if (VariantOf(placement) is >= 0 and int worn && placement.Variants![worn] is { } variantBinding)
        {
            displayed = placement with { Instance = variantBinding.Instance, Pack = variantBinding.Pack, Model = variantBinding.Model };
        }

        if (placement.DestroyedReplacement is { } replacement && UsesDestroyedReplacement(placement))
        {
            displayed = placement with
            {
                Instance = replacement.Instance,
                Pack = replacement.Pack,
                Model = replacement.Model,
            };
        }

        return ApplyLiveTransforms(placement, displayed);
    }

    /// <summary>Applies the per-frame transforms that turn or move a placement but never change its model.</summary>
    /// <param name="placement">The authored placement the live state is keyed by.</param>
    /// <param name="displayed">The placement as displayed so far.</param>
    /// <returns><paramref name="displayed"/>, swivelled, spun, swung and turned on its stand as this frame has it.</returns>
    internal WorldPlacement ApplyLiveTransforms(WorldPlacement placement, WorldPlacement displayed)
    {
        // The security camera's swivel changes the orientation but never the model, so the
        // displayed placement cached above stays right; only the transform is per-frame.
        if (SecurityCameras?.TryGetHeadOrientation(
            placement,
            FrameAlpha,
            FrameSmooth,
            out Matrix3f orientation) == true)
        {
            displayed = displayed with { Orientation = orientation };
        }

        // A fan or a radar head, which turns for ever and never leaves its spot. Same reasoning as
        // the camera above: the model and the position are settled, only the transform is per-frame.
        if (Spins?.TryGetOrientation(placement, FrameAlpha, out Matrix3f spun) == true)
        {
            displayed = displayed with { Orientation = spun };
        }

        // A door a title swings: it leaves its spot as well as turning.
        if (Motions?.TryGetTransform(placement, FrameAlpha, out Vector3d moved, out Matrix3f movedOrientation) == true)
        {
            displayed = displayed with { Position = moved, Orientation = movedOrientation };
        }

        if (TryGetStandOrientation(placement, out Matrix3f stand))
        {
            displayed = displayed with { Orientation = stand };
        }

        return displayed;
    }

    /// <summary>Whether the renderer must swap an authored placement for its destroyed-model binding.</summary>
    internal bool UsesDestroyedReplacement(WorldPlacement placement) =>
        placement.DestroyedReplacement is not null
        && (AlarmSpeakers?.IsDestroyed(placement.Owner) == true
            || Destructibles?.IsDestroyed(placement) == true
            || Destruction?.IsDestroyed(placement) == true);

    /// <summary>
    /// Works out where a placement is drawn this frame, after everything that can move it has moved it.
    /// </summary>
    /// <param name="placement">The placement being drawn.</param>
    /// <param name="drawPosition">Receives the position to draw at.</param>
    /// <param name="drawOrientation">Receives the orientation to draw with.</param>
    /// <param name="rotorDraw">Receives the motion-blurred rotor state, when one applies.</param>
    /// <param name="hasRotorDraw">Receives whether a rotor state applies.</param>
    /// <remarks>
    /// A door swings, an elevator carries what stands on it, a cutscene can seize either, and a rotor
    /// smears across the frame. Pulled out of the draw loop so the selection outline resolves the same
    /// transform from the same code: an outline computed a second way would drift off its subject the
    /// moment any of these took hold, which is exactly when a reader is watching it.
    /// </remarks>
    internal void ResolveDrawTransform(
        WorldPlacement placement,
        out Vector3d drawPosition,
        out Matrix3f drawOrientation,
        out RotorDrawState rotorDraw,
        out bool hasRotorDraw)
    {
        drawPosition = placement.Position;
        drawOrientation = placement.Orientation;
        rotorDraw = default;
        hasRotorDraw = false;

        if (Doors?.TryGetDrawTransform(
            placement.Owner,
            FrameAlpha,
            FrameSmooth,
            out Vector3d doorPosition,
            out Matrix3f doorOrientation) == true)
        {
            drawPosition = doorPosition;
            drawOrientation = doorOrientation;
            if (Elevators?.TryGetDisplacement(
                placement.Owner,
                FrameAlpha,
                FrameSmooth,
                out Vector3d doorDisplacement) == true)
            {
                drawPosition += doorDisplacement;
            }
        }
        else if (Elevators?.TryGetDrawTransform(
            placement.Owner,
            placement.Position,
            placement.Orientation,
            FrameAlpha,
            FrameSmooth,
            out Vector3d elevatorPosition,
            out Matrix3f elevatorOrientation) == true)
        {
            drawPosition = elevatorPosition;
            drawOrientation = elevatorOrientation;
        }
        else if (Cutscenes?.TryGetRotorDrawState(placement, out rotorDraw) == true)
        {
            drawPosition = rotorDraw.Position;
            hasRotorDraw = true;
        }
        else if (Cutscenes?.TryGetPlacementDrawTransform(
            placement,
            out Vector3d cinematicPosition,
            out Matrix3f cinematicOrientation) == true)
        {
            drawPosition = cinematicPosition;
            drawOrientation = cinematicOrientation;
        }
        else if (RotatingObjects?.TryGetDrawTransform(
            placement,
            FrameAlpha,
            FrameSmooth,
            out Vector3d rotatingPosition,
            out Matrix3f rotatingOrientation) == true)
        {
            drawPosition = rotatingPosition;
            drawOrientation = rotatingOrientation;
        }
        else if (Interactions?.TryGetPlacedPose(
            placement.Owner,
            out Vector3d placedUnits,
            out Matrix3f placedOrientation) == true)
        {
            // A planted charge: state 36 moved its EditRigidObj to where the plant's trace struck.
            drawPosition = placedUnits / WorldScene.UnitsPerMetre;
            drawOrientation = placedOrientation;
        }
    }

    // The transform a placement is drawn with this frame, through the same precedence the draw loop
    // and the selection outline use. A rotor has no single orientation mid-blur; nothing on one takes a
    // mark (0x42D416), so its authored orientation stands.
    internal bool TryGetDrawTransform(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ResolveDrawTransform(DisplayedPlacement(placement), out positionMetres, out orientation, out _, out _);
        return true;
    }
}
