using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Which level of detail each placed model is drawn at from a view, as the original picks it (<see cref="ModelLodSelector"/>),
/// and the placement that draws it: the level's own instance, model and lightmap entry.
/// </summary>
/// <remarks>
/// A model's levels are the instances named after it with the last character counted on (<c>0x4CED50</c>), and every level is
/// baked its own lightmap, entry <c>LightmapIndex + n</c>.
/// </remarks>
public sealed class ModelLodPolicy
{
    private readonly ModelLodSettings _settings;
    private readonly ModelLodSettings _bones;
    private readonly IReadOnlyDictionary<string, float[]> _distances;
    private readonly Dictionary<WorldPlacement, Chain> _chains = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="ModelLodPolicy"/> class.</summary>
    /// <param name="settings">The level's settings for rigid models.</param>
    /// <param name="distances">The models' own distances by their first level's name, world units.</param>
    /// <param name="scale">The detail scale, <c>gLodScale</c>.</param>
    /// <param name="bones">The level's settings for skinned models; none leaves them whole.</param>
    /// <param name="rules">The title's way of picking; Project I.G.I.'s by default.</param>
    public ModelLodPolicy(
        in ModelLodSettings settings,
        IReadOnlyDictionary<string, float[]> distances,
        float scale = 1f,
        ModelLodSettings? bones = null,
        ModelLodRules? rules = null)
    {
        Rules = rules ?? ModelLodRules.Igi1;
        ArgumentNullException.ThrowIfNull(distances);
        _settings = settings;
        _bones = bones ?? default;
        HasBoneSettings = bones is not null;
        _distances = distances;
        Scale = scale;
    }

    /// <summary>Gets the detail scale.</summary>
    public float Scale { get; }

    /// <summary>Gets the title's way of picking.</summary>
    public ModelLodRules Rules { get; }

    /// <summary>Gets a value indicating whether skinned models give up detail too.</summary>
    public bool HasBoneSettings { get; }

    /// <summary>Picks the level a skinned model is drawn at from a view, <c>0x4CFF70</c>.</summary>
    /// <param name="name">The first level's instance name, which the model's own distances are kept by.</param>
    /// <param name="first">The first level's model.</param>
    /// <param name="triangles">Each level's triangle count, the first level's first.</param>
    /// <param name="positionMetres">Where the body's origin is drawn this frame, metres.</param>
    /// <param name="camera">The view.</param>
    /// <param name="fieldOfView">The view's horizontal half-angle tangent.</param>
    /// <returns>The level, or <c>-1</c> when it is beyond its cutoff.</returns>
    /// <remarks>
    /// Against a rigid model's pick, the radius is the body's own (<c>MESH+0x78</c>, which the body keeps from its model) rather
    /// than the render sphere's, the detail scale is not applied, and the level's settings are the ones for bone objects.
    /// </remarks>
    public int SelectBone(string name, MefModel first, ReadOnlySpan<int> triangles, Vector3d positionMetres, in Camera3D camera, float fieldOfView)
    {
        ArgumentNullException.ThrowIfNull(first);
        if (!HasBoneSettings)
        {
            return 0;
        }

        Vector3f centre = first.RenderSphereCentre;
        Vector3d eye = WorldScene.ToUnits(camera.Position);
        Vector3d at = WorldScene.ToUnits(positionMetres);
        Matrix3f o = camera.Orientation;
        double depth = (o.M31 * (at.X - eye.X + centre.X)) + (o.M32 * (at.Y - eye.Y + centre.Y)) + (o.M33 * (at.Z - eye.Z + centre.Z));
        float[] distances = _distances.TryGetValue(name, out float[]? own) ? own : [];
        return ModelLodSelector.Select(depth, first.NativeMarkerBound, 1.0, Math.Min(fieldOfView, Rules.FieldOfViewCeiling), _bones, triangles, distances);
    }

    /// <summary>Picks the level a placement is drawn at from a view.</summary>
    /// <param name="placement">The placement, as authored.</param>
    /// <param name="positionMetres">Where it is drawn this frame, metres.</param>
    /// <param name="camera">The view.</param>
    /// <param name="fieldOfView">The view's horizontal half-angle tangent.</param>
    /// <returns>The level, or <c>-1</c> when it is beyond its cutoff.</returns>
    public int Select(WorldPlacement placement, Vector3d positionMetres, in Camera3D camera, float fieldOfView)
    {
        ArgumentNullException.ThrowIfNull(placement);
        Chain chain = ChainOf(placement);
        MefModel first = placement.Model;

        // 0x4CFCB0: the depth of the position plus the sphere's centre, the centre taken in the world's axes as it stands.
        Vector3f centre = first.RenderSphereCentre;
        Vector3d eye = WorldScene.ToUnits(camera.Position);
        Vector3d at = WorldScene.ToUnits(positionMetres);
        if (Rules.RigidByDistance)
        {
            // ModelObj_CalcDistanceSquared: the eye's offset taken into the model's own frame, then the sphere's centre added.
            Vector3d offset = at - eye;
            Matrix3f m = placement.Orientation;
            double x = (m.M11 * offset.X) + (m.M21 * offset.Y) + (m.M31 * offset.Z) + centre.X;
            double y = (m.M12 * offset.X) + (m.M22 * offset.Y) + (m.M32 * offset.Z) + centre.Y;
            double z = (m.M13 * offset.X) + (m.M23 * offset.Y) + (m.M33 * offset.Z) + centre.Z;
            float bias = first.Format == MefMeshFormat.Lightmapped ? 1f : Rules.BiasSquared;
            return ModelLodSelector.SelectByDistance(
                (float)((x * x) + (y * y) + (z * z)), 1f, fieldOfView, bias, first.RenderSphereRadius, _settings.Cull, chain.Triangles.Length, chain.Distances);
        }

        Matrix3f o = camera.Orientation;
        double depth = (o.M31 * (at.X - eye.X + centre.X)) + (o.M32 * (at.Y - eye.Y + centre.Y)) + (o.M33 * (at.Z - eye.Z + centre.Z));
        return ModelLodSelector.Select(depth, first.RenderSphereRadius, Scale, fieldOfView, _settings, chain.Triangles, chain.Distances);
    }

    /// <summary>Gets the placement that draws a placement at one of its levels.</summary>
    /// <param name="placement">The placement, as authored or as displayed.</param>
    /// <param name="level">The level, above nought.</param>
    /// <returns>The level's placement, or <see langword="null"/> when the model has no such level.</returns>
    public WorldPlacement? Level(WorldPlacement placement, int level)
    {
        ArgumentNullException.ThrowIfNull(placement);
        Chain chain = ChainOf(placement);
        return level > 0 && level < chain.Levels.Length ? chain.Levels[level] : null;
    }

    private Chain ChainOf(WorldPlacement placement)
    {
        if (_chains.TryGetValue(placement, out Chain? known))
        {
            return known;
        }

        List<WorldPlacement> levels = [placement];
        List<int> triangles = [placement.Model.TriangleCount];
        string name = placement.Instance.Name;
        for (int level = 1; level < LodModelChain.MaxLevels && name.Length > 0; level++)
        {
            name = name[..^1] + (char)(name[^1] + 1);
            MtpInstance instance = default;
            bool found = false;
            foreach (MtpInstance candidate in placement.Pack.Instances)
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    instance = candidate;
                    found = true;
                    break;
                }
            }

            if (!found || !placement.Pack.TryGetModel(instance, out MefModel? model) || model is null)
            {
                break;
            }

            levels.Add(placement with
            {
                Instance = instance,
                Model = model,
                LightmapIndex = placement.LightmapIndex >= 0 ? placement.LightmapIndex + level : -1,
                LightmapEntry = placement.LevelLightmapEntries is { } entries ? (level < entries.Count ? entries[level] : string.Empty) : placement.LightmapEntry,
                Variants = null,
                BentGeometry = null,
            });
            triangles.Add(model.TriangleCount);
        }

        float[] distances = _distances.TryGetValue(placement.Instance.Name, out float[]? own) ? own : [];
        Chain chain = new([.. levels], [.. triangles], distances);
        _chains[placement] = chain;
        return chain;
    }

    private sealed record Chain(WorldPlacement[] Levels, int[] Triangles, float[] Distances);
}
