using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
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
/// <param name="work">Where a break is worked out, off the frame where the scheduler runs work beside it.</param>
public sealed partial class BlastDebris(IGraphicsDevice device, IWorkScheduler work) : IDisposable
{
    // How fast the nearest piece leaves, in metres a second, for a bomb of twenty metres; scaled with the blast.
    private const float LaunchMetresPerSecond = 34f;

    // How long the cut faces burn before they are only broken concrete, in ticks.
    private const int HotTicks = 36;

    // How long something a building held up stands before it drops: with the first of the slabs, and spread over the next.
    private const int CarriedLeastHoldTicks = 4;
    private const int CarriedHoldSpreadTicks = 20;

    // How hard a slab must land, metres a second, to throw dust up; and the most dust bursts a tick, so a collapse of
    // hundreds of slabs does not flood the particles.
    private const float DustLandingMetresPerSecond = 2.5f;
    private const int MostDustBurstsATick = 8;

    private const float UnitsPerMetre = (float)WorldUnits.PerMetre;

    // The most corners a piece's contacts are read at: its extreme points along these directions.
    private static readonly Vector3f[] CornerDirections =
    [
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
        new(1, 1, 1), new(1, 1, -1), new(1, -1, 1), new(1, -1, -1), new(-1, 1, 1), new(-1, 1, -1), new(-1, -1, 1), new(-1, -1, -1),
    ];

    private readonly List<DebrisPiece> _pieces = [];
    private readonly List<CollapseCloud> _clouds = [];
    private Pending? _pending;
    private bool _catchingUp;
    private int _dustThisTick;
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

    /// <summary>Gets the dust clouds of the buildings brought down, oldest first.</summary>
    public IReadOnlyList<CollapseCloud> Clouds => _clouds;

    /// <summary>Raised where a piece throws dust up -- landing hard, or shedding it as it falls -- for the particles to show.</summary>
    /// <remarks>Where, in world units; how big the piece is, in metres; and how hard, nought to one.</remarks>
    public event Action<Vector3d, float, float>? Dust;

    /// <summary>Raised as a building starts to come down, for the particles to throw its surge out along the ground.</summary>
    /// <remarks>Its footprint's middle on the ground, in world units; how far the footprint reaches and how tall it stood, metres; its seed.</remarks>
    public event Action<Vector3d, float, float, uint>? Collapsed;

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

        // What each structure that may come down holds up, read here with the rest: whether it does come down is only known
        // once the break is worked out.
        HashSet<WorldPlacement> breaking = [.. sources.Select(static s => s.Placement)];
        Dictionary<WorldPlacement, List<(ShatterSource Source, bool Attached)>> riders = [];
        foreach (ShatterSource source in sources)
        {
            float radius = source.Geometry.Bounds.Radius;
            if (radius <= BlastShatterPlanner.PropRadiusMetres || radius > BlastShatterPlanner.BuildingRadiusMetres)
            {
                continue;
            }

            List<(ShatterSource, bool)> held = [];
            foreach ((WorldPlacement placement, _, bool attached) in world.RidersOf(scene, source.Placement, radius + 2.0))
            {
                if (!breaking.Contains(placement) && world.ShatterSourceOf(placement) is { } rider)
                {
                    held.Add((rider, attached));
                }
            }

            riders[source.Placement] = held;
        }

        int generation = _generation;
        ShatterBudget budget = _budget;
        Task<List<ShatterPlan>> planning = work.Run(() =>
        {
            List<ShatterPlan> plans = [];
            foreach (ShatterSource source in sources)
            {
                if (BlastShatterPlanner.Plan(source, blast.CentreMetres, blast.RadiusMetres, blast.Seed, budget) is { } plan)
                {
                    plans.Add(plan);
                }
            }

            // What the buildings brought down were holding up comes down with them, each thing once.
            HashSet<WorldPlacement> carried = [];
            foreach (ShatterPlan plan in plans.ToArray())
            {
                if (!plan.Collapse || !riders.TryGetValue(plan.Source.Placement, out var held))
                {
                    continue;
                }

                foreach ((ShatterSource rider, bool attached) in held)
                {
                    if (!carried.Contains(rider.Placement) && BlastShatterPlanner.PlanCarried(rider, attached, plan, blast.CentreMetres, blast.Seed, budget) is { } fall)
                    {
                        carried.Add(rider.Placement);
                        plans.Add(fall);
                    }
                }
            }

            return plans;
        });
        _pending = new Pending(blast, planning, generation);
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

        _dustThisTick = 0;
        foreach (DebrisPiece piece in _pieces)
        {
            Step(piece);
        }

        foreach (CollapseCloud cloud in _clouds)
        {
            cloud.Tick();
        }

        FoldSettled();
        TickHeaps();
    }

    /// <summary>Waits for a blast's break still being worked out, so the next tick lays its pieces in.</summary>
    /// <remarks>For a fast-forward, which ticks faster than the break would otherwise finish.</remarks>
    public void FinishBreaking()
    {
        // A break that failed is dropped by the tick, as it would be had it failed in its own time.
        if (_pending is { } pending)
        {
            ((IAsyncResult)pending.Work).AsyncWaitHandle.WaitOne();
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
        ClearSettled();
        _clouds.Clear();
        ClearHeaps();
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
        if (_pieces.Count == 0 && _heaps.Count == 0 && _settled.Count == 0)
        {
            return;
        }

        EnsureTextures();
        device.SetProjection(projection);
        device.SetCullMode(CullMode.None);
        float t = Math.Clamp(blend, 0f, 1f);
        DrawHeaps(camera, t);
        DrawSettled(camera);
        for (int index = 0; index < _pieces.Count; index++)
        {
            DebrisPiece piece = _pieces[index];
            DebrisBody body = piece.Body;
            Vector3d units = body.PreviousPosition + ((body.Position - body.PreviousPosition) * t);

            // A held slab shudders in the last moments before it lets go.
            if (body.HeldTicks is > 0 and <= 6)
            {
                units += Shudder(index, piece.AgeTicks);
            }

            Matrix4f modelView = camera.CreateModelView(units / WorldUnits.PerMetre, Blend(body.PreviousOrientation, body.Orientation, t));
            float heat = 1f - (piece.AgeTicks / (float)HotTicks);
            foreach (PieceDraw draw in piece.Draws)
            {
                bool interior = draw.Texture == TextureHandle.None;
                bool hot = interior && piece.Hot && heat > 0.14f;
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
        Dictionary<ShatterPlan, Collapse> collapses = [];
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

            // After it is out of the world, so the ground found under it is the ground and not its own floor. A shell the
            // building held comes down with it, under the building's own dust and onto its heap.
            if (plan.Collapse)
            {
                collapses[plan] = plan.Carried ? Frame(plan, blast, grounded: false) : BringDown(plan, blast, collision);
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

            if (Build(chunk, plan, blast, random, collapses.GetValueOrDefault(plan)) is { } piece)
            {
                _pieces.Add(piece);

                // Caught up to where the blast is: the break ran while it burned.
                _catchingUp = true;
                for (int i = 0; i < blast.Ticks; i++)
                {
                    Step(piece);
                }

                _catchingUp = false;
            }
        }
    }

    private DebrisPiece? Build(FractureChunk chunk, ShatterPlan plan, BombBlast blast, IgiRandom random, Collapse? collapse)
    {
        // How near the blast it was, in the mesh's space, which blackens it and decides how it goes.
        WorldPlacement source = plan.Source.Placement;
        Vector3d fromPlacement = blast.CentreMetres - source.Position;
        Vector3f blastLocal = source.Orientation.Transform(new Vector3f((float)fromPlacement.X, (float)fromPlacement.Y, (float)fromPlacement.Z));
        float toBlast = (chunk.Centre - blastLocal).Length;
        float soot = 0.3f + (0.7f * BombBlast.Smooth(blast.RadiusMetres * 0.05f, blast.RadiusMetres * 0.5f, toBlast));

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
                    vertices.Add(new Vertex3D(v.Position - chunk.Centre, v.Normal, v.DiffuseU, v.DiffuseV, 0f, 0f, Sooted(v.Colour, soot)));
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

        // Something the building held up: it stands a moment as the slabs start to go, then drops with them, tipping as it falls.
        if (plan.Carried && collapse is null)
        {
            var carried = new DebrisBody(Corners(vertices), units, orientation,
                new Vector3d((random.NextSingle() - 0.5) * 1.2, (random.NextSingle() - 0.5) * 1.2, 0.0) * (UnitsPerMetre / 30.0),
                new Vector3d(random.NextSingle() - 0.5, random.NextSingle() - 0.5, (random.NextSingle() - 0.5) * 0.4) * 0.06);
            carried.Hold(CarriedLeastHoldTicks + (int)(random.NextSingle() * CarriedHoldSpreadTicks));
            return new DebrisPiece(mesh, [.. draws], carried) { Hot = false, Vertices = [.. vertices], Indices = [.. indices] };
        }

        // A building brought down: each slab goes as its place in the building says -- blown out, dropped, toppled, or left
        // standing as a stump of wall.
        if (collapse is not null)
        {
            CollapseMotion motion = collapse.MotionOf(chunk, vertices, toBlast, blast, random);
            if (!motion.Blown)
            {
                var held = new DebrisBody(Corners(vertices), units, orientation, motion.Velocity, motion.Spin);
                held.Hold(motion.HoldTicks);
                return new DebrisPiece(mesh, [.. draws], held) { Hot = false, Shedding = motion.HoldTicks >= 0, Vertices = [.. vertices], Indices = [.. indices] };
            }
        }

        // Thrown away from the blast and up, the nearer and the lighter the faster.
        Vector3d away = metres - blast.CentreMetres;
        double distance = Math.Max(away.Length, 0.5);
        double lift = 0.45 + (random.NextSingle() * 0.4);
        Vector3d direction = (away / distance) + new Vector3d(0.0, 0.0, lift);
        direction /= Math.Max(direction.Length, 1e-6);
        float near = 1f / (1f + MathF.Pow((float)distance / MathF.Max(blast.RadiusMetres * 0.35f, 1f), 2f));
        float light = Math.Clamp(1.3f - MathF.Cbrt(chunk.Volume), 0.45f, 1.5f);
        float speed = LaunchMetresPerSecond * (blast.RadiusMetres / 20f) * (0.25f + near) * light * (0.75f + (random.NextSingle() * 0.5f));
        Vector3d velocity = direction * (speed * UnitsPerMetre / 30.0);

        // Spin about world axes, radians a tick: a chip a few turns a second, a slab a slow roll.
        Vector3d spin = new Vector3d(random.NextSingle() - 0.5, random.NextSingle() - 0.5, random.NextSingle() - 0.5) * (0.5 * light);

        return new DebrisPiece(mesh, [.. draws], new DebrisBody(Corners(vertices), units, orientation, velocity, spin))
        {
            Shedding = collapse is not null,
            Vertices = [.. vertices],
            Indices = [.. indices],
        };
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
        if (_collision is not { } collision)
        {
            return;
        }

        DebrisBody body = piece.Body;
        body.Step(collision, _budget.Substeps);
        if (_catchingUp || Dust is not { } dust || _dustThisTick >= MostDustBurstsATick || body.IsHeld || body.IsAtRest)
        {
            return;
        }

        float size = (float)(body.Radius / UnitsPerMetre);
        float landing = (float)(body.LandingSpeed * 30.0 / UnitsPerMetre);
        if (landing > DustLandingMetresPerSecond && size > 0.25f)
        {
            _dustThisTick++;
            dust(body.Position - new Vector3d(0.0, 0.0, body.Radius * 0.5), size, Math.Clamp(landing / 9f, 0.2f, 1f));
            return;
        }

        // A falling slab sheds a thread of dust behind it.
        float speed = (float)(body.Velocity.Length * 30.0 / UnitsPerMetre);
        if (piece.Shedding && speed > 2.5f && size > 0.4f && (piece.AgeTicks & 3) == 0)
        {
            _dustThisTick++;
            dust(body.Position, size * 0.5f, 0.15f);
        }
    }

    // A held slab's shake before it goes: a centimetre or two, a new way each tick.
    private static Vector3d Shudder(int index, int tick)
    {
        unchecked
        {
            uint h = ((uint)index * 2654435761u) ^ ((uint)tick * 40503u);
            h ^= h >> 15;
            h *= 2246822519u;
            double x = ((h & 0xFF) / 255.0) - 0.5;
            double y = (((h >> 8) & 0xFF) / 255.0) - 0.5;
            double z = (((h >> 16) & 0xFF) / 255.0) - 0.5;
            return new Vector3d(x, y, z * 0.5) * (0.05 * UnitsPerMetre);
        }
    }

    // A face's colour, blackened by the blast that far from it.
    private static Bgra32 Sooted(Bgra32 colour, float keep) => keep >= 0.999f
        ? colour
        : new Bgra32((byte)(colour.B * keep), (byte)(colour.G * keep), (byte)(colour.R * keep), colour.A);

    // A turn between two, near enough for the few degrees a tick turns: the rows blended and squared up.
    private static Matrix3f Blend(Matrix3f a, Matrix3f b, float t)
    {
        if (t >= 0.999f)
        {
            return b;
        }

        Vector3f x = Vector3f.Lerp(a.Column1, b.Column1, t).Normalized();
        Vector3f y = Vector3f.Lerp(a.Column2, b.Column2, t);
        y = (y - (x * Vector3f.Dot(x, y))).Normalized();
        float hand = Vector3f.Dot(Vector3f.Cross(b.Column1, b.Column2), b.Column3) < 0f ? -1f : 1f;
        Vector3f z = Vector3f.Cross(x, y) * hand;
        return new Matrix3f(x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z);
    }

    private void EnsureTextures()
    {
        if (!_concrete.IsValid)
        {
            // Broken concrete: a mottled cement, pitted, with the aggregate showing as light and dark stones.
            _concrete = Noise(static (n, fine, speck) =>
            {
                float v = 112f + (n * 64f) + (fine * 30f);
                v += speck > 0.94f ? 46f : speck < 0.05f ? -48f : 0f;
                v -= fine < -0.32f ? 22f : 0f;
                byte b = (byte)Math.Clamp(v, 0f, 255f);
                return new Bgra32((byte)(b * 0.9f), (byte)(b * 0.95f), b, 255);
            });
        }

        if (!_ember.IsValid)
        {
            _ember = Noise(static (n, fine, _) =>
            {
                float glow = Math.Clamp(0.55f + (n * 0.6f) + (fine * 0.2f), 0f, 1f);
                return new Bgra32((byte)(30f * glow), (byte)(150f * glow * glow), (byte)(255f * glow), 255);
            });
        }
    }

    // A tiling procedural texture: two octaves of hashed value noise, wrapped at its edge.
    private TextureHandle Noise(Func<float, float, float, Bgra32> colour)
    {
        const int size = 128;
        var pixels = new Bgra32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float coarse = (ValueNoise(x, y, 16, size) * 0.65f) + (ValueNoise(x, y, 8, size) * 0.35f) - 0.5f;
                float fine = ValueNoise(x, y, 2, size) - 0.5f;
                pixels[(y * size) + x] = colour(coarse, fine, Hash(x * 7, y * 13));
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

        // Whether its broken faces burn: only the ones the blast itself tore off.
        public bool Hot { get; init; } = true;

        // Whether it sheds dust as it falls: a building's.
        public bool Shedding { get; init; }

        // Its mesh as built, kept to fold it into the settled meshes once it lies still.
        public required Vertex3D[] Vertices { get; init; }

        public required ushort[] Indices { get; init; }
    }
}
