using OpenIGI.UI.Animation;

namespace OpenIGI.UI.Kit;

/// <summary>
/// The design's title change: the old title wipes out upwards, the new one wipes in from below, resolving
/// out of a blur while its letterspacing tightens, with the caption and description following it in and a
/// light sweep crossing the screen.
/// </summary>
/// <remarks>
/// <para>
/// Three phases, exactly as the mock-up times them. <b>Out</b> (230 ms): the title fades, rises 40 units,
/// blurs and is wiped away from the left; the caption and description slide 16 units left and fade; the
/// background scales up to 108%. <b>Pre</b> (one frame): the new title is put in place below, wiped shut
/// from the right, tracked out to 0.12 em. <b>In</b> (900 ms): every property eases home on
/// <see cref="Ease.Emphasized"/> with its own duration, the caption 120 ms behind the title and the
/// description 260 ms; the background settles from 110% over 1.2 s; the sweep crosses in 800 ms.
/// </para>
/// <para>
/// It holds values, not drawing: a campaign page, a loading card or the launcher's hero reads
/// <see cref="TitleOpacity"/>, <see cref="TitleOffset"/> and the rest and draws with them, so one timing
/// serves all three.
/// </para>
/// </remarks>
public sealed class TitleReveal
{
    private const float OutSeconds = 0.23f;
    private const float InSeconds = 1.2f;

    private Phase _phase = Phase.Idle;
    private float _time;
    private int _direction = 1;
    private Action? _swap;

    private enum Phase
    {
        Idle,
        Out,
        In,
    }

    /// <summary>Gets a value indicating whether a change is under way.</summary>
    public bool IsRunning => _phase != Phase.Idle;

    /// <summary>Gets the title's opacity.</summary>
    public float TitleOpacity { get; private set; } = 1f;

    /// <summary>Gets the title's vertical offset, in design units.</summary>
    public float TitleOffset { get; private set; }

    /// <summary>Gets the title's blur, in design units.</summary>
    public float TitleBlur { get; private set; }

    /// <summary>Gets how much of the title is wiped away from the left, 0 to 1.</summary>
    public float ClipLeft { get; private set; }

    /// <summary>Gets how much of the title is wiped away from the right, 0 to 1.</summary>
    public float ClipRight { get; private set; }

    /// <summary>Gets the title's letterspacing, in ems.</summary>
    public float LetterSpacing { get; private set; } = 0.01f;

    /// <summary>Gets the caption line's opacity.</summary>
    public float MetaOpacity { get; private set; } = 1f;

    /// <summary>Gets the caption line's horizontal offset, in design units.</summary>
    public float MetaOffset { get; private set; }

    /// <summary>Gets the description's opacity.</summary>
    public float DescriptionOpacity { get; private set; } = 1f;

    /// <summary>Gets the description's horizontal offset, in design units.</summary>
    public float DescriptionOffset { get; private set; }

    /// <summary>Gets the background's scale about its centre.</summary>
    public float BackgroundScale { get; private set; } = 1f;

    /// <summary>Gets how far the light sweep has crossed, 0 to 1.</summary>
    public float SweepProgress { get; private set; }

    /// <summary>Gets the light sweep's opacity.</summary>
    public float SweepOpacity { get; private set; }

    /// <summary>Starts a change: the current title leaves, <paramref name="swap"/> runs, and the new one arrives.</summary>
    /// <param name="direction">+1 when moving down a list (the title leaves upwards), -1 when moving up.</param>
    /// <param name="swap">What to do between the two titles — usually, change which one is shown.</param>
    public void Start(int direction, Action? swap)
    {
        _direction = direction >= 0 ? 1 : -1;

        // A change asked for mid-change goes straight to the swap: the old title has already gone.
        if (_phase == Phase.Out)
        {
            _swap = swap;
            return;
        }

        _swap = swap;
        _phase = Phase.Out;
        _time = 0f;
        Apply();
    }

    /// <summary>Plays the arrival alone, with nothing leaving — the first title a page shows.</summary>
    public void Enter()
    {
        _swap = null;
        _phase = Phase.In;
        _time = 0f;
        Apply();
    }

    /// <summary>Jumps to rest, with the current title fully shown.</summary>
    public void Snap()
    {
        _phase = Phase.Idle;
        _time = 0f;
        Apply();
    }

    /// <summary>Advances the change.</summary>
    /// <param name="elapsedSeconds">The real time since the last update.</param>
    public void Update(float elapsedSeconds)
    {
        if (_phase == Phase.Idle)
        {
            return;
        }

        _time += MathF.Max(0f, elapsedSeconds);
        if (_phase == Phase.Out && _time >= OutSeconds)
        {
            _swap?.Invoke();
            _swap = null;
            _phase = Phase.In;
            _time = 0f;
        }
        else if (_phase == Phase.In && _time >= InSeconds)
        {
            _phase = Phase.Idle;
            _time = 0f;
        }

        Apply();
    }

    private void Apply()
    {
        switch (_phase)
        {
            case Phase.Out:
            {
                float title = Easing.Evaluate(Ease.EmphasizedAccelerate, _time / 0.22f);
                float meta = Easing.Evaluate(Ease.InQuad, _time / 0.18f);
                float background = Easing.Evaluate(Ease.InQuad, _time / OutSeconds);
                TitleOpacity = 1f - title;
                TitleOffset = -_direction * 40f * title;
                TitleBlur = 10f * title;
                ClipLeft = title;
                ClipRight = 0f;
                LetterSpacing = 0.01f;
                MetaOpacity = DescriptionOpacity = 1f - meta;
                MetaOffset = DescriptionOffset = -16f * meta;
                BackgroundScale = 1f + (0.08f * background);
                SweepOpacity = 0f;
                SweepProgress = 0f;
                break;
            }

            case Phase.In:
            {
                float t = _time;
                float opacity = Out(t, 0f, 0.5f);
                float travel = Out(t, 0f, 0.7f);
                float clear = Out(t, 0f, 0.5f);
                float wipe = Out(t, 0f, 0.65f);
                float tracking = Out(t, 0f, 0.9f);
                float meta = Out(t, 0.12f, 0.5f);
                float description = Out(t, 0.26f, 0.5f);
                float background = Out(t, 0f, 1.2f);

                TitleOpacity = opacity;
                TitleOffset = _direction * 60f * (1f - travel);
                TitleBlur = 10f * (1f - clear);
                ClipLeft = 0f;
                ClipRight = 1f - wipe;
                LetterSpacing = 0.12f + ((0.01f - 0.12f) * tracking);
                MetaOpacity = meta;
                MetaOffset = -16f * (1f - meta);
                DescriptionOpacity = description;
                DescriptionOffset = -16f * (1f - description);
                BackgroundScale = 1.1f - (0.1f * background);
                SweepProgress = Easing.Evaluate(Ease.Sweep, t / 0.8f);
                SweepOpacity = t < 0.6f ? 1f : 1f - Math.Clamp((t - 0.6f) / 0.3f, 0f, 1f);
                break;
            }

            default:
                TitleOpacity = MetaOpacity = DescriptionOpacity = 1f;
                TitleOffset = TitleBlur = ClipLeft = ClipRight = MetaOffset = DescriptionOffset = 0f;
                LetterSpacing = 0.01f;
                BackgroundScale = 1f;
                SweepOpacity = SweepProgress = 0f;
                break;
        }

        static float Out(float time, float delay, float duration) =>
            Easing.Evaluate(Ease.Emphasized, (time - delay) / duration);
    }
}
