using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Presentation.Igi1.Cameras;

/// <summary>
/// Project I.G.I.'s <c>HumanCamera</c> orbit (<c>camera/humancamera.c</c>): mode 3, around a body on a ladder, a wire or
/// another interaction, and mode 5, over the player's body once dead.
/// </summary>
/// <remarks>
/// <para>
/// Both modes enter the same way (<c>0x483360</c>, <c>0x483D40</c>): at no angle off the body's own frame, with the
/// distance at 1024 and the target at 10240. The camera stands at the <b>target</b> distance behind the look point, along
/// the orbit, and looks at it (<c>0x4834D0</c>'s <c>follow</c>); the distance only eases toward the target, 341.33 a
/// tick (<c>0x483390</c>). It is pulled in only where every one of eight points around the body is hidden from it
/// (<c>0x483A00</c>): then 204.8 short of whatever the line from the look point met. A body seen from anywhere keeps the
/// camera where it is.
/// </para>
/// <para>
/// Mode 5 also casts, each tick, four lines from the look point to the sides, top and bottom of a frame at the current
/// distance (<c>0x483EC0</c>). A surface facing one pulls the camera to 1.05 times its depth along the orbit, never
/// under a metre, distance and target both; with none struck the target grows 341.33 back toward 10240. The orbit's
/// pitch is held within 75 degrees either way. The one-frame shot of the killer (<c>0x4849B0</c>) is left out: the
/// mode's latch is cleared after its first frame (<c>0x482700</c>), so no player ever sees it.
/// </para>
/// <para>
/// The mouse turns the orbit with the player's own look settings, as every view of the remaster does; the look point
/// and the distances move on the tick and are presented between their tick pair.
/// </para>
/// </remarks>
public sealed class Igi1HumanCamera
{
    /// <summary>World units in a metre.</summary>
    public const double UnitsPerMetre = 4096.0;

    /// <summary>The distance both modes enter at — <c>+272 = 1024</c>.</summary>
    public const double InitialDistanceInUnits = 1024.0;

    /// <summary>The target both modes enter at, and mode 5's ceiling — <c>+276 = 10240</c>.</summary>
    public const double TargetDistanceInUnits = 10240.0;

    /// <summary>How far the distance eases toward the target a tick, and mode 5's target grows — <c>341.33</c>.</summary>
    public const double EaseInUnits = 341.33334;

    /// <summary>How far short of a wall a hidden body pulls the camera — <c>204.8</c>.</summary>
    public const double KeepOffInUnits = 204.8;

    /// <summary>The orbit's pitch limit — 75 degrees.</summary>
    public const double PitchLimit = 1.3089969;

    /// <summary>The nearest mode 5's collision brings the camera — one metre.</summary>
    public const double DeathNearestInUnits = 4096.0;

    // 0x483EC0's frame half-width a unit of distance: 0x1.99999ap-14 * 4096.
    private const double DeathFrameScale = 0.000099999997764825821 * 4096.0;

    // 0x483EC0's depth multiplier.
    private const double DeathPull = 1.05;

    // The eight points 0x483A00 tests around a body: 1433.6 to every side, at the feet and 2662.4 up.
    private static readonly Vector3d[] BodyPoints =
    [
        new(-1433.6, 1433.6, 0.0), new(1433.6, 1433.6, 0.0), new(-1433.6, -1433.6, 0.0), new(1433.6, -1433.6, 0.0),
        new(-1433.6, 1433.6, 2662.4), new(1433.6, 1433.6, 2662.4), new(-1433.6, -1433.6, 2662.4), new(1433.6, -1433.6, 2662.4),
    ];

    private Camera3D _camera = Camera3D.Default;
    private Vector3d _look;
    private Vector3d _lookPrevTick;
    private Vector3d _feet;
    private bool _hasTickPair;
    private float _presentedAlpha = 1f;
    private bool _death;
    private double _facing;
    private double _pitch;
    private double _yaw;
    private double _distance = InitialDistanceInUnits;
    private double _target = TargetDistanceInUnits;
    private double _targetPrevTick = TargetDistanceInUnits;
    private WorldRayCast? _walls;

    /// <summary>Gets whether the camera has the view.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets or sets how far the mouse orbits — the player's own look settings.</summary>
    public MouseLookSettings LookSettings { get; set; } = MouseLookSettings.Default;

    /// <summary>Gets the eye to draw the world from.</summary>
    public Camera3D Camera => _camera;

    /// <summary>Gets how far the camera eases toward, in world units — <c>+276</c>.</summary>
    public double TargetDistance => _target;

    /// <summary>Gets the eased distance, in world units — <c>+272</c>, which mode 5 sizes its collision frame by.</summary>
    public double Distance => _distance;

    /// <summary>Takes the view in mode 3 (<c>0x483360</c>) or mode 5 (<c>0x483D40</c>).</summary>
    /// <param name="look">The point it orbits and looks at — the body's eye, in world units.</param>
    /// <param name="feet">The body's feet, which the eight hidden-body points stand around.</param>
    /// <param name="facing">The body's heading; the orbit starts straight behind it.</param>
    /// <param name="walls">The world line test, or <see langword="null"/> for none.</param>
    /// <param name="death">Whether this is mode 5, over the dead player.</param>
    public void Begin(Vector3d look, Vector3d feet, float facing, WorldRayCast? walls, bool death)
    {
        _death = death;
        _facing = facing;
        _pitch = 0.0;
        _yaw = 0.0;
        _distance = InitialDistanceInUnits;
        _target = TargetDistanceInUnits;
        _targetPrevTick = _target;
        _walls = walls;
        _look = look;
        _lookPrevTick = look;
        _feet = feet;
        _hasTickPair = false;
        _presentedAlpha = 1f;
        IsActive = true;
        BuildCamera(_look, _target);
    }

    /// <summary>Moves the look point and the body with the tick, and runs the tick's easing and, in mode 5, collision.</summary>
    /// <param name="look">The body's eye, in world units.</param>
    /// <param name="feet">The body's feet.</param>
    public void Tick(Vector3d look, Vector3d feet)
    {
        if (!IsActive)
        {
            return;
        }

        _lookPrevTick = _hasTickPair ? _look : look;
        _look = look;
        _feet = feet;
        _targetPrevTick = _target;
        _hasTickPair = true;

        // 0x483390 / 0x483D80: the distance eases toward the target.
        _distance += Math.Clamp(_target - _distance, -EaseInUnits, EaseInUnits);
        if (_death)
        {
            Collide();
        }

        BuildCamera(_look, _target);
    }

    /// <summary>Places the camera for a frame drawn between two ticks.</summary>
    /// <param name="alpha">How far through the tick the frame is.</param>
    public void Present(float alpha)
    {
        if (!IsActive)
        {
            return;
        }

        _presentedAlpha = Math.Clamp(alpha, 0f, 1f);
        if (!_hasTickPair)
        {
            BuildCamera(_look, _target);
            return;
        }

        BuildCamera(
            Vector3d.Lerp(_lookPrevTick, _look, _presentedAlpha),
            _targetPrevTick + ((_target - _targetPrevTick) * _presentedAlpha));
    }

    /// <summary>Turns the orbit by a frame's mouse motion, its pitch held within 75 degrees.</summary>
    /// <param name="mouseDeltaX">The horizontal motion, in pixels.</param>
    /// <param name="mouseDeltaY">The vertical motion, in pixels.</param>
    public void Control(float mouseDeltaX, float mouseDeltaY)
    {
        if (!IsActive)
        {
            return;
        }

        _yaw += LookSettings.YawDelta(mouseDeltaX);
        _pitch = Math.Clamp(_pitch + LookSettings.PitchDelta(mouseDeltaY), -PitchLimit, PitchLimit);
        Present(_presentedAlpha);
    }

    /// <summary>Hands the view back.</summary>
    public void Reset()
    {
        IsActive = false;
        _camera = Camera3D.Default;
        _walls = null;
        _hasTickPair = false;
        _presentedAlpha = 1f;
    }

    // The orbit's backward direction: from the look point toward the camera.
    private Vector3d Back()
    {
        double heading = _facing + _yaw;
        double level = Math.Cos(_pitch);
        return new Vector3d(Math.Sin(heading) * level, -Math.Cos(heading) * level, Math.Sin(_pitch));
    }

    // 0x483EC0: four lines from the look point to a frame `distance` behind it, half `distance * 0.4096` across.
    private void Collide()
    {
        if (_walls is not { } walls)
        {
            return;
        }

        Vector3d back = Back();
        Vector3d right = new(Math.Cos(_facing + _yaw), Math.Sin(_facing + _yaw), 0.0);
        Vector3d up = Vector3d.Cross(back, right).Normalized();
        double half = _distance * DeathFrameScale;
        Vector3d centre = _look + (back * _distance);
        Span<Vector3d> ends = [centre - (right * half), centre + (right * half), centre + (up * half), centre - (up * half)];
        bool sidesClear = true;
        bool endsClear = true;
        for (int i = 0; i < ends.Length; i++)
        {
            Vector3d line = ends[i] - _look;
            if (!walls(_look, ends[i], out WorldRayHit hit)
                || (line.X * hit.Normal.X) + (line.Y * hit.Normal.Y) + (line.Z * hit.Normal.Z) >= 0.0)
            {
                continue;
            }

            double depth = Vector3d.Dot(hit.Position - _look, back) * DeathPull;
            _distance = _target = Math.Max(DeathNearestInUnits, depth);
            if (i < 2)
            {
                sidesClear = false;
            }
            else
            {
                endsClear = false;
            }
        }

        if (sidesClear && endsClear)
        {
            _target = Math.Min(TargetDistanceInUnits, _target + EaseInUnits);
        }
    }

    // 0x4834D0's follow: the camera at the target distance, pulled in only for a body hidden from it.
    private void BuildCamera(Vector3d look, double target)
    {
        Vector3d back = Back();
        Vector3d eye = look + (back * target);
        if (_walls is { } walls && walls(look, eye, out WorldRayHit hit) && IsBodyHidden(walls, eye))
        {
            double reach = Vector3d.Distance(look, hit.Position);
            eye = look + (back * Math.Max(0.0, reach - KeepOffInUnits));
        }

        Vector3d toLook = look - eye;
        double length = toLook.Length;
        float yaw = (float)Math.Atan2(toLook.X, toLook.Y);
        float pitch = length > 0.0 ? (float)Math.Asin(toLook.Z / length) : 0f;
        _camera = new Camera3D(eye / UnitsPerMetre, Camera3D.LookRotation(yaw, pitch));
    }

    // 0x483A00: whether every line from the eight points around the body to the camera is blocked.
    private bool IsBodyHidden(WorldRayCast walls, Vector3d eye)
    {
        foreach (Vector3d point in BodyPoints)
        {
            if (!walls(_feet + point, eye, out _))
            {
                return false;
            }
        }

        return true;
    }
}
