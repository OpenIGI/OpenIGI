namespace OpenIGI.Desktop;

/// <summary>
/// Decides when a freshly loaded level may be shown: not after a fixed number of frames, but once the
/// lighting it will be shown with exists, has converged, and has had a few frames to settle.
/// </summary>
/// <remarks>
/// <para>
/// A load ending is not the same as a level being ready. The lighting model this renderer plays under is
/// built rather than switched on -- a distance field that may still be baking on a worker, a surface
/// cache that converges over dispatches -- and everything temporal on top of it needs frames. Counting
/// frames alone, which is what the warm-up used to do, revealed a level whose field had not landed and
/// let the player watch it cross over a few seconds later.
/// </para>
/// <para>
/// So this is a gate with three stages, each fed by the code that knows about it: the blocking load
/// reports its stages, the lighting reports whether its structures are ready and how far the
/// convergence has come, and the frame loop reports each drawn frame. The settle count starts only when
/// the lighting is ready. Past a patience the gate opens anyway, because a hitch is better than a hang.
/// </para>
/// <para>
/// No device, no clock of its own: it is fed elapsed seconds, so it can be tested against every route a
/// level takes in.
/// </para>
/// </remarks>
public sealed class MissionWarmUpGate
{
    /// <summary>How long the gate waits for the lighting before showing the level regardless, in seconds.</summary>
    /// <remarks>
    /// Long enough for the slowest measured field bake plus its convergence with room to spare (mission
    /// 8 bakes in seven to twelve seconds on one core, depending on what the render thread is doing
    /// beside it, and converges in a few more), short enough that a bake that has genuinely hung does
    /// not hold a loading screen forever.
    /// </remarks>
    public const float PatienceSeconds = 30f;

    /// <summary>
    /// How long a field bake is assumed to take, for the progress shown while one runs. The bake reports
    /// no progress of its own, so the bar eases towards -- and never reaches -- the end of its share.
    /// </summary>
    public const float EstimatedBakeSeconds = 10f;

    // The share of the bar each stage owns.
    private const float LoadShare = 0.35f;
    private const float FieldShare = 0.25f;
    private const float ConvergeShare = 0.25f;
    private const float SettleShare = 0.15f;

    private readonly int _settleFrames;
    private int _settleRemaining;
    private float _loadProgress;
    private float _fieldProgress;
    private float _convergeProgress;
    private float _waitedSeconds;
    private bool _lightingReady;

    /// <summary>Initializes a new instance of the <see cref="MissionWarmUpGate"/> class.</summary>
    /// <param name="settleFrames">How many frames to draw, unseen, once the lighting is ready.</param>
    public MissionWarmUpGate(int settleFrames)
    {
        _settleFrames = Math.Max(0, settleFrames);
    }

    /// <summary>Gets whether a level is being held.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets whether the gate gave up waiting rather than opening because the level was ready.</summary>
    public bool GaveUp { get; private set; }

    /// <summary>Gets whether the level must still be withheld.</summary>
    public bool IsHolding => IsActive && !(_lightingReady && _settleRemaining <= 0);

    /// <summary>Gets how far along the whole warm-up is, from zero to one.</summary>
    public float Progress
    {
        get
        {
            if (!IsActive)
            {
                return 1f;
            }

            float settle = _settleFrames == 0
                ? (_lightingReady ? 1f : 0f)
                : 1f - (_settleRemaining / (float)_settleFrames);

            return Math.Clamp(
                (LoadShare * _loadProgress)
                + (FieldShare * _fieldProgress)
                + (ConvergeShare * _convergeProgress)
                + (SettleShare * settle),
                0f,
                1f);
        }
    }

    /// <summary>Gets a short description of what the gate is waiting on, for a caption.</summary>
    public string Stage
    {
        get
        {
            if (!IsActive)
            {
                return string.Empty;
            }

            if (_loadProgress < 1f)
            {
                return "Loading the level";
            }

            if (_fieldProgress < 1f)
            {
                return "Tracing the level's distance field";
            }

            if (_convergeProgress < 1f)
            {
                return "Lighting the level";
            }

            return "Settling";
        }
    }

    /// <summary>Starts holding a level that is about to be loaded.</summary>
    public void Begin()
    {
        IsActive = true;
        GaveUp = false;
        _settleRemaining = _settleFrames;
        _loadProgress = 0f;
        _fieldProgress = 0f;
        _convergeProgress = 0f;
        _waitedSeconds = 0f;
        _lightingReady = false;
    }

    /// <summary>Stops holding, for a level that is abandoned before it was ever shown.</summary>
    public void Cancel()
    {
        IsActive = false;
        GaveUp = false;
    }

    /// <summary>Reports how far the blocking load has come, from zero to one.</summary>
    /// <param name="progress">The load's own progress; never moves the bar backwards.</param>
    public void ReportLoad(float progress) => _loadProgress = Math.Max(_loadProgress, Math.Clamp(progress, 0f, 1f));

    /// <summary>
    /// Advances the gate by one drawn frame.
    /// </summary>
    /// <param name="lightingReady">Whether everything the frame lights from exists and has converged.</param>
    /// <param name="fieldPending">Whether a distance field is still being baked.</param>
    /// <param name="convergeProgress">How far the surface-cache convergence has come, or one when there is none.</param>
    /// <param name="elapsedSeconds">Real seconds since the last frame.</param>
    public void Update(bool lightingReady, bool fieldPending, float convergeProgress, float elapsedSeconds)
    {
        if (!IsActive)
        {
            return;
        }

        // The load is over by the time frames are being drawn, whatever it reported.
        _loadProgress = 1f;
        _waitedSeconds += MathF.Max(0f, elapsedSeconds);

        _fieldProgress = fieldPending
            ? Math.Max(_fieldProgress, MathF.Min(0.95f, _waitedSeconds / EstimatedBakeSeconds))
            : 1f;
        _convergeProgress = lightingReady ? 1f : Math.Max(_convergeProgress, Math.Clamp(convergeProgress, 0f, 1f));

        if (!_lightingReady && !lightingReady && _waitedSeconds >= PatienceSeconds)
        {
            // An honest cross-over later is better than a loading screen that never ends.
            lightingReady = true;
            GaveUp = true;
            _fieldProgress = 1f;
            _convergeProgress = 1f;
        }

        if (lightingReady)
        {
            _lightingReady = true;
        }

        if (_lightingReady && _settleRemaining > 0)
        {
            _settleRemaining--;
        }

        if (!IsHolding)
        {
            IsActive = false;
        }
    }
}
