using OpenIGI.Core.Numerics;

namespace OpenIGI.Audio;

/// <summary>Where the listener is and which way it faces.</summary>
/// <remarks>
/// <para>
/// Positions are in <b>metres</b> and are <see langword="double"/>, matching the rest of the port: world
/// positions are double throughout, and the render world divides raw engine units by 4096 on load. A
/// caller holding engine units must divide by <c>UnitsPerMetre</c> before it gets here.
/// </para>
/// <para>
/// The orientation is the engine's own eye basis, whose columns are right, forward and up — the frame
/// <c>sub_4B38E0</c> composes and the AI vision cone is measured in. It is applied with the engine's
/// <c>v · M</c> convention, which is what <see cref="Matrix3f.Transform(Vector3d)"/> does; using the
/// other convention transposes the rotation and mirrors the stereo image.
/// </para>
/// </remarks>
public readonly record struct AudioListenerState
{
    /// <summary>Gets the listener position in metres.</summary>
    public Vector3d Position { get; init; }

    /// <summary>Gets the listener basis: columns are right, forward and up.</summary>
    public Matrix3f Orientation { get; init; }

    /// <summary>Gets the listener velocity in metres per second, used only when doppler is enabled.</summary>
    public Vector3f Velocity { get; init; }

    /// <summary>Gets a listener at the origin facing along the engine's forward axis.</summary>
    public static AudioListenerState Default => new()
    {
        Position = Vector3d.Zero,
        Orientation = Matrix3f.Identity,
        Velocity = default,
    };

    /// <summary>Gets the direction the listener faces.</summary>
    /// <remarks>The engine's forward axis is the basis' second column, which is +Y in an unrotated frame.</remarks>
    public Vector3f Forward => Orientation.Column2;

    /// <summary>Gets the listener's up direction.</summary>
    public Vector3f Up => Orientation.Column3;

    /// <summary>Gets the listener's right direction, which is the axis a pan is measured along.</summary>
    public Vector3f Right => Orientation.Column1;
}
