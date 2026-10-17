using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Rendering;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// A probe bake running off the frame, and the handover of its result to the render thread.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IrradianceProbeBaker.Bake"/> takes about three seconds in Release and half a minute in
/// Debug. Called inline during a level load that already owns a live window, that is not a stall: the
/// process stops answering, the GPU sees no work for the whole of it, and Windows' watchdog takes the
/// context away — loading mission 1 from the menu died with exit <c>0xCFFFFFFF</c>. So the trace runs
/// off the main thread and the frames keep going out while it does.
/// </para>
/// <para>
/// The <em>upload</em> cannot move with it. A GL texture belongs to the context it was made on and the
/// context belongs to the render thread, so the worker produces a plain array of coefficients and the
/// main thread collects it with <see cref="TryCollect"/> and hands it to the device. Nothing here
/// touches the graphics API.
/// </para>
/// <para>
/// The cast handed in must be safe to call while the game runs, because by the time the trace finishes
/// the game <em>is</em> running: doors open, and <see cref="ObjectCollisionIndex.TryCast"/> reads the
/// set that records which ones. <see cref="ObjectCollisionIndex.CasterWithDoorsShut"/> is the cast that
/// answers this, and it is the one the bake wants for its own reasons anyway.
/// </para>
/// </remarks>
public sealed class IrradianceProbeBake
{
    private const string LogCategory = "GI";

    private readonly ScheduledJob<IrradianceVolume?> _job;
    private readonly CancellationTokenSource _cancellation;
    private readonly Stream _stream;
    private readonly IOpenIgiLogger _log;
    private bool _collected;

    private IrradianceProbeBake(ScheduledJob<IrradianceVolume?> job, CancellationTokenSource cancellation, Stream stream, IOpenIgiLogger log)
    {
        _job = job;
        _cancellation = cancellation;
        _stream = stream;
        _log = log;
    }

    // The worker publishes each bounce pass's field here as it finishes; the render thread takes the
    // newest it has not seen. A version counter, not a queue: only the latest field matters, and a slow
    // frame should skip straight to it rather than upload every pass it missed.
    private sealed class Stream
    {
        private readonly object _gate = new();
        private IrradianceVolume? _latest;
        private int _version;
        private int _taken;

        public void Publish(IrradianceVolume volume)
        {
            lock (_gate)
            {
                _latest = volume;
                _version++;
            }
        }

        public bool TryTake(out IrradianceVolume? volume)
        {
            lock (_gate)
            {
                if (_version == _taken)
                {
                    volume = null;
                    return false;
                }

                _taken = _version;
                volume = _latest;
                return true;
            }
        }
    }

    /// <summary>Gets whether the trace has finished, however it finished.</summary>
    public bool IsComplete => _job.IsFinished;

    /// <summary>Asks whether the trace has finished, tracing a slice of it first where it runs inline.</summary>
    /// <returns><see langword="true"/> once it has finished, however it finished.</returns>
    /// <remarks>What the frame loop calls each frame; on a single-threaded host this is the bake's progress.</remarks>
    public bool Poll() => _job.Poll();

    /// <summary>Starts tracing a level's probe field: on a worker where the scheduler has one, a slice per poll where it does not.</summary>
    /// <param name="scene">The level, for its bounds.</param>
    /// <param name="cast">
    /// How to ask the world what a ray meets. Must be safe to call from another thread for as long as
    /// the bake runs — see the remarks on the type.
    /// </param>
    /// <param name="sunDirection">The direction light arrives from, pointing towards the sun.</param>
    /// <param name="spacing">The distance between probes, in metres.</param>
    /// <param name="groundAlbedo">What the level bounces, per channel.</param>
    /// <param name="work">Where the trace runs; see <see cref="ScheduledJob{T}"/>.</param>
    /// <param name="logger">Where to report the bake's size and duration.</param>
    /// <returns>A handle to poll with <see cref="TryCollect"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="cast"/> is null.</exception>
    public static IrradianceProbeBake Start(
        WorldScene scene,
        WorldRayCast cast,
        Vector3f sunDirection,
        float spacing,
        Vector3f groundAlbedo,
        IWorkScheduler work,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(cast);
        ArgumentNullException.ThrowIfNull(work);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        CancellationTokenSource cancellation = new();
        CancellationToken token = cancellation.Token;
        Stream stream = new();

        SteppedJob<IrradianceVolume?> job = new(done => Trace(scene, cast, sunDirection, spacing, groundAlbedo, log, stream, work, done, token));
        return new IrradianceProbeBake(new ScheduledJob<IrradianceVolume?>(job, work), cancellation, stream, log);
    }

    /// <summary>The trace, a slice at a time, and how long it took once it is done.</summary>
    private static IEnumerable<bool> Trace(
        WorldScene scene,
        WorldRayCast cast,
        Vector3f sunDirection,
        float spacing,
        Vector3f groundAlbedo,
        IOpenIgiLogger log,
        Stream stream,
        IWorkScheduler work,
        Action<IrradianceVolume?> done,
        CancellationToken token)
    {
        long startedAt = Stopwatch.GetTimestamp();
        IrradianceVolume? volume = null;

        foreach (bool slice in IrradianceProbeBaker.Bake(
            scene, cast, sunDirection, spacing, groundAlbedo, log, stream.Publish, work, baked => volume = baked, token))
        {
            yield return slice;
        }

        if (volume is not null)
        {
            double seconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
            log.Information(
                LogCategory,
                FormattableString.Invariant(
                    $"Probe bake took {seconds:0.00} s; the field lights the level from the next frame."));
        }

        done(volume);
    }

    /// <summary>Takes the newest bounce pass's field the render thread has not yet uploaded.</summary>
    /// <param name="volume">Receives the latest intermediate field, for the game to show the light building up.</param>
    /// <returns><see langword="true"/> when a newer field than the last taken is available.</returns>
    /// <remarks>
    /// Poll every frame while the bake runs. Unlike <see cref="TryCollect"/>, which fires once at the end,
    /// this returns each pass as it lands, so an interactive load can enter the level on flat ambient and
    /// watch the multi-bounce fill in rather than waiting for the finished field.
    /// </remarks>
    public bool TryCollectLatest(out IrradianceVolume? volume) => _stream.TryTake(out volume);

    /// <summary>Takes the finished volume, if it is finished.</summary>
    /// <param name="volume">
    /// Receives the baked volume, which is <see langword="null"/> when the bake found nothing to trace
    /// around, was cancelled, or failed.
    /// </param>
    /// <returns>
    /// <see langword="true"/> exactly once, on the first call after the trace completes. Callers upload
    /// on that call and can stop polling.
    /// </returns>
    /// <remarks>Call from the render thread: whatever comes back is about to become a GL texture.</remarks>
    public bool TryCollect(out IrradianceVolume? volume)
    {
        volume = null;

        if (_collected || !_job.Poll())
        {
            return false;
        }

        _collected = true;

        // A bake that throws must not take the game with it. The level keeps the flat ambient every
        // preset below high runs on anyway; a cancelled one -- quitting, or changing level -- is simply
        // never collected, and has nothing to report.
        if (_job.Failure is { } failure)
        {
            _log.Error(LogCategory, $"The probe bake failed and the level will use flat ambient: {failure.Message}", failure);
        }

        volume = _job.Result;
        return true;
    }

    /// <summary>Blocks until the trace finishes and takes the result.</summary>
    /// <returns>The baked volume, or <see langword="null"/> as <see cref="TryCollect"/> describes.</returns>
    /// <remarks>
    /// For a capture run, where determinism outranks responsiveness: a reference image has to contain
    /// the field or not contain it, never "whichever the worker had reached by frame 30". Interactive
    /// play must not call this — blocking the main thread here is the exact failure the type exists to
    /// avoid.
    /// </remarks>
    public IrradianceVolume? WaitForResult()
    {
        _collected = true;
        return _job.Wait();
    }

    /// <summary>Asks the trace to stop, for a level change or a shutdown.</summary>
    /// <remarks>
    /// Does not wait. The worker is a thread-pool thread and so cannot hold the process open; this only
    /// stops it burning every core on a field nobody will collect.
    /// </remarks>
    public void Cancel() => _cancellation.Cancel();
}
