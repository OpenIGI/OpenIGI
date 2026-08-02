using OpenIGI.Core.Diagnostics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Bakes a level's distance fields on a worker, and hands them over when they are ready.
/// </summary>
/// <remarks>
/// <para>
/// The same handover <see cref="IrradianceProbeBake"/> uses, and for the same reason recorded there: a
/// multi-second job on the thread holding a live GL context stops the process answering, hands the GPU
/// no work for the whole of it, and loses the context to the operating system's watchdog. Loading a
/// mission that way died with exit <c>0xCFFFFFFF</c>.
/// </para>
/// <para>
/// <b>The split is where the GL is.</b> Tracing the geometry is arithmetic and moves to a worker
/// freely; the texture upload cannot, because a GL texture belongs to the context that made it. So the
/// worker produces the fields and the atlas, and the render thread collects them and uploads.
/// </para>
/// <para>
/// A lockstep capture waits rather than polls, so a reference image either contains the field or does
/// not — never whichever frame the worker happened to reach.
/// </para>
/// </remarks>
public sealed class DistanceFieldBake
{
    private const string LogCategory = "DistanceField";

    private readonly Task<Result?> _work;
    private readonly CancellationTokenSource _cancellation;
    private bool _collected;

    private DistanceFieldBake(Task<Result?> work, CancellationTokenSource cancellation)
    {
        _work = work;
        _cancellation = cancellation;
    }

    /// <summary>What the bake produces: the exact field, and every model's packed into one volume.</summary>
    /// <param name="Scene">The level's field, which the clipmaps resample and validation compares against.</param>
    /// <param name="Atlas">Every distinct model's field, packed for the GPU.</param>
    public readonly record struct Result(SceneDistanceField Scene, DistanceFieldAtlas Atlas);

    /// <summary>Starts a bake on a thread-pool thread.</summary>
    /// <param name="placements">The level's placed objects.</param>
    /// <param name="resolution">How many voxels the longest axis of each model gets.</param>
    /// <param name="logger">Where to report progress.</param>
    /// <returns>A handle the render thread collects from.</returns>
    /// <remarks>
    /// The placements are copied before the worker starts. The list they came from belongs to a scene
    /// the main thread goes on using, and a bake reading it while something else appends is a data race
    /// rather than a stale answer — the same hazard that made the probe bake take the doors-shut cast.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="placements"/> is <see langword="null"/>.</exception>
    public static DistanceFieldBake Start(
        IEnumerable<WorldPlacement> placements,
        int resolution = MeshDistanceFieldBaker.DefaultResolution,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(placements);

        IIgiLogger log = logger ?? NullLogger.Instance;
        WorldPlacement[] snapshot = [.. placements];

        // The token is taken before the work starts, and the work captures the token rather than the
        // handle. Task.Run begins immediately, so a lambda reaching back through a not-yet-assigned
        // handle for its own cancellation source is a race the worker usually wins and occasionally
        // does not.
        CancellationTokenSource cancellation = new();
        CancellationToken token = cancellation.Token;

        Task<Result?> work = Task.Run<Result?>(() =>
        {
            try
            {
                // One geometry build per distinct model, cached here rather than inside the field
                // builder: MefMeshBuilder.Build allocates a whole mesh, and a level that places the same
                // crate two hundred times would otherwise build it two hundred times to bake it once.
                Dictionary<MefModel, MeshGeometry?> meshes = [];

                MeshGeometry? Geometry(MefModel model)
                {
                    if (!meshes.TryGetValue(model, out MeshGeometry? mesh))
                    {
                        // In METRES, like the placements the fields stand at. Model vertices are raw
                        // engine units (4096 to the metre); baking them unscaled put every field
                        // 4096 times too large around a metre-space position, and the validator never
                        // saw it because the exact field and the clipmap shared the mistake.
                        mesh = MefMeshBuilder.Build(model, (float)(1.0 / WorldScene.UnitsPerMetre));
                        meshes[model] = mesh;
                    }

                    return mesh;
                }

                SceneDistanceField scene = SceneDistanceField.Build(
                    snapshot, Geometry, resolution, log, token);

                if (scene.FieldCount == 0)
                {
                    log.Information(LogCategory, "This level has no bakeable geometry; no field was built.");
                    return null;
                }

                DistanceFieldAtlas atlas = DistanceFieldAtlas.Build(scene.Fields);
                log.Information(LogCategory, atlas.ToString());

                return new Result(scene, atlas);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        });

        return new DistanceFieldBake(work, cancellation);
    }

    /// <summary>Takes the result if the worker has finished, without waiting.</summary>
    /// <param name="result">Receives the result, or its default when nothing is ready.</param>
    /// <returns><see langword="true"/> exactly once, on the call that collects it.</returns>
    /// <remarks>
    /// Collected exactly once, so a caller can poll it every frame and upload on the frame it arrives
    /// without guarding against doing so twice.
    /// </remarks>
    public bool TryCollect(out Result result)
    {
        result = default;

        if (_collected || !_work.IsCompleted)
        {
            return false;
        }

        _collected = true;

        if (_work.IsCompletedSuccessfully && _work.Result is { } baked)
        {
            result = baked;
            return true;
        }

        return false;
    }

    /// <summary>Waits for the bake and returns its result.</summary>
    /// <returns>The result, or <see langword="null"/> when there was nothing to bake.</returns>
    /// <remarks>
    /// For a lockstep capture, which must be reproducible and has nobody watching the window. An
    /// interactive load must not use this — blocking here is precisely what the worker exists to avoid.
    /// </remarks>
    public Result? WaitForResult()
    {
        _collected = true;
        return _work.GetAwaiter().GetResult();
    }

    /// <summary>Abandons a bake whose level is being unloaded.</summary>
    public void Cancel() => _cancellation.Cancel();
}
