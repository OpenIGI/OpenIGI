using System.Diagnostics;

using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Lighting;

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
/// <b>Paced by the GPU, not by the frame.</b> The slices go out in batches, and a batch is not followed by
/// another until the GPU has finished it: the work is marked as soon as it is recorded, and the marker
/// polled from the frames after. How long the batch took to come back sizes the next one against
/// <see cref="BatchBudgetMilliseconds"/>, so a fast GPU is soon taking the maximum and a slow one settles
/// at a slice at a time.
/// </para>
/// <para>
/// It used to size the step from how long the host's last frame took, doubling it under twelve
/// milliseconds. But the host records a frame and moves on; until the frames in flight fill up, nothing it
/// times includes the work it has queued. So every convergence opened by doubling blind -- two slices, four,
/// eight, sixteen -- and on a MacBook Air's integrated GPU, a frame's command buffer carrying sixteen slices
/// of shadowed, distance-field-traced lighting ran for seconds, which macOS answers by killing it and the
/// device with it.
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

    /// <summary>How long a batch may take to come back, recording to completion.</summary>
    /// <remarks>
    /// Measured from when the batch is recorded to the first frame that sees it finished, so it includes a
    /// frame or so of latency besides the batch's own GPU time. That makes it a conservative budget -- a
    /// batch is judged slower than it is -- and one a batch that has the GPU to itself still fits a frame
    /// within.
    /// </remarks>
    public const double BatchBudgetMilliseconds = 50.0;

    /// <summary>The most slices one batch takes, however fast the GPU.</summary>
    public const int MaximumStepsPerBatch = 16;

    private readonly IGraphicsDevice _device;
    private readonly SurfaceCacheLighting _cards;
    private readonly GlobalDistanceField _field;
    private readonly Vector3f _sunDirection;
    private readonly Vector3f _sunColour;
    private readonly Vector3f _skyColour;
    private readonly float _boost;
    private readonly int _total;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private int _step;

    // One slice to start with: nothing is known yet about what this GPU makes of one.
    private int _stepsPerBatch = 1;

    // The batch in flight: how many slices it carried, when they were recorded, and the marker that says
    // when the GPU has finished them.
    private int _batchSteps;
    private long _batchRecordedAt;
    private IGpuWorkMarker? _batchMarker;

    /// <summary>Initializes a new instance of the <see cref="SurfaceCacheConvergence"/> class.</summary>
    /// <param name="device">The device the work goes to, which says when the GPU has finished it.</param>
    /// <param name="cards">The lit cache to converge.</param>
    /// <param name="field">The distance field shadows and bounces trace against.</param>
    /// <param name="sunDirection">The direction towards the sun, or zero for none.</param>
    /// <param name="sunColour">The sun's colour, through every scale the screen applies.</param>
    /// <param name="skyColour">The sky's fill colour.</param>
    /// <param name="boost">The albedo boost the bounce applies.</param>
    /// <param name="passes">How many bounces to fold in.</param>
    public SurfaceCacheConvergence(
        IGraphicsDevice device,
        SurfaceCacheLighting cards,
        GlobalDistanceField field,
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        float boost,
        int passes)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(field);
        _device = device;
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

    /// <summary>Gets how many slices the next batch will carry.</summary>
    public int StepsPerBatch => _stepsPerBatch;

    /// <summary>Runs every remaining step now, for a capture that has to be deterministic.</summary>
    public void RunAll()
    {
        while (!IsComplete)
        {
            Advance();
        }
    }

    /// <summary>Runs this frame's share of the steps. Call once per frame from the render thread.</summary>
    /// <remarks>
    /// Records a batch only when the last one has finished on the GPU, so a frame either carries a batch or
    /// carries none; a GPU that cannot keep up gets frames with no convergence in them rather than a queue
    /// that grows.
    /// </remarks>
    public void Step()
    {
        if (IsComplete)
        {
            return;
        }

        if (_batchSteps > 0 && !BatchFinished())
        {
            return;
        }

        int recorded = 0;
        while (recorded < _stepsPerBatch && !IsComplete)
        {
            Advance();
            recorded++;
        }

        _batchSteps = recorded;
        _batchRecordedAt = Stopwatch.GetTimestamp();
        _batchMarker = _device.MarkRecordedWork();
    }

    /// <summary>Whether the batch in flight has finished, sizing the next one from how long it took if so.</summary>
    private bool BatchFinished()
    {
        if (_batchMarker is { IsComplete: false })
        {
            return false;
        }

        double milliseconds = (Stopwatch.GetTimestamp() - _batchRecordedAt) * 1000.0 / Stopwatch.Frequency;
        _stepsPerBatch = NextBatchSize(_batchSteps, milliseconds);
        _batchSteps = 0;
        _batchMarker = null;
        return true;
    }

    /// <summary>How many slices the next batch takes, given how the last one went.</summary>
    /// <param name="lastSteps">How many slices the last batch carried.</param>
    /// <param name="lastMilliseconds">How long it took to come back, recording to completion.</param>
    /// <returns>As many as the budget affords at the last batch's cost per slice, at least one.</returns>
    /// <remarks>
    /// Growth is held to doubling. The cost per slice is measured from the whole batch, and a small batch
    /// is mostly the latency every batch pays, so it reads as cheaper per slice than a large one will be;
    /// taking the budget's word for it outright would jump from one slice to sixteen on the strength of a
    /// frame's worth of waiting. Shrinking is not held back: a batch that ran long is cut straight to what
    /// the budget affords, because the next one runs at that cost too.
    /// </remarks>
    public static int NextBatchSize(int lastSteps, double lastMilliseconds)
    {
        int steps = Math.Max(1, lastSteps);
        double perStep = Math.Max(lastMilliseconds, 0.001) / steps;
        int affordable = (int)Math.Min(BatchBudgetMilliseconds / perStep, MaximumStepsPerBatch);
        return Math.Clamp(affordable, 1, Math.Min(MaximumStepsPerBatch, steps * 2));
    }

    private void Advance()
    {
        int stage = _step / Slices;
        int slice = _step % Slices;
        _cards.Converging = true;

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

        _cards.Converging = false;
        _step++;
    }
}
