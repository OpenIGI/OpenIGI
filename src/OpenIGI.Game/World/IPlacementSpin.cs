using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>Turns placements that spin where they stand, once per tick.</summary>
/// <remarks>
/// <para>
/// Some of what a level places never moves from its spot but never stops turning either: extractor fans,
/// radar heads, the drum of a winch. Nothing about that belongs to the walk that placed them — the
/// position, the model and the lightmap are all settled — so it is a per-frame override of one field,
/// the same shape the security cameras' swivel already uses.
/// </para>
/// <para>
/// Behind a seam because only I.G.I. 2 has it. The sequel declares a <c>RotateAttachment</c> task that
/// names a part of an object already placed and gives it three rotation speeds; Project I.G.I. has no
/// such type, and the renderer should not know the difference.
/// </para>
/// </remarks>
public interface IPlacementSpin
{
    /// <summary>Advances every spin by one simulation tick.</summary>
    /// <remarks>Called from the host's fixed step, beside the other registries that tick.</remarks>
    void Advance();

    /// <summary>Gets the orientation a spinning placement is drawn at this frame.</summary>
    /// <param name="placement">The placement as the walk produced it.</param>
    /// <param name="tickFraction">How far the frame is between the last tick and the next, 0 to 1.</param>
    /// <param name="orientation">Receives the turned orientation.</param>
    /// <returns><see langword="false"/> when this placement does not spin, which is nearly all of them.</returns>
    bool TryGetOrientation(WorldPlacement placement, float tickFraction, out Matrix3f orientation);
}
