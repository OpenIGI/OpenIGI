using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1.Cameras;

/// <summary>
/// What a downed co-op player sees while a teammate fights on: the teammate's own view, from just behind and above its head
/// so the head is not in the way, looking exactly where it looks. Walls pull the camera in rather than letting it through.
/// </summary>
/// <remarks>The teammate moves and turns on the tick; the view is presented between its last two, as the walking eye is.</remarks>
internal sealed class TeammateView
{
    // Behind the eye along the look, over it, and to the right of it: an over-the-shoulder view of the teammate's own aim.
    // Metres; a wall in the way brings the camera this far short of it.
    private const double BackMetres = 1.6;
    private const double UpMetres = 0.3;
    private const double RightMetres = 0.45;
    private const double KeepOffMetres = 0.2;

    private (Vector3d Eye, float Yaw, float Pitch) _previous;
    private (Vector3d Eye, float Yaw, float Pitch) _current;

    /// <summary>Gets whether a teammate is being watched.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Follows the teammate's eye and look for this tick.</summary>
    /// <param name="eye">Its eye, in world units.</param>
    /// <param name="yaw">Its heading.</param>
    /// <param name="pitch">Its pitch, up positive.</param>
    public void Follow(Vector3d eye, float yaw, float pitch)
    {
        // A first tick, or a switch to another teammate far off, starts from where it is rather than gliding across.
        bool jumped = !IsActive || Vector3d.Distance(eye, _current.Eye) > 4.0 * WorldScene.UnitsPerMetre;
        _previous = jumped ? (eye, yaw, pitch) : _current;
        _current = (eye, yaw, pitch);
        IsActive = true;
    }

    /// <summary>Stops watching.</summary>
    public void Reset() => IsActive = false;

    /// <summary>The camera, between the last two ticks.</summary>
    /// <param name="alpha">How far between them, from the older.</param>
    /// <param name="walls">What pulls the camera in, or <see langword="null"/> for nothing.</param>
    /// <returns>The eye to draw from, in metres.</returns>
    public Camera3D Present(float alpha, WorldRayCast? walls)
    {
        Vector3d eyeUnits = _previous.Eye + ((_current.Eye - _previous.Eye) * alpha);
        float turn = MathF.IEEERemainder(_current.Yaw - _previous.Yaw, MathF.Tau);
        float yaw = _previous.Yaw + (turn * alpha);
        float pitch = float.Lerp(_previous.Pitch, _current.Pitch, alpha);
        Matrix3f look = Camera3D.LookRotation(yaw, pitch);

        Vector3d eye = WorldScene.ToMetres(eyeUnits);
        Vector3d forward = new(look.M31, look.M32, look.M33);
        Vector3d right = new(look.M11, look.M12, look.M13);
        Vector3d wanted = eye - (forward * BackMetres) + (right * RightMetres) + new Vector3d(0, 0, UpMetres);

        // The line from the eye out to where the camera would stand: a wall on it brings the camera in front of the wall.
        Vector3d at = wanted;
        if (walls is not null && walls(eyeUnits, WorldScene.ToUnits(wanted), out WorldRayHit hit))
        {
            double reach = Vector3d.Distance(eye, wanted);
            double clear = Math.Max(0.0, (hit.Distance / WorldScene.UnitsPerMetre) - KeepOffMetres);
            if (clear < reach)
            {
                at = eye + ((wanted - eye) * (clear / reach));
            }
        }

        return new Camera3D(at, look);
    }
}
