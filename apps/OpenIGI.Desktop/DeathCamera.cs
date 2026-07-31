using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// The camera the game hands control to when the player dies — it detaches from the eye and pulls back to
/// a third-person view of the body, the original's <c>HumanCamera</c> death mode.
/// </summary>
/// <remarks>
/// <para>
/// The original's <c>HumanCamera_DeathCameraInit</c> (<c>0x4d51d0</c>) sets a pull-back distance and a
/// ninety-degree field of view, then <c>FindStartPosition</c> (<c>0x4d36a0</c>) casts a fan of elevation
/// and azimuth guesses out to <c>+292 + 2048 = 12288</c> units — three metres — and takes the first that
/// clears the world, so the body is always framed against open space rather than the inside of a wall.
/// </para>
/// <para>
/// This reproduces the <b>distance and the clear-vantage guarantee</b>, not yet the full search: it sits
/// the camera three metres behind and above the body, looking at its chest, and — like the original's
/// ray fan — pulls the eye in short of whatever the world puts between the body and that point, so the
/// camera never ends up inside geometry. The elevation/azimuth search that finds the <em>best</em> of
/// several vantages is the recorded refinement; read from I.G.I. 2's named camera, whose engine this is.
/// </para>
/// </remarks>
public sealed class DeathCamera
{
    /// <summary>World units in a metre — the game's own scale.</summary>
    public const double UnitsPerMetre = 4096.0;

    // How far above the horizon the camera looks down at the body, in radians -- about 28 degrees, so the
    // view is a raised third-person shot rather than a level one.
    private const double ElevationRadians = 0.5;

    // Where on the body the camera aims: a metre above the feet, roughly the chest.
    private const double ChestAboveFeetInUnits = UnitsPerMetre;

    // How far short of a blocking surface to stop, so the near plane never punches through it -- 12.5 cm.
    private const double WallMarginInUnits = 512.0;

    private Camera3D _camera = Camera3D.Default;

    /// <summary>A world ray test: whether the straight line from <paramref name="fromUnits"/> to
    /// <paramref name="toUnits"/> is blocked, and where.</summary>
    /// <param name="fromUnits">The start, in engine units.</param>
    /// <param name="toUnits">The end, in engine units.</param>
    /// <param name="hitUnits">Receives where the line was first blocked, in engine units.</param>
    /// <returns><see langword="true"/> when something blocks the line.</returns>
    public delegate bool Blocked(Vector3d fromUnits, Vector3d toUnits, out Vector3d hitUnits);

    /// <summary>Gets a value indicating whether the death camera has taken over.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the eye to draw the world from while the death camera is active.</summary>
    /// <remarks>
    /// The world is already drawn at a ninety-degree horizontal field of view — the game view's own, which
    /// <c>DeathCameraInit</c> also sets (<see cref="HumanPlayerBody.DeathCameraFieldOfView"/>) — so the
    /// death camera needs no projection of its own; only the eye changes.
    /// </remarks>
    public Camera3D Camera => _camera;

    /// <summary>Takes over the view: pulls back behind and above the body and looks at it.</summary>
    /// <param name="bodyFeetUnits">Where the body fell — the player's own position, in engine units.</param>
    /// <param name="gamma">The heading the body faces.</param>
    /// <param name="walls">A world ray test to keep the camera out of geometry, or <see langword="null"/>.</param>
    public void Begin(Vector3d bodyFeetUnits, float gamma, Blocked? walls = null)
    {
        Vector3d target = new(
            bodyFeetUnits.X, bodyFeetUnits.Y, bodyFeetUnits.Z + ChestAboveFeetInUnits);

        // Behind the body. A body facing gamma looks along (-sin gamma, cos gamma) -- the port's own
        // facing convention -- so behind it is (+sin gamma, -cos gamma). The elevation lifts that up, so
        // the camera sits raised and looks slightly down at the fall.
        double ce = Math.Cos(ElevationRadians);
        double se = Math.Sin(ElevationRadians);
        Vector3d back = new(Math.Sin(gamma) * ce, -Math.Cos(gamma) * ce, se);

        double distance = HumanPlayerBody.DeathCameraDistanceInUnits;
        Vector3d eye = new(
            target.X + (back.X * distance),
            target.Y + (back.Y * distance),
            target.Z + (back.Z * distance));

        // Keep out of walls: if the world blocks the line from the body to the eye, pull the eye in to
        // just short of what blocked it -- FindStartPosition's clear-vantage guarantee, by the simplest
        // means that honours it.
        if (walls is not null && walls(target, eye, out Vector3d hit))
        {
            Vector3d d = new(hit.X - target.X, hit.Y - target.Y, hit.Z - target.Z);
            double length = Math.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z));
            double pulled = Math.Max(0.0, length - WallMarginInUnits);
            double scale = length > 0.0 ? pulled / length : 0.0;
            eye = new(target.X + (d.X * scale), target.Y + (d.Y * scale), target.Z + (d.Z * scale));
        }

        Vector3d look = new(target.X - eye.X, target.Y - eye.Y, target.Z - eye.Z);
        double lookLength = Math.Sqrt((look.X * look.X) + (look.Y * look.Y) + (look.Z * look.Z));
        float yaw = (float)Math.Atan2(look.X, look.Y);
        float pitch = lookLength > 0.0 ? (float)Math.Asin(look.Z / lookLength) : 0f;

        Vector3d eyeMetres = new(eye.X / UnitsPerMetre, eye.Y / UnitsPerMetre, eye.Z / UnitsPerMetre);
        _camera = new Camera3D(eyeMetres, Camera3D.LookRotation(yaw, pitch));
        IsActive = true;
    }

    /// <summary>Hands the view back — what starting a fresh mission does.</summary>
    public void Reset()
    {
        IsActive = false;
        _camera = Camera3D.Default;
    }
}
