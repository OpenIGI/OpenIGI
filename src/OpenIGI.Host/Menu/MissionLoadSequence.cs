using OpenIGI.UI.Animation;

namespace OpenIGI.Desktop;

/// <summary>Which part of the loading sequence is running.</summary>
public enum MissionLoadPhase
{
    /// <summary>Nothing is loading.</summary>
    Idle,

    /// <summary>The interface is leaving and the picture is being taken out of focus.</summary>
    Depart,

    /// <summary>The title card is up over the defocused picture while the level is built and lit.</summary>
    Hold,

    /// <summary>The card is leaving and the picture is fading off the live level.</summary>
    Reveal,
}

/// <summary>
/// The presentation of going into a mission where no cinematic covers it: the picture the player was
/// looking at freezes and falls out of focus as the interface leaves, a title card holds over it while
/// the level is built and lit, and then the card leaves and the picture dissolves into the live level.
/// </summary>
/// <remarks>
/// <para>
/// It owns no device and no renderer. A clock goes in, and every amount a frame needs -- how far out of
/// focus, how much of the interface is left, where the card sits -- comes out, already eased. The game
/// does two things on its say-so: captures the clean picture (before the interface is drawn over it) at
/// the start, and runs the blocking load once the departure has played out, so the player watches the
/// interface leave rather than a frozen menu. The hold then lasts exactly as long as the level needs.
/// </para>
/// <para>
/// The picture is the level itself -- the menu's backdrop of it, or the mission being restarted -- so
/// what the player looks at while they wait is where they are going. The card is set in the shell's own
/// faces and left-aligned, the way a title is: a caption over the place, not a dialog in front of it.
/// </para>
/// </remarks>
public sealed class MissionLoadSequence
{
    /// <summary>How long the interface takes to leave and the picture to fall out of focus, in seconds.</summary>
    public const float DepartSeconds = 0.6f;

    /// <summary>How long the card takes to leave and the picture to dissolve, in seconds.</summary>
    public const float RevealSeconds = 1.5f;

    /// <summary>How long the card takes to come in, in seconds.</summary>
    public const float CardInSeconds = 0.7f;

    /// <summary>How long the card takes to go, in seconds, once its beat at the end is over.</summary>
    public const float CardOutSeconds = 0.45f;

    /// <summary>How long the card stays, complete, at the start of the reveal before it goes, in seconds.</summary>
    /// <remarks>A bar that reaches its end and is gone the same instant reads as never having got there.</remarks>
    public const float CardOutDelaySeconds = 0.5f;

    /// <summary>How far the card slides as it comes in and goes, in reference pixels at 1080p.</summary>
    public const float CardSlidePixels = 26f;

    /// <summary>The fraction of the height each cinematic bar takes when fully in.</summary>
    public const float LetterboxFraction = 0.10f;

    /// <summary>How far the picture pushes in over the hold: a few per cent, over a long time.</summary>
    private const float PushInAmount = 0.05f;
    private const float PushInSeconds = 14f;

    /// <summary>How far out of focus the live level is held while its lighting settles.</summary>
    private const float LiveBlur = 0.6f;

    private float _elapsed;
    private float _phaseElapsed;
    private bool _captureWanted;
    private bool _loaded;
    private bool _live;

    /// <summary>Gets which part of the sequence is running.</summary>
    public MissionLoadPhase Phase { get; private set; } = MissionLoadPhase.Idle;

    /// <summary>Gets whether the sequence is on screen at all.</summary>
    public bool IsActive => Phase != MissionLoadPhase.Idle;

    /// <summary>Gets whether a held picture of the level is behind the card.</summary>
    public bool HasPicture { get; private set; }

    /// <summary>Gets whether the level has been built, so the hold is only waiting on its lighting.</summary>
    public bool Loaded => _loaded;

    /// <summary>
    /// Gets whether the picture behind the card is the live level from the shot's vantage rather than a
    /// still, so it is re-taken every frame and the caption is in the world rather than on the card.
    /// </summary>
    public bool LiveScene => _live;

    /// <summary>Gets whether this frame's picture should be taken, for a live scene that is being held out of focus.</summary>
    public bool WantsLivePicture => _live && Phase is MissionLoadPhase.Hold or MissionLoadPhase.Reveal;

    /// <summary>Gets whether the clean picture should be captured now, before the interface draws.</summary>
    public bool WantsCapture => Phase == MissionLoadPhase.Depart && _captureWanted;

    /// <summary>Gets whether the blocking load should run now: the departure has played and the picture is held.</summary>
    public bool WantsLoad =>
        Phase == MissionLoadPhase.Depart && !_captureWanted && !_loaded && _phaseElapsed >= DepartSeconds;

    /// <summary>Gets how much of the interface is still showing, one for all of it.</summary>
    public float ShellOpacity =>
        Phase == MissionLoadPhase.Idle ? 1f
        : Phase == MissionLoadPhase.Depart ? 1f - Easing.Evaluate(Ease.OutCubic, Math.Clamp(_phaseElapsed / (DepartSeconds * 0.7f), 0f, 1f))
        : 0f;

    /// <summary>Gets how far out of focus the picture is, one for fully.</summary>
    /// <remarks>
    /// A live level is held only part-way out of focus: the place stays legible under its caption while
    /// the lighting settles beneath the defocus, and the reveal is a focus pull rather than a dissolve.
    /// </remarks>
    public float Blur =>
        Phase switch
        {
            MissionLoadPhase.Depart => Easing.Evaluate(Ease.OutCubic, Math.Clamp(_phaseElapsed / DepartSeconds, 0f, 1f)),
            MissionLoadPhase.Hold => _live ? LiveBlur : 1f,
            MissionLoadPhase.Reveal => (_live ? LiveBlur : 1f) * (1f - Easing.Evaluate(Ease.InOutCubic, Math.Clamp(_phaseElapsed / RevealSeconds, 0f, 1f))),
            _ => 0f,
        };

    /// <summary>Gets how far the picture is graded towards monochrome; it follows the focus, gently over a live level.</summary>
    public float Desaturation => _live && Phase != MissionLoadPhase.Depart ? Blur * 0.6f : Blur * 0.85f;

    /// <summary>Gets how strongly the edges darken; it follows the focus.</summary>
    public float Vignette => Blur * 0.9f;

    /// <summary>Gets the slow push in on the picture, one for none.</summary>
    public float Zoom => 1f + (PushInAmount * Easing.Evaluate(Ease.OutQuad, Math.Clamp(_elapsed / PushInSeconds, 0f, 1f)));

    /// <summary>Gets how much of the whole backdrop shows over the live frame.</summary>
    public float Opacity =>
        Phase switch
        {
            MissionLoadPhase.Depart or MissionLoadPhase.Hold => 1f,
            MissionLoadPhase.Reveal => 1f - Easing.Evaluate(Ease.InOutCubic, Math.Clamp(_phaseElapsed / RevealSeconds, 0f, 1f)),
            _ => 0f,
        };

    /// <summary>Gets how far the cinematic bars have come in, one for fully.</summary>
    public float Letterbox =>
        Phase switch
        {
            MissionLoadPhase.Depart => Easing.Evaluate(Ease.OutCubic, Math.Clamp(_phaseElapsed / DepartSeconds, 0f, 1f)),
            MissionLoadPhase.Hold => 1f,
            MissionLoadPhase.Reveal => 1f - Easing.Evaluate(Ease.InOutCubic, Math.Clamp(_phaseElapsed / (RevealSeconds * 0.8f), 0f, 1f)),
            _ => 0f,
        };

    /// <summary>Gets how much of the card shows, one for all of it.</summary>
    /// <remarks>
    /// In a beat after the departure starts, so the interface has begun to leave before the card arrives
    /// -- two things fading at once read as a glitch -- and out quickly at the reveal, ahead of the
    /// picture, so the level is never seen through a caption.
    /// </remarks>
    public float Card =>
        Phase switch
        {
            MissionLoadPhase.Depart or MissionLoadPhase.Hold =>
                Easing.Evaluate(Ease.OutCubic, Math.Clamp((_elapsed - (DepartSeconds * 0.45f)) / CardInSeconds, 0f, 1f)),
            MissionLoadPhase.Reveal => 1f - Easing.Evaluate(Ease.InCubic, Math.Clamp((_phaseElapsed - CardOutDelaySeconds) / CardOutSeconds, 0f, 1f)),
            _ => 0f,
        };

    /// <summary>Gets how far the card is displaced from its resting place, in reference pixels: up as it goes, down as it comes.</summary>
    public float CardSlide =>
        Phase == MissionLoadPhase.Reveal
            ? -(1f - Card) * CardSlidePixels
            : (1f - Card) * CardSlidePixels;

    /// <summary>Gets how far the diagonal light has drifted across, in screen widths.</summary>
    public float Sweep => -0.4f + ((_elapsed * 0.06f) % 2.2f);

    /// <summary>Gets how long the sequence has been running, in seconds.</summary>
    public float Elapsed => _elapsed;

    /// <summary>Begins the sequence.</summary>
    /// <param name="capturePicture">
    /// Whether there is a picture on screen to hold. False for a level started before anything was drawn,
    /// which gets the procedural surface instead.
    /// </param>
    public void Begin(bool capturePicture)
    {
        Phase = MissionLoadPhase.Depart;
        _elapsed = 0f;
        _phaseElapsed = 0f;
        _captureWanted = capturePicture;
        HasPicture = false;
        _loaded = false;
        _live = false;
    }

    /// <summary>Tells the sequence the clean picture has been held.</summary>
    public void NotifyCaptured()
    {
        _captureWanted = false;
        HasPicture = true;
    }

    /// <summary>Tells the sequence the level has been built.</summary>
    public void NotifyLoaded() => _loaded = true;

    /// <summary>Tells the sequence the shot is up over the level, so the picture from here is the live one.</summary>
    public void NotifyLiveScene()
    {
        _live = true;
        HasPicture = true;
    }

    /// <summary>Stops the sequence at once, for a load that was abandoned.</summary>
    public void Cancel()
    {
        Phase = MissionLoadPhase.Idle;
        _captureWanted = false;
        _loaded = false;
        _live = false;
    }

    /// <summary>Advances the sequence by a real-time step.</summary>
    /// <param name="elapsedSeconds">Real seconds since the last call.</param>
    /// <param name="ready">Whether the level is built, lit and settled, so the reveal may begin.</param>
    public void Update(float elapsedSeconds, bool ready)
    {
        if (Phase == MissionLoadPhase.Idle)
        {
            return;
        }

        float step = Math.Clamp(elapsedSeconds, 0f, 0.1f);
        _elapsed += step;
        _phaseElapsed += step;

        switch (Phase)
        {
            case MissionLoadPhase.Depart when _loaded && _phaseElapsed >= DepartSeconds:
                Enter(MissionLoadPhase.Hold);
                break;

            case MissionLoadPhase.Hold when ready && _loaded:
                Enter(MissionLoadPhase.Reveal);
                break;

            case MissionLoadPhase.Reveal when _phaseElapsed >= RevealSeconds:
                Enter(MissionLoadPhase.Idle);
                break;

            default:
                break;
        }
    }

    private void Enter(MissionLoadPhase phase)
    {
        Phase = phase;
        _phaseElapsed = 0f;
    }
}
