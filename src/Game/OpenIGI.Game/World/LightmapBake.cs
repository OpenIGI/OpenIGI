using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;

namespace OpenIGI.Game.World;

/// <summary>
/// A path-traced object-lightmap bake running on a worker, and the hand-off of its finished pages to the
/// render thread.
/// </summary>
/// <remarks>
/// <para>
/// The bake -- direct sun, sky and several bounces of indirect over every lightmapped surface -- takes
/// seconds. Run inline during a level load that already owns a live window it is not a stall but a hang:
/// the process stops answering and the driver takes the context away, exactly as the probe bake found
/// (<see cref="IrradianceProbeBake"/>). So the tracing runs off the main thread and the frames keep going.
/// </para>
/// <para>
/// The texel maps and the triangle BVH are built by the caller on the main thread, because resolving each
/// surface's albedo reads the resource registry the render thread also uses. Everything the worker does --
/// tracing against the collision cast and the triangle BVH, then compositing -- reads only immutable data
/// and the shut-doors cast, both safe to touch while the game runs. The <em>upload</em> stays on the
/// render thread: <see cref="TryCollect"/> hands back plain pixel arrays for the device to turn into
/// textures.
/// </para>
/// </remarks>
public sealed class LightmapBake
{
    private const string LogCategory = "Lightmap";

    private readonly ScheduledJob<BakedLightmaps?> _job;
    private readonly CancellationTokenSource _cancellation;
    private readonly IIgiLogger _log;
    private bool _collected;

    private LightmapBake(ScheduledJob<BakedLightmaps?> job, CancellationTokenSource cancellation, IIgiLogger log)
    {
        _job = job;
        _cancellation = cancellation;
        _log = log;
    }

    /// <summary>Gets whether the bake has finished, however it finished.</summary>
    public bool IsComplete => _job.IsFinished;

    /// <summary>Starts baking a level's object lightmaps: on a worker where the scheduler has one, a slice per poll where it does not.</summary>
    /// <param name="scene">The scene, for each covered placement's lightmap entry.</param>
    /// <param name="targets">The texel maps from <see cref="LightmapBaker.EnumerateTargets"/>, built by the caller.</param>
    /// <param name="triangles">The world-space lightmapped triangles from <see cref="LightmapBaker.BuildTriangles"/>.</param>
    /// <param name="bvh">The BVH over <paramref name="triangles"/>.</param>
    /// <param name="cast">How to ask the world what a ray meets; must be safe to call from another thread.</param>
    /// <param name="sunDirection">The direction light arrives from, pointing towards the sun.</param>
    /// <param name="sunColour">The sun's colour.</param>
    /// <param name="skyColour">The sky's radiance a ray that escapes returns.</param>
    /// <param name="work">Where the bake runs; see <see cref="ScheduledJob{T}"/>.</param>
    /// <param name="bounces">How many indirect passes to gather.</param>
    /// <param name="hemisphereRays">Rays per texel per pass.</param>
    /// <param name="logger">Where to report the bake's duration.</param>
    /// <returns>A handle to poll with <see cref="TryCollect"/>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static LightmapBake Start(
        WorldScene scene,
        IReadOnlyList<LightmapBaker.LightmapTarget> targets,
        LightmapBaker.LightmapTriangle[] triangles,
        LightmapBaker.LightmapBvh bvh,
        WorldRayCast cast,
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        IWorkScheduler work,
        int bounces = 4,
        int hemisphereRays = 64,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(bvh);
        ArgumentNullException.ThrowIfNull(cast);
        ArgumentNullException.ThrowIfNull(work);

        IIgiLogger log = logger ?? NullLogger.Instance;
        CancellationTokenSource cancellation = new();
        CancellationToken token = cancellation.Token;

        SteppedJob<BakedLightmaps?> job = new(done => Bake(
            scene, targets, triangles, bvh, cast, sunDirection, sunColour, skyColour, bounces, hemisphereRays, work, log, done, token));

        return new LightmapBake(new ScheduledJob<BakedLightmaps?>(job, work), cancellation, log);
    }

    /// <summary>The bake's phases, a slice at a time.</summary>
    private static IEnumerable<bool> Bake(
        WorldScene scene,
        IReadOnlyList<LightmapBaker.LightmapTarget> targets,
        LightmapBaker.LightmapTriangle[] triangles,
        LightmapBaker.LightmapBvh bvh,
        WorldRayCast cast,
        Vector3f sunDirection,
        Vector3f sunColour,
        Vector3f skyColour,
        int bounces,
        int hemisphereRays,
        IWorkScheduler work,
        IIgiLogger log,
        Action<BakedLightmaps?> done,
        CancellationToken token)
    {
        long startedAt = Stopwatch.GetTimestamp();

        // The token goes INTO each phase, not merely between them. A phase is tens of seconds of path
        // tracing across every core; a bake cancelled at the top of one went on tracing the level the
        // player had just abandoned for the whole of it -- the new level loading against a machine with
        // nothing left to load it with, which is what leaving a big mission felt like -- and held its
        // texel maps, triangles and BVH the entire time. Several of those overlapped at a few hundred
        // megabytes each. Each phase reads the token per texel, so cancelling stops within a block.
        token.ThrowIfCancellationRequested();

        foreach (bool slice in LightmapBaker.BakeDirectAndSky(
            targets, cast, sunDirection, sunColour, skyColour, hemisphereRays, work, token))
        {
            yield return slice;
        }

        token.ThrowIfCancellationRequested();

        foreach (bool slice in LightmapBaker.BakeBounces(
            targets, triangles, bvh, skyColour, bounces, hemisphereRays, work, token))
        {
            yield return slice;
        }

        token.ThrowIfCancellationRequested();

        foreach (bool slice in LightmapBaker.Denoise(targets, work, cancellationToken: token))
        {
            yield return slice;
        }

        token.ThrowIfCancellationRequested();

        BakedLightmaps baked = BakedLightmaps.Compose(scene, targets);

        double seconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
        log.Information(
            LogCategory,
            FormattableString.Invariant(
                $"Lightmap bake took {seconds:0.00} s over {baked.ObjectCount} object(s); it swaps in from the next frame."));

        done(baked);
    }

    /// <summary>Takes the finished pages, if the bake is finished.</summary>
    /// <param name="baked">Receives the composited pages, or <see langword="null"/> on a cancelled or failed bake.</param>
    /// <returns><see langword="true"/> exactly once, on the first call after the bake completes.</returns>
    /// <remarks>Call from the render thread: whatever comes back is about to become GL textures.</remarks>
    public bool TryCollect(out BakedLightmaps? baked)
    {
        baked = null;

        if (_collected || !_job.Poll())
        {
            return false;
        }

        _collected = true;

        // A bake that throws must not take the game with it; the shipped lightmaps are the fallback. A
        // cancelled one -- quitting, or changing level -- is simply never collected.
        if (_job.Failure is { } failure)
        {
            _log.Error(LogCategory, $"The lightmap bake failed and the shipped lightmaps stay: {failure.Message}", failure);
        }

        baked = _job.Result;
        return true;
    }

    /// <summary>Blocks until the bake finishes and takes the result.</summary>
    /// <returns>The composited pages, or <see langword="null"/> as <see cref="TryCollect"/> describes.</returns>
    /// <remarks>For a capture run, where determinism outranks responsiveness. Interactive play must not call this.</remarks>
    public BakedLightmaps? WaitForResult()
    {
        _collected = true;
        return _job.Wait();
    }

    /// <summary>Asks the bake to stop, for a level change or a shutdown.</summary>
    public void Cancel() => _cancellation.Cancel();
}
