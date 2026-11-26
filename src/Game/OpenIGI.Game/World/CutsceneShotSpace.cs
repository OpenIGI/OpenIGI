using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// The space a cutscene camera key is written in, and the world it is seen in: one for a key that rides nothing (the two are the
/// same), another for one that rides a task -- its values written relative to where the task stood as the level loaded, carried
/// to where the task stands now (retail <c>sub_508940</c>).
/// </summary>
/// <param name="LinkOrigin">Where the ridden task stood as the level loaded, in world units.</param>
/// <param name="LinkOriginOrientation">How it was turned then.</param>
/// <param name="LinkPosition">Where it stands for the frame shown, in world units.</param>
/// <param name="LinkOrientation">How it is turned for that frame.</param>
/// <remarks>
/// <para>
/// One implementation for the game, which builds a riding shot's frame with <see cref="ToWorld"/>, and the editor, which writes
/// what its viewport shows back into the key with <see cref="ToAuthored"/> -- a piloted camera's view written back through its
/// parent's transform.
/// </para>
/// <para>
/// <see cref="Matrix3f.Transform(Vector3d)"/> is the engine's row-vector multiply: the orientations here are orthonormal, so each one's
/// transpose undoes it, and the order of the transposes is the retail one (reversed, a riding camera orbits on the wrong axes).
/// </para>
/// </remarks>
public readonly record struct CutsceneShotSpace(
    Vector3d LinkOrigin,
    Matrix3f LinkOriginOrientation,
    Vector3d LinkPosition,
    Matrix3f LinkOrientation)
{
    /// <summary>Gets the space of a key that rides nothing: written as it is seen.</summary>
    public static CutsceneShotSpace World { get; } = new(Vector3d.Zero, Matrix3f.Identity, Vector3d.Zero, Matrix3f.Identity);

    /// <summary>Where a key written in this space is seen, and how it is turned.</summary>
    /// <param name="position">The key's position, in world units, as written.</param>
    /// <param name="orientation">Its turn as written, in the engine's storage.</param>
    /// <returns>The position and turn in the world.</returns>
    public (Vector3d Position, Matrix3f Orientation) ToWorld(Vector3d position, Matrix3f orientation)
    {
        Vector3d relative = LinkOriginOrientation.Transform(position - LinkOrigin);
        return (LinkPosition + LinkOrientation.Transposed().Transform(relative), LinkOrientation * LinkOriginOrientation.Transposed() * orientation);
    }

    /// <summary>What a key has to be written as to be seen at a place, turned a way: <see cref="ToWorld"/> undone.</summary>
    /// <param name="position">Where it is to be seen, in world units.</param>
    /// <param name="orientation">How it is to be turned in the world, in the engine's storage.</param>
    /// <returns>The position and turn to write.</returns>
    public (Vector3d Position, Matrix3f Orientation) ToAuthored(Vector3d position, Matrix3f orientation)
    {
        Vector3d relative = LinkOrientation.Transform(position - LinkPosition);
        return (LinkOrigin + LinkOriginOrientation.Transposed().Transform(relative), LinkOriginOrientation * LinkOrientation.Transposed() * orientation);
    }
}
