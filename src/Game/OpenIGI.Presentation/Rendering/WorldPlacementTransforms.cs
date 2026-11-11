using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Where and as what each placement is shown this frame: the variant it wears, the destroyed model it
/// swaps to, and the door, lift, cutscene, rotor, swivel and stand transforms that move it.
/// </summary>
/// <remarks>
/// <para>
/// One precedence for every reader -- the frame's resolve, the selection outline and the decal and
/// mark poses -- so none of them can drift from what the draw shows.
/// </para>
/// <para>
/// It knows no door, lift or camera: what a title's objects do to a placement it asks of the title
/// (<see cref="IPlacementObjects"/>, and the narrower contracts beside it).
/// </para>
/// </remarks>
internal sealed class WorldPlacementTransforms
{
    private IPlacementObjects? _objects;

    /// <summary>Gets or sets the title's objects that move, turn, break and take away placements.</summary>
    /// <remarks>Setting it is a claim made or let go, whichever objects it now answers for.</remarks>
    internal IPlacementObjects? Objects
    {
        get => _objects;
        set => (_objects, _) = (value, Claimed());
    }

    /// <summary>Gets or sets the placements that turn for ever where they stand.</summary>
    internal IPlacementSpin? Spins { get; set; }

    /// <summary>Gets or sets where a title's own objects move what they placed.</summary>
    internal IPlacementMotion? Motions { get; set; }

    /// <summary>Gets or sets which of a title's own placements are destroyed.</summary>
    internal IPlacementDestruction? Destruction { get; set; }

    /// <summary>Gets or sets which of a title's own placements are in the world.</summary>
    internal IPlacementPresence? Presence { get; set; }

    /// <summary>Gets the placements a blast has shattered into pieces.</summary>
    internal HashSet<WorldPlacement> Shattered { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets or sets which of their models a title's own placements wear.</summary>
    internal IPlacementVariant? Variants { get; set; }

    /// <summary>Gets or sets how far the frame is between two ticks.</summary>
    internal float FrameAlpha { get; set; } = 1f;

    /// <summary>Gets or sets a value indicating whether motion is blended between ticks.</summary>
    internal bool FrameSmooth { get; set; }

    // Which of its variants a placement wears, or -1 for the model the walk placed: an index the title gives, whose binding resolved.
    internal int VariantOf(WorldPlacement placement) =>
        placement.Variants is { } variants
        && (Variants?.VariantOf(placement) is >= 0 and int titled ? titled : Objects?.LightVariantOf(placement)) is int variant
        && variant >= 0
        && variant < variants.Count
        && variants[variant] is not null
            ? variant
            : -1;

    // The placement as it is shown: the variant it wears, its destroyed model where it has gone off, and where it stands now.
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

    internal WorldPlacement ApplyLiveTransforms(WorldPlacement placement, WorldPlacement displayed)
    {
        // A security camera's swivel changes the orientation but never the model, so the
        // displayed placement cached above stays right; only the transform is per-frame.
        if (Objects?.TryGetSwivelOrientation(placement, FrameAlpha, FrameSmooth, out Matrix3f orientation) == true)
        {
            displayed = displayed with { Orientation = orientation };
        }

        // A fan or a radar head, which turns for ever and never leaves its spot. Same reasoning as
        // the camera above: the model and the position are settled, only the transform is per-frame.
        // What moves the placement below is asked with it as it has turned: a rotor turns in its helicopter's frame, and the
        // helicopter carries it turned, where asked as authored the turn was lost the moment its object moved.
        WorldPlacement carried = placement;
        if (Spins?.TryGetOrientation(placement, FrameAlpha, out Matrix3f spun) == true)
        {
            displayed = displayed with { Orientation = spun };
            carried = placement with { Orientation = spun };
        }

        // A door a title swings: it leaves its spot as well as turning.
        if (Motions?.TryGetTransform(carried, FrameAlpha, out Vector3d moved, out Matrix3f movedOrientation) == true)
        {
            displayed = displayed with { Position = moved, Orientation = movedOrientation };
        }

        if (Objects?.TryGetStandOrientation(placement, out Matrix3f stand) == true)
        {
            displayed = displayed with { Orientation = stand };
        }

        return displayed;
    }

    internal bool UsesDestroyedReplacement(WorldPlacement placement) =>
        placement.DestroyedReplacement is not null
        && (Objects?.WearsDestroyedModel(placement) == true || Destruction?.IsDestroyed(placement) == true);

    // Whether a placement has gone off: one of the title's objects says it is broken.
    internal bool IsBroken(WorldPlacement placement) =>
        Objects?.IsBroken(placement) == true || Destruction?.IsDestroyed(placement) == true;

    internal bool IsClaimed(WorldPlacement placement) => _objects?.Claims(placement) == true;

    // The title's objects swapped in or out is a claim made or let go.
    private static bool Claimed()
    {
        PlacementClaims.Changed();
        return true;
    }

    internal void ResolveDrawTransform(
        WorldPlacement placement,
        out Vector3d drawPosition,
        out Matrix3f drawOrientation,
        out RotorDrawState rotorDraw,
        out bool hasRotorDraw)
    {
        if (Objects is { } objects)
        {
            objects.TryGetDrawTransform(
                placement, FrameAlpha, FrameSmooth, out drawPosition, out drawOrientation, out rotorDraw, out hasRotorDraw);
            return;
        }

        drawPosition = placement.Position;
        drawOrientation = placement.Orientation;
        rotorDraw = default;
        hasRotorDraw = false;
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
