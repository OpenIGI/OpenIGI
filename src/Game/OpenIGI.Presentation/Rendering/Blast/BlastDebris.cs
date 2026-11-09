using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Fracture;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// What a bomb's blast breaks, as the frame shows it: the props near it shattered and the walls facing it holed, and the
/// pieces of both thrown out, tumbling, landing and lying where they fell until the level is put back.
/// </summary>
/// <remarks>
/// <para>
/// The breaking itself (<see cref="BlastShatterPlanner"/>) runs off the frame's thread as the blast goes off; its pieces come
/// into the world a few ticks later, already as far along as the blast is, under the flash and the fireball that hide the
/// wait. Then the placements leave the frame -- or take their holed meshes -- and their pieces fly.
/// </para>
/// <para>
/// Every piece is a rigid body of its own shape (<see cref="DebrisBody"/>): its corners meet the ground together, with
/// bounce, friction and its own inertia, so a slab lands on an edge and tips, and a splinter rolls. What they land on is
/// the level less what the blast took away (<see cref="DebrisCollision"/>), so nothing lies on a roof that is gone. How
/// many pieces, and how finely things break, follows the blast's quality (<see cref="ShatterBudget"/>): the light tier the
/// web and phones run breaks into a quarter as many, larger pieces, stepped once a tick.
/// </para>
/// <para>
/// The faces the cuts made glow hot and cool to broken concrete over a second and a half. Presentation alone: nobody
/// collides with a piece, and the holes let nobody through -- the round is over when they are made.
/// </para>
/// </remarks>
/// <param name="device">The device the pieces are uploaded to and drawn with.</param>
public sealed class BlastDebris(IGraphicsDevice device) : IDisposable
{
    // How fast the nearest piece leaves, in metres a second, for a bomb of twenty metres; scaled with the blast.
    private const float LaunchMetresPerSecond = 34f;

    // How long the cut faces burn before they are only broken concrete, in ticks.
    private const int HotTicks = 36;

    private const float UnitsPerMetre = (float)WorldUnits.PerMetre;

    // The most corners a piece's contacts are read at: its extreme points along these directions.
    private static readonly Vector3f[] CornerDirections =
    [
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
        new(1, 1, 1), new(1, 1, -1), new(1, -1, 1), new(1, -1, -1), new(-1, 1, 1), new(-1, 1, -1), new(-1, -1, 1), new(-1, -1, -1),
    ];

    private readonly List<DebrisPiece> _pieces = [];
    private Pending? _pending;
    private IShatterableWorld? _world;
    private DebrisCollision? _collision;
    private RagdollWorld? _collisionSource;
    private ShatterBudget _budget = ShatterBudget.For(2);
    private TextureHandle _concrete;
    private TextureHandle _ember;
    private int _generation;
    private bool _disposed;

    /// <summary>Gets how many pieces are in the world.</summary>
    public int PieceCount => _pieces.Count;

    /// <summary>Breaks what a blast reaches, off the frame's thread.</summary>
    /// <param name="blast">The blast.</param>
    /// <param name="scene">The level.</param>
    /// <param name="world">The world it breaks.</param>
    /// <param name="physics">What its pieces land on, or <see langword="null"/> for the ground the bomb lay on.</param>
    /// <param name="quality">The blast's quality: 1 the light tier, 2 the full one.</param>
    public void Begin(BombBlast blast, WorldScene? scene, IShatterableWorld? world, RagdollWorld? physics, int quality)
    {
        ArgumentNullException.ThrowIfNull(blast);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (scene is null || world is null)
        {
            return;
        }

        _world = world;

        _budget = ShatterBudget.For(quality);

        // Without the level's own queries -- an arena run without its soldiers' simulation -- the pieces land on the ground the
        // bomb lay on, which is flat enough for the few metres they fly.
        if (_collision is null || !ReferenceEquals(_collisionSource, physics) || physics is null)
        {
            _collision = new DebrisCollision(physics, blast.CentreMetres.Z * WorldUnits.PerMetre);
            _collisionSource = physics;
        }

        // The meshes and textures are read here, on the frame's thread, which owns the texture cache; only the geometry runs off it.
        List<ShatterSource> sources = [];
        double reach = blast.RadiusMetres * MathF.Max(BlastShatterPlanner.PropReach, BlastShatterPlanner.BreachReach);
        // The nearest first, and no more than the budget: the work a blast does is bounded wherever it runs.
        List<(WorldPlacement Placement, double Distance)> near = world.PlacementsNear(scene, blast.CentreMetres, reach);
        near.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        foreach ((WorldPlacement placement, _) in near.Take(_budget.MostPlacements))
        {
            if (world.ShatterSourceOf(placement) is { } source)
            {
                sources.Add(source);
            }
        }

        if (sources.Count == 0)
        {
            return;
        }

        int generation = _generation;
        ShatterBudget budget = _budget;
        Task<List<ShatterPlan>> work = Task.Run(() =>
        {
            List<ShatterPlan> plans = [];
            foreach (ShatterSource source in sources)
            {
                if (BlastShatterPlanner.Plan(source, blast.CentreMetres, blast.RadiusMetres, blast.Seed, budget) is { } plan)
                {
                    plans.Add(plan);
                }
            }

            return plans;
        });
        _pending = new Pending(blast, work, generation);
    }

    /// <summary>Steps the pieces one tick, and brings in the pieces of a break that has finished.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pending is { } pending && pending.Work.IsCompleted)
        {
            _pending = null;
            if (pending.Generation == _generation && pending.Work.Status == TaskStatus.RanToCompletion)
            {
                Apply(pending.Blast, pending.Work.Result);
            }
        }

        foreach (DebrisPiece piece in _pieces)
        {
            Step(piece);
        }
    }

    /// <summary>Forgets every piece and puts back everything broken, as the level is put back.</summary>
    public void Clear()
    {
        _generation++;
        _pending = null;
        foreach (DebrisPiece piece in _pieces)
        {
            device.DestroyMesh(piece.Mesh);
        }

        _pieces.Clear();
        _collision?.Clear();
        _world?.MendShattered();
    }

    /// <summary>Draws every piece into the frame's geometry, lit as the world is.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection, float blend)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pieces.Count == 0)
        {
            return;
        }

        EnsureTextures();
        device.SetProjection(projection);
        device.SetCullMode(CullMode.None);
        float t = Math.Clamp(blend, 0f, 1f);
        foreach (DebrisPiece piece in _pieces)
        {
            DebrisBody body = piece.Body;
            Vector3d units = body.PreviousPosition + ((body.Position - body.PreviousPosition) * t);
            Matrix4f modelView = camera.CreateModelView(units / WorldUnits.PerMetre, Blend(body.PreviousOrientation, body.Orientation, t));
            float heat = 1f - (piece.AgeTicks / (float)HotTicks);
            foreach (PieceDraw draw in piece.Draws)
            {
                bool interior = draw.Texture == TextureHandle.None;
                bool hot = interior && heat > 0.14f;
                device.SetMaterialOverride(hot ? new SurfaceMaterial(Roughness: 1f, Emissive: 0.1f + (1.1f * heat * heat)) : SurfaceMaterial.Default);
                device.DrawMesh(
                    piece.Mesh, draw.Offset, draw.Count, modelView, interior ? (hot ? _ember : _concrete) : draw.Texture, TextureHandle.None, BlendMode.Opaque);
            }
        }

        device.SetMaterialOverride(SurfaceMaterial.Default);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
        if (_concrete.IsValid)
        {
            device.DestroyTexture(_concrete);
        }

        if (_ember.IsValid)
        {
            device.DestroyTexture(_ember);
        }
    }

    // The finished break into the world: the placements out or holed, the pieces in, as far along as the blast.
    private void Apply(BombBlast blast, List<ShatterPlan> plans)
    {
        if (_world is not { } world || _collision is not { } collision)
        {
            return;
        }

        var random = new IgiRandom(blast.Seed ^ 0xDEB815u);
        List<(FractureChunk Chunk, ShatterPlan Plan)> all = [];
        foreach (ShatterPlan plan in plans)
        {
            WorldPlacement placement = plan.Source.Placement;
            Vector3d origin = placement.Position * WorldUnits.PerMetre;
            if (plan.Holed is { } holed)
            {
                world.Breach(placement, holed);
                Vector3f offset = placement.Orientation.Transposed().Transform(plan.HoleCentre);
                collision.AddHole(origin + (new Vector3d(offset.X, offset.Y, offset.Z) * WorldUnits.PerMetre), plan.HoleRadius * 1.1 * WorldUnits.PerMetre);
            }
            else
            {
                world.Shatter(placement);
                collision.AddGone(
                    origin,
                    placement.Orientation,
                    new Vector3d(plan.Low.X, plan.Low.Y, plan.Low.Z) * WorldUnits.PerMetre,
                    new Vector3d(plan.High.X, plan.High.Y, plan.High.Z) * WorldUnits.PerMetre);
            }

            foreach (FractureChunk chunk in plan.Pieces)
            {
                all.Add((chunk, plan));
            }
        }

        // The biggest pieces first: they are the ones kept when there are too many, and gravel is what goes.
        all.Sort(static (a, b) => b.Chunk.Volume.CompareTo(a.Chunk.Volume));
        foreach ((FractureChunk chunk, ShatterPlan plan) in all)
        {
            if (_pieces.Count >= _budget.MostPieces)
            {
                break;
            }

            if (Build(chunk, plan, blast, random) is { } piece)
            {
                _pieces.Add(piece);

                // Caught up to where the blast is: the break ran while it burned.
                for (int i = 0; i < blast.Ticks; i++)
                {
                    Step(piece);
                }
            }
        }
    }

    private DebrisPiece? Build(FractureChunk chunk, ShatterPlan plan, BombBlast blast, IgiRandom random)
    {
        // The piece's mesh, about its own centre, its faces grouped by what they are drawn with.
        List<Vertex3D> vertices = [];
        List<ushort> indices = [];
        List<PieceDraw> draws = [];
        foreach (IGrouping<int, FracturePolygon> group in chunk.Faces.GroupBy(f => f.Subset).OrderBy(g => g.Key))
        {
            int start = indices.Count;
            foreach (FracturePolygon face in group)
            {
                if (vertices.Count + face.Vertices.Count > ushort.MaxValue)
                {
                    break;
                }

                int first = vertices.Count;
                foreach (Vertex3D v in face.Vertices)
                {
                    vertices.Add(new Vertex3D(v.Position - chunk.Centre, v.Normal, v.DiffuseU, v.DiffuseV, 0f, 0f, v.Colour));
                }

                for (int i = 1; i + 1 < face.Vertices.Count; i++)
                {
                    indices.Add((ushort)first);
                    indices.Add((ushort)(first + i));
                    indices.Add((ushort)(first + i + 1));
                }
            }

            TextureHandle texture = group.Key >= 0 && group.Key < plan.Source.SubsetTextures.Length
                ? plan.Source.SubsetTextures[group.Key]
                : TextureHandle.None;
            if (indices.Count > start)
            {
                draws.Add(new PieceDraw(start, indices.Count - start, texture));
            }
        }

        if (draws.Count == 0)
        {
            return null;
        }

        MeshHandle mesh = device.CreateMesh([.. vertices], [.. indices]);
        if (!mesh.IsValid)
        {
            return null;
        }

        // Where it starts: the placement's turn and place, the piece's centre carried into the world.
        WorldPlacement placement = plan.Source.Placement;
        Matrix3f orientation = placement.Orientation;
        Vector3f offset = orientation.Transposed().Transform(chunk.Centre);
        Vector3d metres = placement.Position + new Vector3d(offset.X, offset.Y, offset.Z);
        Vector3d units = metres * WorldUnits.PerMetre;

        // Thrown away from the blast and up, the nearer and the lighter the faster.
        Vector3d away = metres - blast.CentreMetres;
        double distance = Math.Max(away.Length, 0.5);

        // A building brought down slumps: its slabs are shoved off the blast and fall, little thrown up.
        double lift = plan.Collapse ? 0.08 + (random.NextSingle() * 0.15) : 0.45 + (random.NextSingle() * 0.4);
        Vector3d direction = (away / distance) + new Vector3d(0.0, 0.0, lift);
        direction /= Math.Max(direction.Length, 1e-6);
        float near = 1f / (1f + MathF.Pow((float)distance / MathF.Max(blast.RadiusMetres * 0.35f, 1f), 2f));
        float light = Math.Clamp(1.3f - MathF.Cbrt(chunk.Volume), 0.45f, 1.5f);
        float speed = LaunchMetresPerSecond * (blast.RadiusMetres / 20f) * (0.25f + near) * light * (0.75f + (random.NextSingle() * 0.5f));
        if (plan.Collapse)
        {
            speed *= 0.35f;
        }

        Vector3d velocity = direction * (speed * UnitsPerMetre / 30.0);

        // Spin about world axes, radians a tick: a chip a few turns a second, a slab a slow roll.
        Vector3d spin = new Vector3d(random.NextSingle() - 0.5, random.NextSingle() - 0.5, random.NextSingle() - 0.5)
            * (0.5 * light * (plan.Collapse ? 0.3 : 1.0));

        return new DebrisPiece(mesh, [.. draws], new DebrisBody(Corners(vertices), units, orientation, velocity, spin));
    }

    // A piece's contact corners: its extreme points along a handful of directions, in world units.
    private static List<Vector3f> Corners(List<Vertex3D> vertices)
    {
        List<Vector3f> corners = new(CornerDirections.Length);
        foreach (Vector3f direction in CornerDirections)
        {
            float best = float.MinValue;
            Vector3f at = default;
            foreach (Vertex3D v in vertices)
            {
                float reach = Vector3f.Dot(v.Position, direction);
                if (reach > best)
                {
                    best = reach;
                    at = v.Position;
                }
            }

            Vector3f corner = at * UnitsPerMetre;
            if (!corners.Contains(corner))
            {
                corners.Add(corner);
            }
        }

        return corners;
    }

    private void Step(DebrisPiece piece)
    {
        piece.AgeTicks++;
        if (_collision is { } collision)
        {
            piece.Body.Step(collision, _budget.Substeps);
        }
    }

    // A turn between two, near enough for the few degrees a tick turns: the rows blended and squared up.
    private static Matrix3f Blend(Matrix3f a, Matrix3f b, float t)
    {
        if (t >= 0.999f)
        {
            return b;
        }

        Vector3f x = Lerp(a.Column1, b.Column1, t).Normalized();
        Vector3f y = Lerp(a.Column2, b.Column2, t);
        y = (y - (x * Vector3f.Dot(x, y))).Normalized();
        float hand = Vector3f.Dot(Vector3f.Cross(b.Column1, b.Column2), b.Column3) < 0f ? -1f : 1f;
        Vector3f z = Vector3f.Cross(x, y) * hand;
        return new Matrix3f(x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z);
    }

    private static Vector3f Lerp(Vector3f a, Vector3f b, float t) => a + ((b - a) * t);

    private void EnsureTextures()
    {
        if (!_concrete.IsValid)
        {
            _concrete = Noise(static (n, fine) =>
            {
                byte v = (byte)Math.Clamp(92f + (n * 70f) + (fine * 26f), 0f, 255f);
                return new Bgra32((byte)(v * 0.92f), (byte)(v * 0.96f), v, 255);
            });
        }

        if (!_ember.IsValid)
        {
            _ember = Noise(static (n, fine) =>
            {
                float glow = Math.Clamp(0.55f + (n * 0.6f) + (fine * 0.2f), 0f, 1f);
                return new Bgra32((byte)(30f * glow), (byte)(150f * glow * glow), (byte)(255f * glow), 255);
            });
        }
    }

    // A tiling procedural texture: two octaves of hashed value noise, wrapped at its edge.
    private TextureHandle Noise(Func<float, float, Bgra32> colour)
    {
        const int size = 64;
        var pixels = new Bgra32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float coarse = ValueNoise(x, y, 8, size) - 0.5f;
                float fine = ValueNoise(x, y, 2, size) - 0.5f;
                pixels[(y * size) + x] = colour(coarse, fine);
            }
        }

        return device.CreateTexture(new TextureDescription(size, size, 1, TextureFilter.Linear, TextureAddressMode.Repeat), pixels);
    }

    private static float ValueNoise(int x, int y, int cell, int size)
    {
        int cells = size / cell;
        float fx = x / (float)cell;
        float fy = y / (float)cell;
        int x0 = (int)fx;
        int y0 = (int)fy;
        float tx = fx - x0;
        float ty = fy - y0;
        tx = tx * tx * (3f - (2f * tx));
        ty = ty * ty * (3f - (2f * ty));
        float a = Hash(x0 % cells, y0 % cells);
        float b = Hash((x0 + 1) % cells, y0 % cells);
        float c = Hash(x0 % cells, (y0 + 1) % cells);
        float d = Hash((x0 + 1) % cells, (y0 + 1) % cells);
        return (a + ((b - a) * tx)) + (((c + ((d - c) * tx)) - (a + ((b - a) * tx))) * ty);
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = ((uint)x * 374761393u) + ((uint)y * 668265263u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    private sealed record Pending(BombBlast Blast, Task<List<ShatterPlan>> Work, int Generation);

    private readonly record struct PieceDraw(int Offset, int Count, TextureHandle Texture);

    private sealed class DebrisPiece(MeshHandle mesh, PieceDraw[] draws, DebrisBody body)
    {
        public MeshHandle Mesh { get; } = mesh;

        public PieceDraw[] Draws { get; } = draws;

        public DebrisBody Body { get; } = body;

        public int AgeTicks { get; set; }
    }
}
