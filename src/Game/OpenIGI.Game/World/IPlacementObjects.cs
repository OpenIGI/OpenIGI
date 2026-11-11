using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// What a title's own objects do to how the level's placements are drawn: which they move, turn, break, take away or
/// dress in another model.
/// </summary>
/// <remarks>
/// <para>
/// The original draws a placed object from its own position and matrix, which a door, a lift or a cut-scene that moves it
/// writes into the object itself. Here the title's objects keep their own state and the picture asks them, through this:
/// the renderer knows no door, lift or camera, only that a placement may be drawn somewhere other than where the level put
/// it.
/// </para>
/// <para>
/// A title that keeps its objects another way answers through the narrower contracts beside this one
/// (<see cref="IPlacementMotion"/>, <see cref="IPlacementDestruction"/>, <see cref="IPlacementPresence"/>,
/// <see cref="IPlacementVariant"/>).
/// </para>
/// </remarks>
public interface IPlacementObjects
{
    /// <summary>Gets whether something may draw a placement elsewhere than where the level put it.</summary>
    /// <param name="placement">The placement.</param>
    /// <returns>Whether one of the title's objects claims it.</returns>
    bool Claims(WorldPlacement placement);

    /// <summary>Gets whether a placement has been picked up and is gone from the level.</summary>
    /// <param name="placement">The placement.</param>
    /// <returns>Whether it has been taken.</returns>
    bool IsTaken(WorldPlacement placement);

    /// <summary>Gets whether a placement has been blown up or shot to pieces.</summary>
    /// <param name="placement">The placement.</param>
    /// <returns>Whether it is broken.</returns>
    bool IsBroken(WorldPlacement placement);

    /// <summary>Gets whether a placement is a light that has been shot out and leaves the picture.</summary>
    /// <param name="placement">The placement.</param>
    /// <returns>Whether its light is gone.</returns>
    bool IsLightGone(WorldPlacement placement);

    /// <summary>Gets whether a placement is drawn as its destroyed model, where the level gave it one.</summary>
    /// <param name="placement">The placement.</param>
    /// <returns>Whether it wears the destroyed model.</returns>
    bool WearsDestroyedModel(WorldPlacement placement);

    /// <summary>Gets which of its variants a light wears.</summary>
    /// <param name="placement">The placement.</param>
    /// <returns>The variant's index.</returns>
    int LightVariantOf(WorldPlacement placement);

    /// <summary>Gets the orientation of a part that swivels where it stands, as a camera's head does.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="tickFraction">How far the frame is between two ticks.</param>
    /// <param name="smooth">Whether the motion is blended between ticks.</param>
    /// <param name="orientation">Receives the orientation.</param>
    /// <returns>Whether the placement is such a part.</returns>
    bool TryGetSwivelOrientation(WorldPlacement placement, float tickFraction, bool smooth, out Matrix3f orientation);

    /// <summary>Gets the orientation of a stand that turns with what it carries, as a mounted gun's does.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="orientation">Receives the orientation.</param>
    /// <returns>Whether the placement is such a stand.</returns>
    bool TryGetStandOrientation(WorldPlacement placement, out Matrix3f orientation);

    /// <summary>Gets where and how a placement one of the title's objects moves is drawn this frame.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="tickFraction">How far the frame is between two ticks.</param>
    /// <param name="smooth">Whether the motion is blended between ticks.</param>
    /// <param name="position">Receives the position, in metres.</param>
    /// <param name="orientation">Receives the orientation.</param>
    /// <param name="rotor">Receives a turning rotor's blur, when <paramref name="hasRotor"/> is set.</param>
    /// <param name="hasRotor">Receives whether the placement is a turning rotor.</param>
    /// <returns>Whether anything moves the placement; otherwise the position and orientation are the placement's own.</returns>
    bool TryGetDrawTransform(
        WorldPlacement placement,
        float tickFraction,
        bool smooth,
        out Vector3d position,
        out Matrix3f orientation,
        out RotorDrawState rotor,
        out bool hasRotor);
}
