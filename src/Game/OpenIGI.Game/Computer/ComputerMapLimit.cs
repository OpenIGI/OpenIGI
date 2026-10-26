using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Computer;

/// <summary>
/// The square a level lets the map computer's camera look over, <c>DefineComputerLimit</c> in both games: a centre and the
/// size, in metres, of its side.
/// </summary>
/// <param name="Position">The square's centre, world units.</param>
/// <param name="Size">Its side, metres.</param>
public readonly record struct ComputerMapLimit(Vector3d Position, float Size)
{
    /// <summary>Half the side, world units, as both games work it out: the side halved, then in world units.</summary>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    /// <returns>The half side.</returns>
    public double HalfSide<TFpu>()
        where TFpu : struct, IX87<TFpu> => ((TFpu)Size * 0.5 * 4096.0).Value;

    /// <summary>Holds a camera position inside the square across, its height left alone (Project I.G.I.'s <c>0x46A200</c>).</summary>
    /// <typeparam name="TFpu">The precision.</typeparam>
    /// <param name="at">The position.</param>
    /// <returns>The position held inside.</returns>
    public Vector3d Clamp<TFpu>(Vector3d at)
        where TFpu : struct, IX87<TFpu>
    {
        double half = HalfSide<TFpu>();
        double dx = Math.Clamp(at.X - Position.X, -half, half);
        double dy = Math.Clamp(at.Y - Position.Y, -half, half);
        return new Vector3d(dx + Position.X, dy + Position.Y, at.Z);
    }

    /// <summary>Whether a point lies inside the square across (Project I.G.I.'s <c>0x46A970</c>).</summary>
    /// <typeparam name="TFpu">The precision.</typeparam>
    /// <param name="at">The point.</param>
    /// <returns><see langword="true"/> when it does, its edges included.</returns>
    public bool Contains<TFpu>(Vector3d at)
        where TFpu : struct, IX87<TFpu>
    {
        double half = HalfSide<TFpu>();
        double dx = at.X - Position.X, dy = at.Y - Position.Y;
        return dx >= -half && dx <= half && dy >= -half && dy <= half;
    }
}
