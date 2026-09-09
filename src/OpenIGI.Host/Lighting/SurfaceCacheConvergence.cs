using System.Diagnostics;

using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Desktop;

/// <summary>
/// Solves a surface cache forward to its multi-bounce equilibrium a few slices at a time, so a level
/// converges behind a loading screen or a held shot instead of in one multi-second stall.
/// </summary>
/// <remarks>
/// <para>
/// The convergence itself is what it always was: one shadowed relight of the whole cache, a seed of the
/// final atlas from it, and <see cref="Passes"/> bounces folded in. Run back to back that measured two
/// and a half seconds on the render thread for a large level -- a freeze, and one that landed mid-play
/// whenever the distance field arrived after the level did. Divided into <see cref="Slices"/> and
/// stepped from the frame loop, the same work is spread over presented frames, so the window keeps
/// answering, a progress bar can move, and a shot that is holding for it keeps drifting.
/// </para>
/// <para>
/// How many slices a frame takes is decided from how long the last frame took, because the only thing
/// this thread can measure is the wall clock and the only reason a frame here is slow is that the queue
/// is full of this. A fast frame doubles the step, a slow one halves it, so a small level finishes in a
/// handful of frames and a large one settles at whatever rate keeps the picture moving.
/// </para>
/// </remarks>
internal sealed class SurfaceCacheConvergence
{
    /// <summary>How many slices the cache is divided into.</summary>
    /// <remarks>
    /// Fine enough that one slice of the largest level is a few tens of milliseconds, coarse enough that
    /// a small level is not paying a hundred dispatches' worth of fixed cost. Sixteen puts the biggest
    /// measured cache at roughly 150 ms per slice per full pass.
    /// </remarks>
    public const int Slices = 16;

    // Above this a frame is visibly stalling and the step comes down; below the lower bound the queue has
    // headroom and the step goes up. Between them the step holds.
    private const double SlowFrameMilliseconds = 30.0;
    private const double FastFrameMilliseconds = 12.0;
    private const int MaximumStepsPerFrame = 16;

    private readonly SurfaceCacheLighting _cards;
    private readonly GlobalDistanceField _field;
    private readonly Vector3f _sunDirection;
    private readonly Vector3f _sunColour;
    private readonly Vector3f _skyColour;
    private readonly float _boost;
    private readonly int _total;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private int _step;
    private int _stepsPerFrame = 2;
    private long _lastStepAt = -1;

    /// <summary>Initializes a new instance of the <see cref="SurfaceCacheConvergence"/> class.</summary>
    /// <param name="cards">The lit cache to converge.</param>
    /// <param name="field">The distance field shadows and bounces trace against.</param>
    /// <param name="sunDirection">The direction towards the sun, or zero for none.</param>
    /// <param name="sunColour">The sun's colour, through every scale the screen applies.</param>
    /// <param name="skyColour">The sky's fill colour.</param>
    /// <param name="boost">The albedo boost the bounce applies.</param>
    /// <param name="passes">How many bounces to fold in.</param>
    public SurfaceCacheConvergence(
        SurfaceCacheLighting cards,
        GlobalDistanceField field,
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        float boost,
        int passes)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(field);
        _cards = cards;
        _field = field;
        _sunDirection = sunDirection;
        _sunColour = sunColour;
        _skyColour = skyColour;
        _boost = boost;
        Passes = Math.Max(0, passes);

        // One relight and one seed per slice, then the bounces.
        _total = Slices * (2 + Passes);
    }

    /// <summary>Gets how many bounces are folded in.</summary>
    public int Passes { get; }

    /// <summary>Gets whether every step has run.</summary>
    public bool IsComplete => _step >= _total;

    /// <summary>Gets how far along this is, from zero to one.</summary>
    public float Progress => _total <= 0 ? 1f : Math.Clamp(_step / (float)_total, 0f, 1f);

    /// <summary>Gets how long the whole convergence has been running, in milliseconds.</summary>
    public long ElapsedMilliseconds => _clock.ElapsedMilliseconds;

    /// <summary>Runs every remaining step now, for a capture that has to be deterministic.</summary>
    public void RunAll()
    {
        while (!IsComplete)
        {
            Advance();
        }
    }

    /// <summary>Runs this frame's share of the steps. Call once per frame from the render thread.</summary>
    public void Step()
    {
        if (IsComplete)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (_lastStepAt >= 0)
        {
            double frameMilliseconds = (now - _lastStepAt) * 1000.0 / Stopwatch.Frequency;
            if (frameMilliseconds > SlowFrameMilliseconds)
            {
                _stepsPerFrame = Math.Max(1, _stepsPerFrame / 2);
            }
            else if (frameMilliseconds < FastFrameMilliseconds)
            {
                _stepsPerFrame = Math.Min(MaximumStepsPerFrame, _stepsPerFrame * 2);
            }
        }

        _lastStepAt = now;

        for (int i = 0; i < _stepsPerFrame && !IsComplete; i++)
        {
            Advance();
        }
    }

    private void Advance()
    {
        int stage = _step / Slices;
        int slice = _step % Slices;

        if (stage == 0)
        {
            _cards.ConvergeLightSlice(_sunDirection, _sunColour, _skyColour, _field, slice, Slices);
        }
        else if (stage == 1)
        {
            _cards.ConvergeSeedSlice(_field, _boost, slice, Slices);
        }
        else
        {
            _cards.ConvergeBounceSlice(_field, _boost, stage - 2, slice, Slices);
        }

        _step++;
    }
}
