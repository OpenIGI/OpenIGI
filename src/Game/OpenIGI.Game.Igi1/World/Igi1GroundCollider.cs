using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Project I.G.I.'s terrain as I.G.I. 2's collision asks for the ground.</summary>
/// <remarks>
/// A vertical line stands on the terrain's height under its start, as I.G.I. 2's terrain answers it; a line in any direction is
/// cast at the terrain's own triangles. Every hit reports the terrain's one game material, translated into the row I.G.I. 2's
/// material table gives it.
/// </remarks>
public sealed class Igi1GroundCollider : IWorldGroundCollider
{
    private readonly TerrainData _terrain;
    private readonly int? _material;

    /// <summary>Initializes a new instance of the <see cref="Igi1GroundCollider"/> class.</summary>
    /// <param name="terrain">The level's terrain.</param>
    /// <param name="material">Translates a Project I.G.I. game material into I.G.I. 2's row, or <see langword="null"/> for none.</param>
    /// <param name="owner">What a hit reports as struck.</param>
    public Igi1GroundCollider(TerrainData terrain, Func<int, int?> material, object owner)
    {
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        ArgumentNullException.ThrowIfNull(material);
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _material = material(TerrainRayTrace.GameMaterialOf(terrain));
    }

    /// <inheritdoc/>
    public object Owner { get; }

    /// <inheritdoc/>
    public void NextZ(WorldLineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        for (int i = 0; i < trace.Count; i++)
        {
            Vector3d start = trace.Starts[i];
            double endZ = trace.Ends[i].Z;
            double high = Math.Max(start.Z, endZ);
            double low = Math.Min(start.Z, endZ);
            if (!_terrain.TryGetGround(start.X, start.Y, high, aboveUnits: 0.0, out TerrainGround ground)
                || !(ground.Height >= low) || !(ground.Height <= high))
            {
                continue;
            }

            double distance = Math.Abs(start.Z - ground.Height);
            WorldIntersection hit = trace.Intersections[i];
            if (hit.IsHit && !(distance < hit.Distance))
            {
                continue;
            }

            Vector3f normal = new((float)ground.NormalX, (float)ground.NormalY, (float)ground.NormalZ);
            Record(hit, new Vector3d(start.X, start.Y, ground.Height), normal, distance);
        }
    }

    /// <inheritdoc/>
    public void TraceLines(WorldLineTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        for (int i = 0; i < trace.Count; i++)
        {
            WorldTraceRequest request = new(trace.Starts[i], trace.Ends[i], WorldTraceMask.Terrain);
            if (!TerrainRayTrace.TryTrace(_terrain, request, out WorldTraceHit crossed))
            {
                continue;
            }

            WorldIntersection hit = trace.Intersections[i];
            if (hit.IsHit && !(crossed.Distance < hit.Distance))
            {
                continue;
            }

            Record(hit, crossed.Position, crossed.Normal, crossed.Distance);
        }
    }

    private void Record(WorldIntersection hit, Vector3d position, Vector3f normal, double distance)
    {
        hit.IsHit = true;
        hit.HitObject = this;
        hit.Position = position;
        hit.LocalPosition = new Vector3f((float)position.X, (float)position.Y, (float)position.Z);
        hit.Normal = normal;
        hit.LocalNormal = normal;
        hit.Distance = distance;
        hit.IsDoubleFace = false;
        hit.EnterMaterial = _material;
        hit.LeaveMaterial = null;
        hit.Light = 1f;
    }
}
