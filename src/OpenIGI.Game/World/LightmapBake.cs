using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;

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

    private readonly Task<BakedLightmaps?> _work;
    private readonly CancellationTokenSource _cancellation;
    private bool _collected;

    private LightmapBake(Task<BakedLightmaps?> work, CancellationTokenSource cancellation)
    {
        _work = work;
        _cancellation = cancellation;
    }

    /// <summary>Gets whether the bake has finished, however it finished.</summary>
    public bool IsComplete => _work.IsCompleted;

    /// <summary>Starts baking a level's object lightmaps on a worker.</summary>
    /// <param name="scene">The scene, for each covered placement's lightmap entry.</param>
    /// <param name="targets">The texel maps from <see cref="LightmapBaker.EnumerateTargets"/>, built by the caller.</param>
    /// <param name="triangles">The world-space lightmapped triangles from <see cref="LightmapBaker.BuildTriangles"/>.</param>
    /// <param name="bvh">The BVH over <paramref name="triangles"/>.</param>
    /// <param name="cast">How to ask the world what a ray meets; must be safe to call from another thread.</param>
    /// <param name="sunDirection">The direction light arrives from, pointing towards the sun.</param>
    /// <param name="sunColour">The sun's colour.</param>
    /// <param name="skyColour">The sky's radiance a ray that escapes returns.</param>
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
        int bounces = 4,
        int hemisphereRays = 64,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(triangles);
        ArgumentNullException.ThrowIfNull(bvh);
        ArgumentNullException.ThrowIfNull(cast);

        IIgiLogger log = logger ?? NullLogger.Instance;
        CancellationTokenSource cancellation = new();

        Task<BakedLightmaps?> work = Task.Run(
            () =>
            {
                long startedAt = Stopwatch.GetTimestamp();

                try
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    LightmapBaker.BakeDirectAndSky(targets, cast, sunDirection, sunColour, skyColour, hemisphereRays);

                    cancellation.Token.ThrowIfCancellationRequested();
                    LightmapBaker.BakeBounces(targets, triangles, bvh, skyColour, bounces, hemisphereRays);

                    cancellation.Token.ThrowIfCancellationRequested();
                    LightmapBaker.Denoise(targets);

                    cancellation.Token.ThrowIfCancellationRequested();
                    BakedLightmaps baked = BakedLightmaps.Compose(scene, targets);

                    double seconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
                    log.Information(
                        LogCategory,
                        FormattableString.Invariant(
                            $"Lightmap bake took {seconds:0.00} s on a worker over {baked.ObjectCount} object(s); it swaps in from the next frame."));
                    return baked;
                }
                catch (OperationCanceledException)
                {
                    // Quitting or changing level during a bake: the pages are simply never collected and the
                    // shipped lightmaps stay in place.
                    return null;
                }
                catch (Exception ex)
                {
                    // A bake that throws must not take the game with it; the shipped lightmaps are the fallback.
                    log.Error(LogCategory, $"The lightmap bake failed and the shipped lightmaps stay: {ex.Message}", ex);
                    return null;
                }
            },
            CancellationToken.None);

        return new LightmapBake(work, cancellation);
    }

    /// <summary>Takes the finished pages, if the bake is finished.</summary>
    /// <param name="baked">Receives the composited pages, or <see langword="null"/> on a cancelled or failed bake.</param>
    /// <returns><see langword="true"/> exactly once, on the first call after the bake completes.</returns>
    /// <remarks>Call from the render thread: whatever comes back is about to become GL textures.</remarks>
    public bool TryCollect(out BakedLightmaps? baked)
    {
        baked = null;

        if (_collected || !_work.IsCompleted)
        {
            return false;
        }

        _collected = true;
        baked = _work.Result;
        return true;
    }

    /// <summary>Blocks until the bake finishes and takes the result.</summary>
    /// <returns>The composited pages, or <see langword="null"/> as <see cref="TryCollect"/> describes.</returns>
    /// <remarks>For a capture run, where determinism outranks responsiveness. Interactive play must not call this.</remarks>
    public BakedLightmaps? WaitForResult()
    {
        _collected = true;
        return _work.GetAwaiter().GetResult();
    }

    /// <summary>Asks the bake to stop, for a level change or a shutdown.</summary>
    public void Cancel() => _cancellation.Cancel();
}
