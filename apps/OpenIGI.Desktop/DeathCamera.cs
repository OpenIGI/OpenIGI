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
/// <c>HumanCamera_DeathCameraInit</c> (<c>0x4d51d0</c>) sets a pull-back distance and a ninety-degree
/// field of view, then <c>FindStartPosition</c> (<c>0x4d36a0</c>) casts a fan of elevation and azimuth
/// guesses out to <c>+292 + 2048 = 12288</c> units — three metres — and takes the first that clears the
/// world, so the body is framed against open space rather than the inside of a wall.
/// </para>
/// <para>
/// The search itself is <see cref="DeathCameraVantage"/>, in the game layer where it can be tested; this
/// wraps it into a <see cref="Camera3D"/>: it aims a metre above the body's feet — the chest — runs the
/// search from there, sits the eye where the search says, and looks back at the pivot. The world is
/// already drawn at the ninety-degree horizontal field of view <c>DeathCameraInit</c> also sets
/// (<see cref="HumanPlayerBody.DeathCameraFieldOfView"/>), so only the eye changes.
/// </para>
/// </remarks>
public sealed class DeathCamera
{
    /// <summary>World units in a metre — the game's own scale.</summary>
    public const double UnitsPerMetre = 4096.0;

    // Where on the body the camera aims: a metre above the feet, roughly the chest.
    private const double ChestAboveFeetInUnits = UnitsPerMetre;

    private Camera3D _camera = Camera3D.Default;

    /// <summary>Gets a value indicating whether the death camera has taken over.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the eye to draw the world from while the death camera is active.</summary>
    public Camera3D Camera => _camera;

    /// <summary>Takes over the view: searches for a clear vantage on the body and looks at it.</summary>
    /// <param name="bodyFeetUnits">Where the body fell — the player's own position, in engine units.</param>
    /// <param name="gamma">The heading the body faces.</param>
    /// <param name="walls">A world ray test to keep the camera out of geometry, or <see langword="null"/>.</param>
    public void Begin(Vector3d bodyFeetUnits, float gamma, DeathCameraVantage.Blocked? walls = null)
    {
        Vector3d pivot = new(
            bodyFeetUnits.X, bodyFeetUnits.Y, bodyFeetUnits.Z + ChestAboveFeetInUnits);

        DeathCameraVantage.Vantage vantage = DeathCameraVantage.Find(pivot, gamma, walls);

        Vector3d eye = new(
            pivot.X + (vantage.Direction.X * vantage.DistanceInUnits),
            pivot.Y + (vantage.Direction.Y * vantage.DistanceInUnits),
            pivot.Z + (vantage.Direction.Z * vantage.DistanceInUnits));

        Vector3d look = new(pivot.X - eye.X, pivot.Y - eye.Y, pivot.Z - eye.Z);
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
