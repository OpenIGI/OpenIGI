using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// Every placed object's distance field, in one structure that can be asked about the whole level.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MeshDistanceFieldBaker"/> answers "how far is this point from <em>this model</em>". A
/// trace needs "how far is this point from <em>anything</em>", and that is this: one baked field per
/// distinct model, and an instance per placement holding where it stands.
/// </para>
/// <para>
/// <b>One field per model, not per placement.</b> A level draws the same crate two hundred times, and
/// the distance field of a crate is a property of the crate rather than of where it is standing — so
/// the bake is shared and the placement contributes only a transform. On level 1 that is the difference
/// between baking seven hundred fields and baking a few dozen.
/// </para>
/// <para>
/// <b>This is the reference implementation, and it exists to be disagreed with.</b> The clipmap the GPU
/// builds is an approximation of exactly this answer, sampled onto a grid, and there is no way to tell
/// a wrong clipmap from a right one by looking at the frame it produces. Having the exact answer
/// available on the CPU is what makes the approximation checkable.
/// </para>
/// </remarks>
public sealed class SceneDistanceField
{
    private const string LogCategory = "DistanceField";

    private readonly MeshDistanceField[] _fields;
    private readonly Instance[] _instances;
    private readonly float _surface;

    private SceneDistanceField(MeshDistanceField[] fields, Instance[] instances)
    {
        _fields = fields;
        _instances = instances;

        // The finest half-voxel of any field, which is what a trace can call a surface. The fields
        // are unsigned, so a ray never sees a zero crossing -- near a wall the trilinear read
        // bottoms out around half a voxel, and a hit threshold below that floor lets the ray step
        // straight through the wall it was slowing down for.
        float finest = float.MaxValue;

        foreach (MeshDistanceField field in fields)
        {
            finest = MathF.Min(
                finest,
                MathF.Min(MathF.Min(field.VoxelSize.X, field.VoxelSize.Y), field.VoxelSize.Z));
        }

        _surface = fields.Length > 0 ? finest * 0.5f : 0.02f;
    }

    /// <summary>Gets how many distinct models were baked.</summary>
    public int FieldCount => _fields.Length;

    /// <summary>Gets how many placements reference them.</summary>
    public int InstanceCount => _instances.Length;

    /// <summary>Gets the total memory the baked fields occupy.</summary>
    public long BytesUsed
    {
        get
        {
            long total = 0;

            // Not named `field`: inside a property accessor that is a contextual keyword bound to the
            // synthesised backing field, so it would silently mean something else.
            foreach (MeshDistanceField baked in _fields)
            {
                total += baked.VoxelCount;
            }

            return total;
        }
    }

    /// <summary>Assembles a scene from fields that have already been baked.</summary>
    /// <param name="fields">The baked fields, one per distinct model.</param>
    /// <param name="instances">
    /// Where each one stands: an index into <paramref name="fields"/>, a world position, and the same
    /// orientation the renderer draws that placement with.
    /// </param>
    /// <returns>The scene's field.</returns>
    /// <remarks>
    /// <b>Separate from <see cref="Build"/> so that this can be tested at all.</b> Building from a level
    /// means walking <see cref="WorldPlacement"/>s, which name a <c>MefModel</c> — a type with a private
    /// constructor that only a real pack file can produce. Keeping the assembly and the walk apart means
    /// the part with the arithmetic in it can be checked against cubes, and the part that reads a level
    /// has nothing in it to get wrong.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An instance names a field that does not exist.</exception>
    public static SceneDistanceField FromInstances(
        IReadOnlyList<MeshDistanceField> fields,
        IEnumerable<(int FieldIndex, Vector3d Position, Matrix3f Orientation)> instances)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(instances);

        List<Instance> placed = [];

        foreach ((int index, Vector3d position, Matrix3f orientation) in instances)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, fields.Count);

            placed.Add(Instance.From(fields[index], index, position, orientation));
        }

        return new SceneDistanceField([.. fields], [.. placed]);
    }

    /// <summary>Bakes a field for every distinct model in a scene and records where each one stands.</summary>
    /// <param name="placements">The level's placed objects.</param>
    /// <param name="geometry">Supplies a model's drawable geometry, or <see langword="null"/> to skip it.</param>
    /// <param name="resolution">How many voxels the longest axis of each model gets.</param>
    /// <param name="logger">Where to report progress.</param>
    /// <param name="work">
    /// Where the models' bakes run: all at once on a scheduler that runs work concurrently, one a slice on
    /// one that does not.
    /// </param>
    /// <param name="complete">Given the scene's field once it is built.</param>
    /// <param name="cancellation">Aborts a bake that is no longer wanted.</param>
    /// <returns>The bake, a slice at a time; see <see cref="SteppedJob{T}"/>.</returns>
    /// <remarks>
    /// <b>Geometry is supplied rather than read.</b> A placement names a model, but which mesh describes
    /// it is a question with two defensible answers — the collision hull, which is what the AI sees, or
    /// the drawn surface, which is what light actually falls on. This takes the second, and takes it as
    /// an argument so the choice sits with the caller and the bake stays testable without a level.
    /// </remarks>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IEnumerable<bool> Build(
        IEnumerable<WorldPlacement> placements,
        Func<MefModel, MeshGeometry?> geometry,
        int resolution,
        IOpenIgiLogger? logger,
        IWorkScheduler work,
        Action<SceneDistanceField> complete,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(complete);

        return BuildSlices(placements, geometry, resolution, logger ?? NullLogger.Instance, work, complete, cancellation);
    }

    /// <summary>The bake itself, checked by <see cref="Build"/> before it is handed out.</summary>
    private static IEnumerable<bool> BuildSlices(
        IEnumerable<WorldPlacement> placements,
        Func<MefModel, MeshGeometry?> geometry,
        int resolution,
        IOpenIgiLogger log,
        IWorkScheduler work,
        Action<SceneDistanceField> complete,
        CancellationToken cancellation)
    {

        List<MeshDistanceField> fields = [];
        List<Instance> instances = [];
        Dictionary<MefModel, int> baked = [];
        int skipped = 0;
        int tooCoarse = 0;

        // The distinct models, in the order they are first placed. The bake of each is independent of
        // every other -- it is a pure function of one mesh and the resolution -- so they run in parallel;
        // the ORDER is kept sequential, because a field's index is what the placements and the cached
        // file refer to it by, and a parallel walk that assigned indices as bakes finished would write a
        // different file from the same level every run.
        WorldPlacement[] placed = [.. placements];
        List<MefModel> models = [];
        Dictionary<MefModel, int> slots = [];
        foreach (WorldPlacement placement in placed)
        {
            if (!slots.ContainsKey(placement.Model))
            {
                slots[placement.Model] = models.Count;
                models.Add(placement.Model);
            }
        }

        // The geometry first, on this thread: the callback caches what it builds and is not written to
        // be entered from several at once.
        MeshGeometry?[] meshes = new MeshGeometry?[models.Count];
        for (int i = 0; i < models.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            meshes[i] = geometry(models[i]);
            yield return true;
        }

        // One core is left free, so a bake behind a loading screen or a menu does not take the render
        // thread's with it. This is what used to hold the level for ten to twenty seconds on one core.
        MeshDistanceField?[] bakes = new MeshDistanceField?[models.Count];

        IEnumerable<bool> perModel = Stepping.Range(
            work,
            models.Count,
            Math.Max(1, Environment.ProcessorCount - 1),
            i =>
            {
                if (meshes[i] is { } mesh && mesh.Indices.Length >= 3)
                {
                    bakes[i] = MeshDistanceFieldBaker.Bake(mesh.Vertices, mesh.Indices, resolution);
                }
            },
            cancellation);

        foreach (bool slice in perModel)
        {
            yield return slice;
        }

        cancellation.ThrowIfCancellationRequested();

        for (int i = 0; i < models.Count; i++)
        {
            int index = -1;

            if (bakes[i] is { } field)
            {
                float coarsest = MathF.Max(
                    MathF.Max(field.VoxelSize.X, field.VoxelSize.Y), field.VoxelSize.Z);

                if (coarsest <= MaximumVoxelSize)
                {
                    index = fields.Count;
                    fields.Add(field);
                }
                else
                {
                    tooCoarse++;
                }
            }

            baked[models[i]] = index;
        }

        foreach (WorldPlacement placement in placed)
        {
            int index = baked[placement.Model];
            if (index < 0)
            {
                skipped++;
                continue;
            }

            instances.Add(Instance.From(fields[index], index, placement.Position, placement.Orientation));
        }

        SceneDistanceField scene = new([.. fields], [.. instances]);

        log.Information(
            LogCategory,
            $"Scene distance field: {scene.FieldCount} model(s) baked, {scene.InstanceCount} instance(s), "
            + $"{scene.BytesUsed / 1024} KiB{(skipped > 0 ? $", {skipped} placement(s) with no usable mesh" : string.Empty)}.");

        if (tooCoarse > 0)
        {
            log.Information(
                LogCategory,
                $"{tooCoarse} model(s) bake to voxels too coarse to describe anything and were left out.");
        }

        // The widest fields, and how much of each baked as solid. A field's range is half its model's
        // span, so a large model reporting a mostly-solid interior is not a local error: the clipmap
        // union is a minimum, so that one model sets the distance for every voxel it covers.
        foreach (MeshDistanceField field in fields.OrderByDescending(f => f.Range).Take(3)
            .Concat(fields.OrderBy(f => f.Range).Take(3)))
        {
            int solid = 0;

            foreach (byte value in field.Distances)
            {
                if (value < 128)
                {
                    solid++;
                }
            }

            log.Information(
                LogCategory,
                $"  field: range {field.Range:0.#} m, {solid * 100.0 / field.Distances.Length:0.#}% solid.");
        }

        complete(scene);
    }

    /// <summary>The coarsest voxel a field may bake to before it is discarded, in metres.</summary>
    /// <remarks>
    /// <b>Coarseness, not size.</b> An earlier attempt cut on the model's extent, which throws away
    /// buildings; what actually disqualifies a field is its voxel, because every field gets the same
    /// voxel count. A crate bakes voxels of centimetres and the largest buildings a few metres, and
    /// those all describe their surfaces usefully. The sky and level-bounds shells span kilometres, so
    /// their voxels come to hundreds of metres — and such a field is worse than absent: its band is as
    /// coarse as its voxels, one byte step spans tens of metres, every true distance in the playable
    /// space quantises to noise, its bounds cover the whole level, and the union takes a minimum. That
    /// noise is what stopped the sun trace in mid-air. Sixteen metres keeps every model whose surfaces
    /// a trace can actually land on and drops the shells whose answers were never real.
    /// </remarks>
    public const float MaximumVoxelSize = 16f;

    /// <summary>Gets the baked fields, in the order instances index them.</summary>
    /// <remarks>
    /// Handed out so the atlas can pack them. The array itself is not copied — these are large and
    /// immutable once baked, and duplicating a level's worth of them to hand them to one consumer
    /// would be megabytes for nothing.
    /// </remarks>
    public IReadOnlyList<MeshDistanceField> Fields => _fields;

    /// <summary>What one placement is, in the terms a shader needs to sample it.</summary>
    /// <param name="FieldIndex">Which baked field describes this placement.</param>
    /// <param name="Position">Where the model stands, in world space.</param>
    /// <param name="Orientation">
    /// The rotation that takes a world offset into the model's own space — already the inverse of the
    /// one the renderer draws with, so a consumer applies it directly rather than transposing again.
    /// </param>
    /// <param name="BoundsMin">The minimum corner of the world-space box the field covers.</param>
    /// <param name="BoundsMax">The maximum corner of that box.</param>
    public readonly record struct InstanceView(
        int FieldIndex, Vector3d Position, Matrix3f Orientation, Vector3d BoundsMin, Vector3d BoundsMax);

    /// <summary>Describes every placement, for a consumer that will resample this onto a grid.</summary>
    /// <returns>One entry per placement.</returns>
    /// <remarks>
    /// The instances are private because nothing outside should be able to move an object without its
    /// bounds being recomputed. This hands out a copy of what they say instead, which is what the
    /// clipmap builder packs into a storage buffer.
    /// </remarks>
    public InstanceView[] Describe()
    {
        InstanceView[] views = new InstanceView[_instances.Length];

        for (int i = 0; i < _instances.Length; i++)
        {
            Instance instance = _instances[i];
            views[i] = new InstanceView(
                instance.FieldIndex,
                instance.Position,
                instance.Orientation,
                instance.BoundsMin,
                instance.BoundsMax);
        }

        return views;
    }

    /// <summary>How far a world-space point is from the nearest surface in the level.</summary>
    /// <param name="point">The point, in world space.</param>
    /// <returns>
    /// The distance to the nearest surface. The fields are unsigned — this game's meshes are
    /// single-sided shells with no meaningful interior — so both sides of a wall read as clearance
    /// from it, and near zero means at a surface.
    /// </returns>
    /// <remarks>
    /// The minimum over every instance, which is the correct combination for a distance field and the
    /// reason a union of shapes is free: the nearest surface to a point is the nearest surface of
    /// whichever object is nearest, and taking the smaller of two distances says exactly that.
    /// <para>
    /// Instances whose box the point lies outside contribute the distance to that box, so a point in
    /// open ground still gets a usable answer rather than the sentinel — which is what lets a trace take
    /// a long step through empty air instead of creeping.
    /// </para>
    /// </remarks>
    public float Sample(Vector3d point)
    {
        float nearest = float.MaxValue;

        foreach (Instance instance in _instances)
        {
            // A cheap reject on the world-aligned box first: it is looser than the object's own grid, so
            // whatever it says is at least this far cannot be beaten by the grid inside it. This only
            // skips work -- it is never the answer, because a rotated box is larger than the grid it
            // bounds and a point in that slack is outside the grid though inside the box.
            float outside = instance.DistanceToBounds(point);

            if (outside >= nearest)
            {
                continue;
            }

            // The honest distance is measured to the object's own grid in local space. Outside it the
            // field holds nothing, so the distance to it stands; sampling the field there instead would
            // clamp to an edge voxel and report a surface at the point when it is metres away. This is
            // exactly what the clipmap build does on the GPU, so the two agree at grid edges.
            MeshDistanceField field = _fields[instance.FieldIndex];

            // A field baked with a range of zero encodes nothing -- every voxel decodes to zero -- so it
            // is treated as absent rather than as a surface filling its own box, the same as the clipmap
            // build does.
            if (field.Range <= 0f)
            {
                continue;
            }

            Vector3f local = instance.ToLocal(point);
            float beyond = field.DistanceOutsideSampledRegion(local);

            nearest = MathF.Min(nearest, beyond > 0f ? beyond : field.Sample(local));
        }

        return nearest == float.MaxValue ? float.MaxValue / 4f : nearest;
    }

    /// <summary>Names the instance responsible for the answer at a point.</summary>
    /// <param name="point">The point, in world space.</param>
    /// <returns>A description of the nearest instance and what it reported.</returns>
    /// <remarks>
    /// For diagnosis only. A union hides which member decided the result, and when the union is wrong
    /// that is the only thing worth knowing -- reasoning about which model it might be, at the scale
    /// these levels run to, is guesswork.
    /// </remarks>
    public string Explain(Vector3d point)
    {
        float nearest = float.MaxValue;
        int culprit = -1;

        for (int i = 0; i < _instances.Length; i++)
        {
            Instance instance = _instances[i];
            MeshDistanceField instanceField = _fields[instance.FieldIndex];
            Vector3f local = instance.ToLocal(point);
            float beyond = instanceField.DistanceOutsideSampledRegion(local);
            float value = beyond > 0f ? beyond : instanceField.Sample(local);

            if (value < nearest)
            {
                nearest = value;
                culprit = i;
            }
        }

        if (culprit < 0)
        {
            return "no instance";
        }

        Instance winner = _instances[culprit];
        MeshDistanceField field = _fields[winner.FieldIndex];
        Vector3d span = winner.BoundsMax - winner.BoundsMin;

        float voxel = MathF.Max(MathF.Max(field.VoxelSize.X, field.VoxelSize.Y), field.VoxelSize.Z);

        return FormattableString.Invariant(
            $"field {winner.FieldIndex} reported {nearest:0.##}; voxel {voxel:0.#}, band {field.Range:0.#}, span ({span.X:0}, {span.Y:0}, {span.Z:0})");
    }

    /// <summary>Marches a ray until it meets a surface.</summary>
    /// <param name="origin">Where the ray starts, in world space.</param>
    /// <param name="direction">Its direction. Expected to be unit length.</param>
    /// <param name="maxDistance">How far to march before giving up.</param>
    /// <param name="hitDistance">Receives how far along the ray the surface was met.</param>
    /// <returns><see langword="true"/> when a surface was met within the distance.</returns>
    /// <remarks>
    /// <para>
    /// Sphere tracing: step by exactly the distance to the nearest surface, which by definition cannot
    /// pass through one. A ray crossing open ground clears it in a handful of steps however much
    /// geometry stands nearby, and a ray grazing a wall slows down of its own accord as it approaches —
    /// neither behaviour needs a bounding hierarchy or a step size chosen in advance.
    /// </para>
    /// <para>
    /// The step floor is what keeps a grazing ray from taking thousands of ever-smaller steps and never
    /// arriving. It costs the trace the ability to resolve detail below that size, which is the right
    /// trade at the resolution these fields are baked to anyway.
    /// </para>
    /// </remarks>
    public bool TryTrace(Vector3d origin, Vector3f direction, float maxDistance, out float hitDistance)
    {
        hitDistance = 0f;

        // Derived, not written down: what counts as "at the surface" is set by the resolution of the
        // finest field, computed once at build.
        float surface = _surface;
        const float minimumStep = 0.01f;
        const int maximumSteps = 256;

        float travelled = 0f;

        for (int step = 0; step < maximumSteps && travelled < maxDistance; step++)
        {
            Vector3d at = new(
                origin.X + (direction.X * travelled),
                origin.Y + (direction.Y * travelled),
                origin.Z + (direction.Z * travelled));

            float distance = Sample(at);

            if (distance < surface)
            {
                hitDistance = travelled;
                return true;
            }

            travelled += MathF.Max(distance, minimumStep);
        }

        return false;
    }

    /// <summary>One placement: which model's field describes it, and where that model stands.</summary>
    private readonly record struct Instance(
        int FieldIndex,
        Vector3d Position,
        Matrix3f Orientation,
        Vector3d BoundsMin,
        Vector3d BoundsMax)
    {
        /// <summary>Builds an instance and the world-space box its field covers.</summary>
        public static Instance From(
            MeshDistanceField field, int index, Vector3d position, Matrix3f orientation)
        {
            // The field's box, rotated, then re-bounded. A rotated box is not a box, so what is stored
            // is the smallest axis-aligned one containing it -- conservative, which is the only safe
            // direction for a structure used to SKIP work.
            Vector3f lo = field.Origin;
            Vector3f hi = field.Extent;

            Vector3d min = new(double.MaxValue);
            Vector3d max = new(double.MinValue);

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3f local = new(
                    (corner & 1) == 0 ? lo.X : hi.X,
                    (corner & 2) == 0 ? lo.Y : hi.Y,
                    (corner & 4) == 0 ? lo.Z : hi.Z);

                Vector3d world = ToWorld(local, position, orientation);

                min = new Vector3d(
                    Math.Min(min.X, world.X), Math.Min(min.Y, world.Y), Math.Min(min.Z, world.Z));
                max = new Vector3d(
                    Math.Max(max.X, world.X), Math.Max(max.Y, world.Y), Math.Max(max.Z, world.Z));
            }

            return new Instance(index, position, orientation, min, max);
        }

        /// <summary>Takes a world point into the model's own space.</summary>
        /// <remarks>
        /// The inverse of what <c>Camera3D.CreateModelView</c> does for
        /// drawing, and it has to match it exactly or the field sits in a different place from the
        /// geometry it describes. That transform applies a 3×3 as <c>M.Transposed().Transform(v)</c>;
        /// the orientation is orthonormal, so undoing it is <c>M.Transform(v)</c>.
        /// </remarks>
        public Vector3f ToLocal(Vector3d point)
        {
            Vector3d offset = new(point.X - Position.X, point.Y - Position.Y, point.Z - Position.Z);
            Vector3d local = Orientation.Transform(offset);
            return new Vector3f((float)local.X, (float)local.Y, (float)local.Z);
        }

        /// <summary>How far a point lies outside this instance's box, or zero when it is inside.</summary>
        public float DistanceToBounds(Vector3d point)
        {
            double dx = Math.Max(Math.Max(BoundsMin.X - point.X, 0.0), point.X - BoundsMax.X);
            double dy = Math.Max(Math.Max(BoundsMin.Y - point.Y, 0.0), point.Y - BoundsMax.Y);
            double dz = Math.Max(Math.Max(BoundsMin.Z - point.Z, 0.0), point.Z - BoundsMax.Z);

            return (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static Vector3d ToWorld(Vector3f local, Vector3d position, Matrix3f orientation)
        {
            Vector3d rotated = orientation.Transposed().Transform(new Vector3d(local.X, local.Y, local.Z));
            return new Vector3d(position.X + rotated.X, position.Y + rotated.Y, position.Z + rotated.Z);
        }
    }
}
