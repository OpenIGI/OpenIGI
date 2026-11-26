using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// The crosshair's ray as either game's command carries it: its origin as an offset from the eye, then its direction, all nought
/// in first person. <b>Not retail</b>: the remaster's third-person view.
/// </summary>
/// <remarks>
/// Over the shoulder the crosshair is the camera's ray, not the eye's, and a player's own game converges its rounds on what that ray
/// meets. The server's copy of the player has no camera, so the ray comes with the command; without it the server fired a
/// third-person joiner's rounds along the eye, the camera's half-metre beside where its crosshair sat.
/// </remarks>
public static class AimRayChannels
{
    /// <summary>The channels the ray takes.</summary>
    public const int Count = 6;

    /// <summary>The farthest a crosshair's ray may start from the eye, in world units: the over-the-shoulder camera's reach and some.</summary>
    /// <remarks>
    /// The camera sits at most 2.6 m back, 0.55 m aside and 0.25 m up by default (<see cref="ThirdPersonCamera"/>). A ray from further
    /// off is a client aiming from somewhere its player is not, and is drawn back to this.
    /// </remarks>
    public const double MaxReach = 3.5 * WorldUnits.PerMetre;

    /// <summary>Writes the ray.</summary>
    /// <param name="channels">Where it goes, <see cref="Count"/> channels.</param>
    /// <param name="aim">The ray as an offset from the eye and a direction, or <see langword="null"/> in first person.</param>
    public static void Pack(Span<float> channels, (Vector3d EyeOffset, Vector3f Direction)? aim)
    {
        (Vector3d offset, Vector3f direction) = aim ?? default;
        channels[0] = (float)offset.X;
        channels[1] = (float)offset.Y;
        channels[2] = (float)offset.Z;
        channels[3] = direction.X;
        channels[4] = direction.Y;
        channels[5] = direction.Z;
    }

    /// <summary>Reads the ray.</summary>
    /// <param name="channels">The ray's <see cref="Count"/> channels.</param>
    /// <returns>Its origin as an offset from the eye and its direction, or <see langword="null"/> when the player was in first person.</returns>
    /// <remarks>
    /// What a client sends is not taken on trust: a direction that is no direction leaves the player in first person, it is made unit
    /// length, and an origin past <see cref="MaxReach"/> is drawn back to it.
    /// </remarks>
    public static (Vector3d EyeOffset, Vector3f Direction)? Unpack(ReadOnlySpan<float> channels)
    {
        Vector3f direction = new(channels[3], channels[4], channels[5]);
        float length = MathF.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (!float.IsFinite(length) || length < 0.5f || length > 2f)
        {
            return null;
        }

        Vector3d offset = new(channels[0], channels[1], channels[2]);
        double reach = offset.Length;
        if (!double.IsFinite(reach))
        {
            return null;
        }

        if (reach > MaxReach)
        {
            offset *= MaxReach / reach;
        }

        return (offset, new Vector3f(direction.X / length, direction.Y / length, direction.Z / length));
    }
}
