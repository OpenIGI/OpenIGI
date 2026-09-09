using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;

namespace OpenIGI.Presentation.Cameras;

/// <summary>Which part of the map computer's transition is running.</summary>
public enum MapComputerPhase
{
    /// <summary>Nothing is happening; the computer is shut and the camera belongs to the player.</summary>
    Idle,

    /// <summary>Climbing out of the player's eye to the map's vantage, turning to face straight down.</summary>
    Ascend,

    /// <summary>Held at the vantage while the display is driven and the interface arrives.</summary>
    Boot,

    /// <summary>Open. The camera belongs to the map, which the player is panning and zooming.</summary>
    Open,

    /// <summary>The interface is leaving and the display is being taken down.</summary>
    Shutdown,

    /// <summary>Falling back out of the map and into the player's eye.</summary>
    Descend,

    /// <summary>Finished. The next frame hands the view back.</summary>
    Done,
}

/// <summary>
/// The shot that carries the player out of their own eyes and up into the map, and back down again: a
/// vertical pull-out to the map's vantage, a display being driven from cold, and — when they close it —
/// a fall back into the head from wherever across the level they had panned to.
/// </summary>
/// <remarks>
/// <para>
/// The deliberate echo of <see cref="MissionEntryCamera"/> is the point. That shot ends by dropping into
/// the player's eyes; this one begins by reversing it, so opening the computer reads as stepping back out
/// to the vantage the mission was surveyed from. Structured the same way for the same reason: poses and a
/// clock in, a camera and a handful of presentation numbers out, no device and no renderer, so the whole
/// of it can be reasoned about and tested as arithmetic.
/// </para>
/// <para>
/// <b>The field of view is part of the move.</b> A climb from eye height to the map's altitude is a rise
/// of some nine hundred metres, and covering it at a gameplay field of view is a fall away so fast it
/// reads as a mistake. The map's own view is a long lens — a narrow field from a long way up, which is
/// what makes a map flat — so the shot narrows into it as it climbs, and what the player sees is mostly
/// the perspective flattening rather than the ground receding. That is why <see cref="FieldOfView"/> is
/// published alongside the camera: the scene pass has to be given both or the move is only half of itself.
/// </para>
/// <para>
/// <b>The angles resolve early and the height resolves late.</b> The turn to straight down and to north
/// finishes at four fifths of the climb, so the last stretch is a clean vertical rise into a heading that
/// is already correct — a camera still rotating as it arrives reads as a stumble. The descent is the same
/// idea mirrored: it swings out of the map's heading over the first part of the fall and spends the rest
/// of it dropping into a look direction the player is about to be handed.
/// </para>
/// <para>
/// <b>The close starts where the map is, not where the player is.</b> A player who has panned across the
/// level and closes the computer gets a flight back rather than a cut, which costs nothing to implement —
/// the descent already interpolates from a pose — and is the difference between the map being a screen
/// and the map being a place they were looking at.
/// </para>
/// </remarks>
public sealed class MapComputerCamera
{
    /// <summary>How long the climb to the map's vantage takes, in seconds.</summary>
    public const float AscendSeconds = 1.05f;

    /// <summary>How long the display takes to come up once the camera has arrived, in seconds.</summary>
    public const float BootSeconds = 0.55f;

    /// <summary>How long the interface takes to leave when the computer is closed, in seconds.</summary>
    public const float ShutdownSeconds = 0.22f;

    /// <summary>How long the fall back into the player's eye takes, in seconds.</summary>
    public const float DescendSeconds = 0.95f;

    /// <summary>The pitch the map is read at: straight down.</summary>
    public const float MapPitch = -MathF.PI * 0.5f;

    /// <summary>How far above the eye the descent aims before it lands, in metres.</summary>
    /// <remarks>
    /// The fall arrives through a point overhead rather than along a straight line, so the camera settles
    /// onto the head from above instead of sliding into it sideways from wherever the map had been panned.
    /// </remarks>
    private const double ApproachAbove = 3.2;

    /// <summary>The widest the shot's own smear gets.</summary>
    /// <remarks>
    /// This camera covers more ground per frame than anything else in the game — a kilometre in about a
    /// second — so without a shutter the climb is a slideshow of sharp stills. Exactly zero by the
    /// hand-over at either end, where the player's own setting takes over again.
    /// </remarks>
    private const float PeakMotionBlur = 0.55f;

    private CameraPose _from;
    private CameraPose _to;
    private CameraPose _landing;
    private float _fromFieldOfView;
    private float _toFieldOfView;
    private float _phaseElapsed;
    private float _seconds;
    private CameraPose _pose;

    /// <summary>Gets which part of the transition is running.</summary>
    public MapComputerPhase Phase { get; private set; } = MapComputerPhase.Idle;

    /// <summary>Gets a value indicating whether anything of the computer is on screen.</summary>
    public bool IsRunning => Phase is > MapComputerPhase.Idle and < MapComputerPhase.Done;

    /// <summary>Gets a value indicating whether this shot, rather than the map, owns the camera.</summary>
    public bool IsFlying => Phase is MapComputerPhase.Ascend or MapComputerPhase.Descend;

    /// <summary>Gets a value indicating whether the map is taking the player's pan and zoom.</summary>
    /// <remarks>
    /// The boot deliberately does not: the display is still coming up, and a drag that landed during it
    /// would move a map the player cannot yet read.
    /// </remarks>
    public bool IsInteractive => Phase == MapComputerPhase.Open;

    /// <summary>Gets a value indicating whether a close may begin.</summary>
    public bool CanClose => Phase is MapComputerPhase.Open or MapComputerPhase.Boot;

    /// <summary>Gets the camera to draw the world from while <see cref="IsFlying"/> holds.</summary>
    public Camera3D Camera => _pose.ToCamera();

    /// <summary>Gets the vertical field of view that camera wants, in radians.</summary>
    public float FieldOfView { get; private set; } = 1f;

    /// <summary>Gets the pose the shot is at, for the map to resume from when a close begins.</summary>
    public CameraPose Pose => _pose;

    /// <summary>Gets a continuously rising clock for the display's flicker and drift, in seconds.</summary>
    public float Seconds => _seconds;

    /// <summary>Gets how much of the phosphor display transform applies, 0 to 1.</summary>
    /// <remarks>
    /// In over the back half of the climb rather than the whole of it. The picture has to be recognisably
    /// the world the player was standing in for long enough to register that it is the same place, and
    /// only then become a readout of it; grading from the first frame makes the climb look like a wipe.
    /// </remarks>
    public float DisplayAmount =>
        Phase switch
        {
            MapComputerPhase.Ascend => Easing.Evaluate(
                Ease.InOutCubic,
                Math.Clamp(((_phaseElapsed / AscendSeconds) - 0.45f) / 0.55f, 0f, 1f)),
            MapComputerPhase.Boot or MapComputerPhase.Open or MapComputerPhase.Shutdown => 1f,
            MapComputerPhase.Descend => 1f - Easing.Evaluate(
                Ease.InOutCubic,
                Math.Clamp((_phaseElapsed / DescendSeconds) / 0.5f, 0f, 1f)),
            _ => 0f,
        };

    /// <summary>Gets the power-on disturbance, 0 to 1.</summary>
    /// <remarks>
    /// Two bursts rather than one ramp: a short one as the grade first takes hold at the top of the
    /// climb, and the larger one across the boot itself, decaying as the display finds its lock. A single
    /// envelope over both reads as a fault rather than as a screen starting.
    /// </remarks>
    public float BootEnergy =>
        Phase switch
        {
            MapComputerPhase.Ascend => 0.45f * Easing.Evaluate(
                Ease.InQuad,
                Math.Clamp(((_phaseElapsed / AscendSeconds) - 0.75f) / 0.25f, 0f, 1f)),
            MapComputerPhase.Boot => 1f - Easing.Evaluate(
                Ease.OutCubic,
                Math.Clamp(_phaseElapsed / (BootSeconds * 0.8f), 0f, 1f)),
            MapComputerPhase.Shutdown => Easing.Evaluate(
                Ease.InCubic,
                Math.Clamp(_phaseElapsed / ShutdownSeconds, 0f, 1f)) * 0.8f,
            MapComputerPhase.Descend => 0.8f * (1f - Easing.Evaluate(
                Ease.OutQuint,
                Math.Clamp(_phaseElapsed / (DescendSeconds * 0.35f), 0f, 1f))),
            _ => 0f,
        };

    /// <summary>Gets how strongly the refresh band shows, 0 for none.</summary>
    public float SweepStrength =>
        Phase == MapComputerPhase.Boot
            ? 1f - Easing.Evaluate(Ease.InQuad, Math.Clamp(_phaseElapsed / BootSeconds, 0f, 1f))
            : 0f;

    /// <summary>Gets where the refresh band is, 0 at the top of the frame to 1 at the bottom.</summary>
    /// <remarks>
    /// One pass down the tube over the boot, easing out, so the band decelerates into the bottom of the
    /// frame instead of running off it at full speed.
    /// </remarks>
    public float SweepPosition =>
        Easing.Evaluate(Ease.OutCubic, Math.Clamp(_phaseElapsed / BootSeconds, 0f, 1f));

    /// <summary>Gets how much of the interface is showing, 0 none to 1 all of it.</summary>
    /// <remarks>
    /// The chrome arrives behind the refresh band rather than with it — the display comes up first and
    /// the interface is drawn onto a screen that is already lit, which is the order the fiction wants.
    /// </remarks>
    public float ChromeOpacity =>
        Phase switch
        {
            MapComputerPhase.Boot => Easing.Evaluate(
                Ease.OutCubic,
                Math.Clamp(((_phaseElapsed / BootSeconds) - 0.35f) / 0.65f, 0f, 1f)),
            MapComputerPhase.Open => 1f,
            MapComputerPhase.Shutdown => 1f - Easing.Evaluate(
                Ease.InCubic,
                Math.Clamp(_phaseElapsed / ShutdownSeconds, 0f, 1f)),
            _ => 0f,
        };

    /// <summary>Gets how much the frame's own movement smears, 0 none to 1 fully.</summary>
    public float MotionBlur =>
        Phase switch
        {
            // In hard over the first half of the climb, where the ground is falling away fastest, and
            // out again before the top so the vantage is arrived at sharp.
            MapComputerPhase.Ascend => PeakMotionBlur * MathF.Sin(
                MathF.PI * Math.Clamp(_phaseElapsed / AscendSeconds, 0f, 1f)),
            MapComputerPhase.Descend => PeakMotionBlur * MathF.Sin(
                MathF.PI * Math.Clamp(_phaseElapsed / DescendSeconds, 0f, 1f)),
            _ => 0f,
        };

    /// <summary>Gets the settings the resolve needs to put this frame on the tube.</summary>
    public TacticalDisplaySettings Display =>
        new(DisplayAmount, BootEnergy, SweepStrength, SweepPosition, _seconds);

    /// <summary>Begins the climb out of the player's eye.</summary>
    /// <param name="eye">Where the player is looking from now.</param>
    /// <param name="eyeFieldOfView">The vertical field of view gameplay is being drawn at, in radians.</param>
    /// <param name="vantage">The pose the map will be read from — straight down over the player.</param>
    /// <param name="vantageFieldOfView">The vertical field of view the map wants, in radians.</param>
    public void BeginOpen(
        in CameraPose eye,
        float eyeFieldOfView,
        in CameraPose vantage,
        float vantageFieldOfView)
    {
        _from = eye;
        _to = vantage;
        _fromFieldOfView = eyeFieldOfView;
        _toFieldOfView = vantageFieldOfView;
        _landing = eye;
        _pose = eye;
        FieldOfView = eyeFieldOfView;
        _phaseElapsed = 0f;
        Phase = MapComputerPhase.Ascend;
    }

    /// <summary>Begins the fall back into the player's eye.</summary>
    /// <param name="vantage">Where the map is being read from, which is where the fall starts.</param>
    /// <param name="vantageFieldOfView">The field of view the map is being read at, in radians.</param>
    /// <param name="eye">Where the player will be looking from; re-read every frame as they settle.</param>
    /// <param name="eyeFieldOfView">The vertical field of view gameplay will resume at, in radians.</param>
    public void BeginClose(
        in CameraPose vantage,
        float vantageFieldOfView,
        in CameraPose eye,
        float eyeFieldOfView)
    {
        _from = vantage;
        _to = eye;
        _landing = eye;
        _fromFieldOfView = vantageFieldOfView;
        _toFieldOfView = eyeFieldOfView;
        _pose = vantage;
        FieldOfView = vantageFieldOfView;
        _phaseElapsed = 0f;
        Phase = MapComputerPhase.Shutdown;
    }

    /// <summary>Puts the shot back to idle.</summary>
    public void Reset()
    {
        Phase = MapComputerPhase.Idle;
        _phaseElapsed = 0f;
    }

    /// <summary>Advances the shot by a real-time step and produces this frame's camera.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last call.</param>
    /// <param name="liveEye">
    /// Where the player is looking from now. The climb leaves from it and the fall lands on it, both
    /// re-read every frame: the body is still settling under the camera at either end, and a landing
    /// aimed at a snapshot taken a second earlier arrives beside the head rather than in it.
    /// </param>
    /// <param name="liveVantage">Where the map is being read from now, which the fall leaves from.</param>
    /// <param name="liveVantageFieldOfView">The field of view the map is being read at, in radians.</param>
    public void Update(
        float elapsedSeconds,
        in CameraPose liveEye,
        in CameraPose liveVantage,
        float liveVantageFieldOfView)
    {
        if (Phase is MapComputerPhase.Idle or MapComputerPhase.Done)
        {
            return;
        }

        float step = Math.Clamp(elapsedSeconds, 0f, 0.1f);
        _seconds += step;
        _phaseElapsed += step;
        _landing = liveEye;

        // The climb's destination follows the map: the player is stationary while the computer is up, but
        // the vantage is derived from their position and the map's zoom, and reading it live means the
        // arrival is exactly the pose the map will take over with rather than nearly it.
        if (Phase == MapComputerPhase.Ascend)
        {
            _to = liveVantage;
            _toFieldOfView = liveVantageFieldOfView;
        }
        else if (Phase == MapComputerPhase.Shutdown)
        {
            _from = liveVantage;
            _fromFieldOfView = liveVantageFieldOfView;
        }

        AdvancePhase();

        switch (Phase)
        {
            case MapComputerPhase.Ascend:
                _pose = Ascending(out float ascendFov);
                FieldOfView = ascendFov;
                break;

            case MapComputerPhase.Descend:
                _pose = Descending(out float descendFov);
                FieldOfView = descendFov;
                break;

            case MapComputerPhase.Boot:
            case MapComputerPhase.Open:
            case MapComputerPhase.Shutdown:
                _pose = liveVantage;
                FieldOfView = liveVantageFieldOfView;
                break;

            default:
                _pose = _landing;
                break;
        }
    }

    private void AdvancePhase()
    {
        switch (Phase)
        {
            case MapComputerPhase.Ascend when _phaseElapsed >= AscendSeconds:
                EnterPhase(MapComputerPhase.Boot);
                break;

            case MapComputerPhase.Boot when _phaseElapsed >= BootSeconds:
                EnterPhase(MapComputerPhase.Open);
                break;

            case MapComputerPhase.Shutdown when _phaseElapsed >= ShutdownSeconds:
                // The fall leaves from wherever the shutdown was holding, which is the map's own pose.
                _from = _pose;
                _fromFieldOfView = FieldOfView;
                _to = _landing;
                EnterPhase(MapComputerPhase.Descend);
                break;

            case MapComputerPhase.Descend when _phaseElapsed >= DescendSeconds:
                EnterPhase(MapComputerPhase.Done);
                break;

            default:
                break;
        }
    }

    private void EnterPhase(MapComputerPhase phase)
    {
        Phase = phase;
        _phaseElapsed = 0f;
    }

    private CameraPose Ascending(out float fieldOfView)
    {
        float t = Math.Clamp(_phaseElapsed / AscendSeconds, 0f, 1f);
        float rise = Easing.Evaluate(Ease.InOutCubic, t);

        // The turn finishes at four fifths, so the last of the climb is a straight rise into a heading
        // that is already correct.
        float settle = Easing.Evaluate(Ease.InOutCubic, Math.Clamp(t / 0.8f, 0f, 1f));

        fieldOfView = ZoomLerp(_fromFieldOfView, _toFieldOfView, rise);
        return new CameraPose(
            new Vector3d(
                _from.Position.X + ((_to.Position.X - _from.Position.X) * rise),
                _from.Position.Y + ((_to.Position.Y - _from.Position.Y) * rise),
                _from.Position.Z + ((_to.Position.Z - _from.Position.Z) * rise)),
            CameraPose.LerpAngle(_from.Yaw, _to.Yaw, settle),
            _from.Pitch + ((_to.Pitch - _from.Pitch) * settle));
    }

    private CameraPose Descending(out float fieldOfView)
    {
        float t = Math.Clamp(_phaseElapsed / DescendSeconds, 0f, 1f);
        if (t >= 1f)
        {
            fieldOfView = _toFieldOfView;
            return _landing;
        }

        float fall = Easing.Evaluate(Ease.InOutCubic, t);
        fieldOfView = ZoomLerp(_fromFieldOfView, _toFieldOfView, fall);

        // A quadratic Bezier through a point directly over the head, so the camera comes down onto the
        // player rather than in at them along whatever diagonal the map had been panned to.
        Vector3d approach = new(
            _landing.Position.X,
            _landing.Position.Y,
            _landing.Position.Z + ApproachAbove);

        double inv = 1.0 - fall;
        double a = inv * inv;
        double b = 2.0 * inv * fall;
        double c = (double)fall * fall;

        Vector3d position = new(
            (a * _from.Position.X) + (b * approach.X) + (c * _landing.Position.X),
            (a * _from.Position.Y) + (b * approach.Y) + (c * _landing.Position.Y),
            (a * _from.Position.Z) + (b * approach.Z) + (c * _landing.Position.Z));

        // The angles resolve over the first four fifths for the same reason the climb's do, from the
        // other end: the last of the fall is a drop into a look direction the player is about to own.
        float settle = Easing.Evaluate(Ease.InOutCubic, Math.Clamp(t / 0.8f, 0f, 1f));
        return new CameraPose(
            position,
            CameraPose.LerpAngle(_from.Yaw, _landing.Yaw, settle),
            _from.Pitch + ((_landing.Pitch - _from.Pitch) * settle));
    }

    /// <summary>Interpolates a field of view geometrically rather than linearly.</summary>
    /// <param name="from">The angle at <paramref name="t"/> = 0, in radians.</param>
    /// <param name="to">The angle at <paramref name="t"/> = 1, in radians.</param>
    /// <param name="t">The blend factor.</param>
    /// <returns>The blended angle.</returns>
    /// <remarks>
    /// Zoom is perceived in ratios: going from 60 degrees to 30 looks like the same amount of zoom as
    /// going from 30 to 15, and a linear ramp between two angles an octave apart therefore appears to
    /// race at the wide end and crawl at the narrow one. This shot spans roughly a factor of five, which
    /// is far enough for that to be the difference between a move and a lurch.
    /// </remarks>
    private static float ZoomLerp(float from, float to, float t)
    {
        float a = MathF.Max(from, 1e-4f);
        float b = MathF.Max(to, 1e-4f);
        return a * MathF.Pow(b / a, Math.Clamp(t, 0f, 1f));
    }
}
