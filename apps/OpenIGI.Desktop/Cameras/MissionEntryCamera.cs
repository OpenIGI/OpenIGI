using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using OpenIGI.UI.Animation;

namespace OpenIGI.Desktop;

/// <summary>Which part of the mission-entry shot is running.</summary>
public enum MissionEntryPhase
{
    /// <summary>Nothing is happening; the camera belongs to whoever else wants it.</summary>
    Idle,

    /// <summary>The interface is leaving and the camera has begun to lift out of the menu's orbit.</summary>
    Reveal,

    /// <summary>Rising to the top-down vantage over where the orbit was.</summary>
    Ascend,

    /// <summary>Travelling across the level to the mission's start, at altitude.</summary>
    Travel,

    /// <summary>Holding over the start. Extends until the mission is ready to be handed over.</summary>
    Hold,

    /// <summary>Dropping in behind the player's head.</summary>
    Descend,

    /// <summary>The view has been handed over; only the framing is still leaving.</summary>
    Chrome,

    /// <summary>Finished.</summary>
    Done,
}

/// <summary>
/// The single continuous shot that carries the player from the menu into their own eyes: the interface
/// fades, the camera rises out of the menu's orbit to a top-down vantage, travels across the level to the
/// mission's start, holds, and then drops in behind the character's head and becomes their view.
/// </summary>
/// <remarks>
/// <para>
/// It owns no device, no renderer and no loop. Poses and a clock go in; a <see cref="Camera3D"/> and a
/// handful of presentation numbers come out. That is what lets the whole of it be tested without a
/// graphics context, which matters because almost everything that can be wrong with a camera move is
/// wrong in the arithmetic rather than in the drawing.
/// </para>
/// <para>
/// <b>Where the weight comes from.</b> Every phase eases, and which curve each one uses is a decision
/// rather than a default. The rise carries the largest rotation in the shot — some eighty degrees, from
/// the orbit's gentle downward tilt to nearly straight down — and a cubic in-out spends enough of itself
/// near both ends that the camera appears to be heavy. The travel is a long flat move where a cubic
/// would spend the whole phase accelerating and decelerating and never appear to cruise, so it takes a
/// quadratic instead. The descent is cubic again, because arriving is the moment that most needs to
/// slow.
/// </para>
/// <para>
/// <b>The one thing that is not a tween.</b> The menu's camera is <em>moving</em> when Play is pressed —
/// it is a slow orbit, two to eight metres a second. Any tween that starts from a snapshot of that pose
/// begins at zero velocity, so the picture visibly stops dead before the shot starts. The rise therefore
/// blends against the <em>live</em> orbit for its first half-second, which preserves the orbit's velocity
/// exactly at the moment of the cut and makes the move look like a continuation rather than a new shot.
/// That is why <see cref="Update"/> is given the live showcase pose every frame.
/// </para>
/// </remarks>
public sealed class MissionEntryCamera
{
    /// <summary>How long the interface takes to leave, in seconds.</summary>
    public const float RevealSeconds = 0.50f;

    /// <summary>How long the rise to the top-down vantage takes, in seconds.</summary>
    public const float AscendSeconds = 1.60f;

    /// <summary>How long the travel across the level takes, in seconds.</summary>
    public const float TravelSeconds = 1.20f;

    /// <summary>The nominal beat held over the start before the descent, in seconds.</summary>
    public const float HoldSeconds = 0.15f;

    /// <summary>How long the descent into the player's eyes takes, in seconds.</summary>
    public const float DescendSeconds = 1.20f;

    /// <summary>How long the framing takes to leave after the view has been handed over, in seconds.</summary>
    public const float ChromeSeconds = 0.25f;

    /// <summary>The pitch held at altitude, in radians — eighty-five degrees down, not ninety.</summary>
    /// <remarks>
    /// At exactly straight down the yaw stops meaning anything and the frame reads flat and airless.
    /// Five degrees off keeps a sliver of horizon, keeps the buildings showing their sides, and keeps the
    /// heading legible as the camera travels — which is what makes it read as a map rather than a plan.
    /// </remarks>
    public const float ApexPitch = -1.4835f;

    /// <summary>The lowest the shot will climb above the ground, in metres.</summary>
    /// <remarks>Below this a spawn just over the ridge would barely leave the ground.</remarks>
    public const double MinimumApexRise = 90.0;

    /// <summary>The highest the shot will climb above the ground, in metres.</summary>
    /// <remarks>Well inside the far plane, and inside the range the shadow cascades are fitted over.</remarks>
    public const double MaximumApexRise = 320.0;

    /// <summary>How long the rise spends blending out of the live orbit, in seconds.</summary>
    private const float OrbitBlendSeconds = 0.45f;

    /// <summary>How far behind and above the head the descent aims before it lands, in metres.</summary>
    private const double ApproachBehind = 2.4;
    private const double ApproachAbove = 0.9;

    /// <summary>How close the camera gets to the eye before the standing body is taken away, in metres.</summary>
    /// <remarks>
    /// <para>
    /// Far enough out that the character is still a figure standing in the level when it goes, rather than
    /// an object arriving at the lens. The descent ends AT the eye, which is inside the head, and the last
    /// stretch of it is fast — so a threshold measured in arm's lengths means the body sweeps from
    /// waist-height to filling the screen and then vanishes, all within a few frames. That reads as the
    /// character flashing, which is precisely what it is.
    /// </para>
    /// <para>
    /// Taking it away at six metres costs nothing: the camera crosses the remaining distance in a small
    /// fraction of a second, by which point it is looking at where the character's head was rather than
    /// at the character, and there is nothing left to notice going.
    /// </para>
    /// </remarks>
    private const double BodyHiddenWithin = 6.0;

    /// <summary>How long a hold may extend before the shot gives up waiting and lands anyway, in seconds.</summary>
    private const float HoldPatienceSeconds = 6.0f;

    private readonly HandheldShake _shake = new();

    private CameraPose _start;
    private CameraPose _apexOverStart;
    private CameraPose _apexOverSpawn;
    private CameraPose _landing;
    private float _travelYaw;

    private float _elapsed;
    private float _phaseElapsed;
    private bool _promoted;
    private bool _bodyHidden;
    private CameraPose _pose;
    private Camera3D _camera = Camera3D.Default;

    /// <summary>Gets which part of the shot is running.</summary>
    public MissionEntryPhase Phase { get; private set; } = MissionEntryPhase.Idle;

    /// <summary>Gets a value indicating whether the shot owns the view and the player owns no input.</summary>
    public bool IsCameraActive => Phase is > MissionEntryPhase.Idle and < MissionEntryPhase.Chrome;

    /// <summary>Gets a value indicating whether anything the shot draws is still on screen.</summary>
    public bool IsRunning => Phase is > MissionEntryPhase.Idle and < MissionEntryPhase.Done;

    /// <summary>Gets a value indicating whether the mission should be brought up now.</summary>
    /// <remarks>
    /// Raised on reaching the hold and cleared by <see cref="NotifyPromoted"/>. The hold is where the
    /// hand-over belongs: it is the one moment in the shot where the camera is not travelling, so
    /// whatever the promotion costs is spent against a picture that was static anyway.
    /// </remarks>
    public bool WantsPromotion => Phase == MissionEntryPhase.Hold && !_promoted;

    /// <summary>Gets a value indicating whether the player's standing body should be drawn.</summary>
    public bool ShowPlayerBody { get; private set; }

    /// <summary>Gets how long the hold has been waiting for the mission, in seconds.</summary>
    public float HeldWaiting { get; private set; }

    /// <summary>Gets the eye to draw the world from this frame, with the shake applied.</summary>
    public Camera3D Camera => _camera;

    /// <summary>Gets the pose the shot is at, before the shake — for tests and for logging.</summary>
    public CameraPose Pose => _pose;

    /// <summary>Gets how much of the interface is still showing, 1 all of it to 0 none.</summary>
    public float ShellOpacity =>
        Phase <= MissionEntryPhase.Idle
            ? 1f
            : 1f - Easing.Evaluate(Ease.OutCubic, Math.Clamp(_elapsed / RevealSeconds, 0f, 1f));

    /// <summary>Gets how far the cinematic bars have come in, 0 open to 1 closed.</summary>
    public float LetterboxAmount
    {
        get
        {
            if (Phase is MissionEntryPhase.Idle or MissionEntryPhase.Done)
            {
                return 0f;
            }

            // Out over the last stretch of the descent and the whole of the chrome tail, so the frame has
            // opened up again by the time the player has control.
            if (Phase == MissionEntryPhase.Chrome)
            {
                return 1f - Easing.Evaluate(Ease.InOutCubic, Math.Clamp(_phaseElapsed / ChromeSeconds, 0f, 1f));
            }

            return Easing.Evaluate(Ease.OutCubic, Math.Clamp(_elapsed / (RevealSeconds + 0.05f), 0f, 1f));
        }
    }

    /// <summary>Gets how strongly the mission's title is showing, 0 to 1.</summary>
    public float TitleOpacity
    {
        get
        {
            if (Phase is MissionEntryPhase.Travel or MissionEntryPhase.Hold)
            {
                return Phase == MissionEntryPhase.Travel
                    ? Easing.Evaluate(Ease.OutQuad, Math.Clamp(_phaseElapsed / 0.5f, 0f, 1f))
                    : 1f;
            }

            if (Phase == MissionEntryPhase.Descend)
            {
                return 1f - Easing.Evaluate(Ease.InOutQuad, Math.Clamp(_phaseElapsed / 0.3f, 0f, 1f));
            }

            return 0f;
        }
    }

    /// <summary>Gets how much the frame's own movement smears, 0 none to 1 fully.</summary>
    /// <remarks>
    /// <para>
    /// A real camera's shutter is open for part of a frame, so anything that moves across it during that
    /// time is smeared — and this camera moves further in a second than a player ever will. Without it the
    /// travel across the level reads as a slideshow of sharp stills, which is the single biggest thing
    /// separating a camera move that looks shot from one that looks stepped.
    /// </para>
    /// <para>
    /// Shaped per phase rather than derived from the camera's speed, because the speed varies by two
    /// orders of magnitude across the shot — hundreds of metres a second at altitude against walking pace
    /// at the landing — and any normalisation of that ends up being a hand-authored curve wearing a
    /// disguise. Strongest across the travel, backed off over the hold so the beat is legible, and
    /// <b>exactly zero</b> by the hand-over: gameplay gets the player's own setting back, and a smear
    /// still running on the first frame they control would be the shot overstaying.
    /// </para>
    /// </remarks>
    public float MotionBlur =>
        Phase switch
        {
            MissionEntryPhase.Reveal => 0f,
            MissionEntryPhase.Ascend => PeakMotionBlur
                * Easing.Evaluate(Ease.InOutQuad, Math.Clamp(_phaseElapsed / AscendSeconds, 0f, 1f)),
            // Weighted hard toward the descent. The travel covers the most ground per frame, so the same
            // setting produces by far the longest smear there — enough at three quarters strength to wash
            // the whole vantage out — while the descent is the move the eye actually follows and the one
            // that wants the shutter. Nearly nothing over the hold, so the beat is a clean look at the
            // place rather than a smear of it.
            MissionEntryPhase.Travel => PeakMotionBlur * 0.25f,
            MissionEntryPhase.Hold => PeakMotionBlur * 0.1f,
            MissionEntryPhase.Descend => PeakMotionBlur
                * (1f - Easing.Evaluate(Ease.InOutCubic, Math.Clamp(_phaseElapsed / (DescendSeconds * 0.9f), 0f, 1f))),
            _ => 0f,
        };

    /// <summary>Gets what the camera is focused on, in metres, or 0 for no depth of field.</summary>
    /// <remarks>
    /// The distance to the thing being looked at — the player's own start — so the level under the
    /// vantage stays sharp while everything far behind it softens. The menu's own framing cannot be
    /// reused here: it focuses at a fixed forty metres, which from three hundred metres up puts the
    /// entire world beyond the focal plane and simply blurs all of it.
    /// </remarks>
    /// <remarks>
    /// Zero once the menu's framing has finished easing off, which turns the defocus gather off outright
    /// rather than leaving it running against a focal plane nothing is behind. The shot wants depth of
    /// field only while it is letting go of the menu's; past that the level is the subject and the only
    /// thing that should still be softening the picture is the shot's own movement.
    /// </remarks>
    public float FocalDistanceMetres =>
        Phase is MissionEntryPhase.Idle or MissionEntryPhase.Done or MissionEntryPhase.Chrome
            || MenuDepthOfField <= 0.001f
            ? 0f
            : (float)Math.Max(1.0, Distance(_pose.Position, _landing.Position));

    /// <summary>The widest the shot's own smear gets.</summary>
    /// <remarks>
    /// Above the ultra preset's own 0.4, because this is a camera move rather than a player turning their
    /// head and the whole point of it is to read as shot rather than stepped.
    /// <para>
    /// An earlier value here was chosen by looking at a washed-out vantage and blaming the smear for it.
    /// That was impossible — motion blur was doing nothing at all at the time, for the reason recorded in
    /// <c>GLGraphicsDevice.BeginSceneCapture</c> — so the haze was the depth of field and the fog, and the
    /// conclusion drawn from it was worthless. Tuned again against an effect that actually runs.
    /// </para>
    /// </remarks>
    private const float PeakMotionBlur = 0.6f;

    /// <summary>Gets how much of the menu's depth of field is still applied, 1 all of it to 0 none.</summary>
    /// <remarks>
    /// The menu frames its backdrop with a close focal plane and full blur, which would be absurd to play
    /// through. Letting it off across the travel and the descent is what makes the world sharpen as the
    /// player drops into it.
    /// </remarks>
    public float MenuDepthOfField =>
        Phase switch
        {
            MissionEntryPhase.Idle => 1f,
            MissionEntryPhase.Reveal => 1f,

            // Off across the CLIMB rather than across the travel. The menu's framing exists to push a
            // backdrop behind an interface, and by the time the interface has gone and the camera is
            // looking at the level from above, the level is the subject and wants to be sharp. Holding
            // the menu's full blur all the way to the vantage washed it out.
            MissionEntryPhase.Ascend => 1f - Easing.Evaluate(Ease.InOutQuad, Math.Clamp(_phaseElapsed / AscendSeconds, 0f, 1f)),
            _ => 0f,
        };

    /// <summary>Begins the shot.</summary>
    /// <param name="showcase">The pose the menu's orbit is in at the moment Play was pressed.</param>
    /// <param name="spawnPositionUnits">Where the level puts the player, in engine units.</param>
    /// <param name="groundHeightMetres">The height to measure the vantage above, in metres.</param>
    public void Begin(in CameraPose showcase, Vector3d spawnPositionUnits, double groundHeightMetres)
    {
        const double unitsPerMetre = 4096.0;

        _start = showcase;
        Vector3d spawn = new(
            spawnPositionUnits.X / unitsPerMetre,
            spawnPositionUnits.Y / unitsPerMetre,
            spawnPositionUnits.Z / unitsPerMetre);

        double dx = spawn.X - showcase.Position.X;
        double dy = spawn.Y - showcase.Position.Y;
        double travelled = Math.Sqrt((dx * dx) + (dy * dy));

        // How high to go, from how far there is to go. A fixed altitude reads wrong at both ends: a short
        // hop to a spawn just over the ridge looks like it went to orbit for no reason, and a move across
        // a whole level from the same height never shows the player where they are going. At the game's
        // ninety-degree horizontal field of view an altitude of h frames roughly 2h metres across the
        // screen, so a little over half the distance puts the whole journey a bit wider than one screen.
        double rise = Math.Clamp(travelled * 0.55, MinimumApexRise, MaximumApexRise);
        double apexZ = Math.Max(groundHeightMetres, Math.Max(showcase.Position.Z, spawn.Z)) + rise;

        // Where the shot faces while it travels. Below a metre the direction is meaningless, so it keeps
        // whatever the orbit was doing rather than snapping to an arbitrary heading.
        _travelYaw = travelled > 1.0 ? (float)Math.Atan2(dx, dy) : showcase.Yaw;

        // The rise leans a little way along the journey rather than going straight up, so the top of the
        // climb is already moving in the direction the travel will continue.
        double lead = Math.Min(travelled * 0.15, 40.0);
        double leadX = travelled > 1.0 ? dx / travelled * lead : 0.0;
        double leadY = travelled > 1.0 ? dy / travelled * lead : 0.0;

        _apexOverStart = new CameraPose(
            new Vector3d(showcase.Position.X + leadX, showcase.Position.Y + leadY, apexZ),
            _travelYaw,
            ApexPitch);

        _apexOverSpawn = new CameraPose(new Vector3d(spawn.X, spawn.Y, apexZ), _travelYaw, ApexPitch);

        // A placeholder until the player exists; the descent re-reads the real eye every frame.
        _landing = new CameraPose(spawn, _travelYaw, 0f);

        _elapsed = 0f;
        _phaseElapsed = 0f;
        _promoted = false;
        HeldWaiting = 0f;
        ShowPlayerBody = false;
        _bodyHidden = false;
        Phase = MissionEntryPhase.Reveal;
        _pose = showcase;
        _camera = showcase.ToCamera();
    }

    /// <summary>Tells the shot the mission has been brought up and the descent may begin.</summary>
    public void NotifyPromoted() => _promoted = true;

    /// <summary>Puts the shot back to idle.</summary>
    public void Reset()
    {
        Phase = MissionEntryPhase.Idle;
        _elapsed = 0f;
        _phaseElapsed = 0f;
        _promoted = false;
        HeldWaiting = 0f;
        ShowPlayerBody = false;
        _bodyHidden = false;
        _camera = Camera3D.Default;
    }

    /// <summary>Advances the shot by a real-time step and produces this frame's camera.</summary>
    /// <param name="elapsedSeconds">The real seconds since the last call.</param>
    /// <param name="liveShowcase">Where the menu's orbit is now, so the rise can inherit its velocity.</param>
    /// <param name="gameplayEye">The player's own camera once it exists, which the descent lands on exactly.</param>
    public void Update(float elapsedSeconds, in CameraPose liveShowcase, CameraPose? gameplayEye)
    {
        if (Phase is MissionEntryPhase.Idle or MissionEntryPhase.Done)
        {
            return;
        }

        float step = Math.Clamp(elapsedSeconds, 0f, 0.1f);
        _elapsed += step;
        _phaseElapsed += step;

        if (gameplayEye is { } landing)
        {
            _landing = landing;
        }

        AdvancePhase();

        _pose = Phase switch
        {
            MissionEntryPhase.Reveal => liveShowcase,
            MissionEntryPhase.Ascend => Ascending(liveShowcase),
            MissionEntryPhase.Travel => Travelling(),
            MissionEntryPhase.Hold => Holding(),
            MissionEntryPhase.Descend => Descending(),
            _ => _landing,
        };

        // The shake, weighted so it is absent at both ends of the shot and full at altitude. Rotation
        // only, and the roll is composed on top of the look because LookRotation cannot carry one.
        _shake.Sample(_elapsed, ShakeWeight, out float shakeYaw, out float shakePitch, out float shakeRoll);

        var shaken = new CameraPose(_pose.Position, _pose.Yaw + shakeYaw, _pose.Pitch + shakePitch);
        _camera = new Camera3D(
            shaken.Position,
            HandheldShake.ApplyRoll(Camera3D.LookRotation(shaken.Yaw, shaken.Pitch), shakeRoll));

        // The standing body is taken away once the camera is close enough that the head fills the frame,
        // which is also close enough that the near plane is already cutting into it. Measured as a
        // distance rather than as a fraction of the phase so it behaves the same however far the drop is.
        //
        // ONCE HIDDEN IT STAYS HIDDEN. The comparison alone is not enough: the landing is re-read every
        // frame from the player, which by this point is a live body settling onto the ground, so the
        // distance jitters. Around the threshold that flipped the body on and off across several frames,
        // and since each of those frames has the camera close enough to be inside the model, what the
        // player saw was the character flashing over the whole screen a few times.
        if (Phase != MissionEntryPhase.Descend)
        {
            ShowPlayerBody = false;
        }
        else if (!_bodyHidden)
        {
            if (Distance(_pose.Position, _landing.Position) <= BodyHiddenWithin)
            {
                _bodyHidden = true;
                ShowPlayerBody = false;
            }
            else
            {
                ShowPlayerBody = true;
            }
        }
    }

    /// <summary>How much of the wander applies right now.</summary>
    /// <remarks>
    /// In over the rise, full across the travel and the hold, and out over the descent — reaching exactly
    /// zero well before the landing, because a quarter of a degree of wander is right at two hundred
    /// metres and unbearable at eye height. Both ends are exact rather than nearly zero, which is what
    /// keeps the hand-over bit-identical.
    /// </remarks>
    private float ShakeWeight =>
        Phase switch
        {
            MissionEntryPhase.Reveal => 0f,
            MissionEntryPhase.Ascend => Easing.Evaluate(Ease.InOutQuad, Math.Clamp(_phaseElapsed / AscendSeconds, 0f, 1f)),
            MissionEntryPhase.Travel or MissionEntryPhase.Hold => 1f,
            MissionEntryPhase.Descend => 1f - Easing.Evaluate(
                Ease.InOutQuad,
                Math.Clamp(((_phaseElapsed / DescendSeconds) - 0.35f) / 0.5f, 0f, 1f)),
            _ => 0f,
        };

    private void AdvancePhase()
    {
        switch (Phase)
        {
            case MissionEntryPhase.Reveal when _phaseElapsed >= RevealSeconds:
                EnterPhase(MissionEntryPhase.Ascend);
                break;

            case MissionEntryPhase.Ascend when _phaseElapsed >= AscendSeconds:
                EnterPhase(MissionEntryPhase.Travel);
                break;

            case MissionEntryPhase.Travel when _phaseElapsed >= TravelSeconds:
                EnterPhase(MissionEntryPhase.Hold);
                break;

            case MissionEntryPhase.Hold:
                // The beat waits for the mission. While it waits the picture keeps moving -- the drift
                // below and the shake carry on -- so a wait is a longer shot rather than a frozen one.
                // Past the patience it lands anyway: an honest hitch is better than a camera that hangs.
                if (!_promoted)
                {
                    HeldWaiting += _phaseElapsed - HeldWaiting;
                    if (HeldWaiting < HoldPatienceSeconds)
                    {
                        break;
                    }
                }

                if (_phaseElapsed >= HoldSeconds)
                {
                    EnterPhase(MissionEntryPhase.Descend);
                }

                break;

            case MissionEntryPhase.Descend when _phaseElapsed >= DescendSeconds:
                EnterPhase(MissionEntryPhase.Chrome);
                break;

            case MissionEntryPhase.Chrome when _phaseElapsed >= ChromeSeconds:
                EnterPhase(MissionEntryPhase.Done);
                break;

            default:
                break;
        }
    }

    private void EnterPhase(MissionEntryPhase phase)
    {
        Phase = phase;
        _phaseElapsed = 0f;
    }

    private CameraPose Ascending(in CameraPose liveShowcase)
    {
        float t = Math.Clamp(_phaseElapsed / AscendSeconds, 0f, 1f);
        float e = Easing.Evaluate(Ease.InOutCubic, t);
        CameraPose climbed = CameraPose.Lerp(_start, _apexOverStart, e);

        // For the first moments the pose is blended against where the orbit actually is, so the shot
        // leaves at the orbit's own speed instead of from a standstill.
        float handover = Easing.Evaluate(Ease.InOutQuad, Math.Clamp(_phaseElapsed / OrbitBlendSeconds, 0f, 1f));
        return CameraPose.Lerp(liveShowcase, climbed, handover);
    }

    private CameraPose Travelling()
    {
        float t = Math.Clamp(_phaseElapsed / TravelSeconds, 0f, 1f);
        float e = Easing.Evaluate(Ease.InOutQuad, t);
        CameraPose flat = CameraPose.Lerp(_apexOverStart, _apexOverSpawn, e);

        // A shallow arch over the middle of the journey. Zero and smooth at both ends, so it costs the
        // seams nothing, and it stops a long lateral move reading as a flat slide.
        double dx = _apexOverSpawn.Position.X - _apexOverStart.Position.X;
        double dy = _apexOverSpawn.Position.Y - _apexOverStart.Position.Y;
        double arch = Math.Clamp(Math.Sqrt((dx * dx) + (dy * dy)) * 0.12, 0.0, 45.0);

        return flat with
        {
            Position = new Vector3d(
                flat.Position.X,
                flat.Position.Y,
                flat.Position.Z + (arch * Math.Sin(Math.PI * e))),
        };
    }

    private CameraPose Holding()
    {
        // Still moving, always. A held shot that is waiting on a load has to keep drifting or the wait
        // reads as the game having stopped rather than as the shot taking its time.
        double drift = _phaseElapsed * 1.5;
        return _apexOverSpawn with
        {
            Position = new Vector3d(
                _apexOverSpawn.Position.X,
                _apexOverSpawn.Position.Y,
                _apexOverSpawn.Position.Z + drift),
        };
    }

    private CameraPose Descending()
    {
        float t = Math.Clamp(_phaseElapsed / DescendSeconds, 0f, 1f);
        if (t >= 1f)
        {
            return _landing;
        }

        float u = Easing.Evaluate(Ease.InOutCubic, t);

        // The drop is a quadratic Bezier through a point behind and above the head rather than a straight
        // line, so the camera swings in behind the character instead of falling through the top of their
        // skull. The control point uses the LANDING's heading, which is the direction the player will be
        // facing when they take over.
        double fx = Math.Sin(_landing.Yaw);
        double fy = Math.Cos(_landing.Yaw);
        Vector3d approach = new(
            _landing.Position.X - (fx * ApproachBehind),
            _landing.Position.Y - (fy * ApproachBehind),
            _landing.Position.Z + ApproachAbove);

        double inv = 1.0 - u;
        double a = inv * inv;
        double b = 2.0 * inv * u;
        double c = (double)u * u;

        Vector3d position = new(
            (a * _apexOverSpawn.Position.X) + (b * approach.X) + (c * _landing.Position.X),
            (a * _apexOverSpawn.Position.Y) + (b * approach.Y) + (c * _landing.Position.Y),
            (a * _apexOverSpawn.Position.Z) + (b * approach.Z) + (c * _landing.Position.Z));

        // The angles finish early, so the last fifth of the drop is a straight fall into a heading that
        // is already correct. A camera still turning as it arrives reads as a crash rather than a landing.
        float settle = Easing.Evaluate(Ease.InOutCubic, Math.Clamp(t / 0.82f, 0f, 1f));
        return new CameraPose(
            position,
            CameraPose.LerpAngle(_travelYaw, _landing.Yaw, settle),
            _apexOverSpawn.Pitch + ((_landing.Pitch - _apexOverSpawn.Pitch) * settle));
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
