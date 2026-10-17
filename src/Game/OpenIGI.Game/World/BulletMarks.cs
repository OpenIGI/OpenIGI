using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>Reports where a placement sits.</summary>
/// <param name="placement">The exact placement.</param>
/// <param name="positionMetres">Receives its origin, in metres.</param>
/// <param name="orientation">Receives its local-to-world orientation.</param>
/// <returns><see langword="false"/> when the placement's pose is not known.</returns>
public delegate bool PlacementPose(WorldPlacement placement, out Vector3d positionMetres, out Matrix3f orientation);

/// <summary>One mark left on a surface by a round, a blast or a wound.</summary>
/// <param name="Position">Where it sat when it was made, in world units.</param>
/// <param name="Normal">The surface's normal then, which the mark faces along.</param>
/// <param name="Texture">The texture name for it.</param>
/// <param name="SizeInUnits">
/// Its <b>half</b>-width. <c>0x47D920</c> lays the decal's four corners at <c>±size</c> along both of
/// its axes, so a mark is twice this wide.
/// </param>
/// <param name="Surface">
/// The object it is stuck to — a <see cref="WorldPlacement"/> or a <see cref="GameTask"/> — or
/// <see langword="null"/> for a mark nothing owns.
/// </param>
/// <param name="Anchor">
/// The placement whose frame the mark is held in, or <see langword="null"/> for a mark that stays
/// where it was made.
/// </param>
/// <param name="LocalPosition">Where it sits in <paramref name="Anchor"/>'s frame, in world units.</param>
/// <param name="LocalNormal">Its normal in <paramref name="Anchor"/>'s frame.</param>
/// <remarks>
/// The original's decal is a child task of the struck object: <c>0x47D380</c> hands its draw object
/// the parent's live position and orientation (<c>+32</c>, <c>+112</c>), and <c>0x47D920</c> keeps
/// the corners in the object's own frame. So a mark on a door swings with the door. An anchored mark
/// does the same by keeping its place in the placement's frame and asking where the placement is
/// when it is drawn.
/// </remarks>
public readonly record struct BulletMark(
    Vector3d Position,
    Vector3f Normal,
    string Texture,
    float SizeInUnits,
    object? Surface = null,
    WorldPlacement? Anchor = null,
    Vector3d LocalPosition = default,
    Vector3f LocalNormal = default)
{
    /// <summary>Works out where the mark is now.</summary>
    /// <param name="poses">Where placements are now, or <see langword="null"/> to use where it was made.</param>
    /// <param name="position">Receives its position, in world units.</param>
    /// <param name="normal">Receives its normal.</param>
    /// <param name="orientation">
    /// Receives its anchor's orientation, so a caller can carry its own local axes with it; identity
    /// for a mark that is not anchored.
    /// </param>
    /// <returns><see langword="true"/> when the anchor's pose placed it.</returns>
    public bool TryResolve(
        PlacementPose? poses,
        out Vector3d position,
        out Vector3f normal,
        out Matrix3f orientation)
    {
        if (Anchor is { } anchor
            && poses is not null
            && poses(anchor, out Vector3d originMetres, out orientation))
        {
            position = BulletMarks.ToWorld(originMetres, orientation, LocalPosition);
            normal = BulletMarks.ToWorld(orientation, LocalNormal);
            return true;
        }

        position = Position;
        normal = Normal;
        orientation = Matrix3f.Identity;
        return false;
    }
}

/// <summary>
/// The marks rounds and blasts have left on the world, and the rules for adding one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bullet does not choose its mark, and neither does the material alone.</b> The shot resolution
/// records an impact and <c>0x4083C0</c> dispatches it to the material's <c>GunShotEffect</c> handler
/// (<c>0x438440</c>). That handler picks the texture and size, then asks the <b>struck object</b> to
/// take the decal (<c>0x438508</c>). The decal only exists if that object's type answers the decal
/// message.
/// </para>
/// <para>
/// Only two base classes register a handler for it, both in <c>0x47CF70</c>:
/// <c>RigidDynCubeObj</c> (<c>0x47D380</c>), which every placed collision object descends from, and
/// <c>SplineObjSegment</c>. Several descendants register a null handler to refuse it — see
/// <see cref="RefusesMarks(GameTask?, WorldPlacement?)"/>. Terrain has no object at all: the trace's
/// terrain pass zeroes the struck task (<c>0x4CC2A0</c>), so the ground is never marked.
/// </para>
/// <para>
/// Everything else is <c>0x47D380</c>'s own bookkeeping, reproduced by <see cref="Add"/>. Marks are
/// kept per object, at most <see cref="MarksPerSurface"/> on one. At most <see cref="MaximumSurfaces"/>
/// objects carry marks at once. A mark landing on top of an existing one is skipped. A mark
/// overhanging an edge is dropped by <c>0x47D920</c>'s four-corner test.
/// </para>
/// </remarks>
public sealed class BulletMarks
{
    private readonly List<BulletMark> _marks = [];
    private readonly List<(object Key, long Stamp)> _surfaces = [];
    private readonly int _unownedCapacity;
    private long _clock;

    /// <summary>Gets or sets where placements are when a mark is made.</summary>
    /// <remarks>
    /// This must be the pose the round <b>struck</b> -- the collision index's -- so that the mark's
    /// place in the object's frame is the face it hit. Without it marks stay where they were made.
    /// </remarks>
    public PlacementPose? Poses { get; set; }

    /// <summary>How many objects may carry marks at once.</summary>
    /// <remarks>
    /// <c>0x47D03A</c> sizes the per-object decal pool at four. When a fifth object is struck,
    /// <c>0x47D3D0</c> frees the pool entry whose last mark is oldest, and every mark on it goes.
    /// </remarks>
    public const int MaximumSurfaces = 4;

    /// <summary>How many marks one object keeps before its oldest is overwritten.</summary>
    /// <remarks>The ring in <c>0x47D806</c> holds 200 and wraps.</remarks>
    public const int MarksPerSurface = 200;

    /// <summary>How close to an existing mark, in its own half-widths, a new one is refused.</summary>
    /// <remarks><c>0x47D534</c>: <c>size × 1.5</c>, compared with the distance between the two.</remarks>
    public const float OverlapFactor = 1.5f;

    /// <summary>How closely two marks' normals must agree for the nearer to refuse the newer.</summary>
    /// <remarks><c>0x47D661</c>: the dot product of the two normals against <c>0.9</c>.</remarks>
    public const float OverlapAlignment = 0.9f;

    /// <summary>How far off the surface each corner probe starts, in world units.</summary>
    /// <remarks><c>0x47DA0B</c> writes <c>12.29</c> as the corners' third coordinate.</remarks>
    public const float CornerProbeLiftInUnits = 12.28996f;

    /// <summary>How far each corner probe reaches back through the surface, in world units.</summary>
    /// <remarks><c>0x47DCA9</c> scales the normal to <c>24.576</c> for the probe's far end.</remarks>
    public const float CornerProbeDepthInUnits = 24.576f;

    /// <summary>How many unowned marks are kept before the oldest is dropped.</summary>
    /// <remarks>
    /// <b>A port-level limit, not the original's.</b> Blood splashes are still stamped from a cast that
    /// does not report which object it met, so they cannot join an object's own list yet.
    /// </remarks>
    public const int DefaultCapacity = 512;

    /// <summary>The shared Concrete row name used when a hit names a row the table does not hold.</summary>
    /// <remarks>
    /// <para>
    /// A collision face's fourth field selects a row of its own mesh's <c>TAMC</c> table. The collision
    /// resolver translates that local selector through TAMC's global-material field before it creates
    /// the <see cref="WorldRayHit"/>, so the hit's <c>Material</c> is already the shared game-material
    /// row that owns the decal effect.
    /// </para>
    /// <para>
    /// The local selector must not be used as a shared material ID here: the same selector value can
    /// mean different global materials in different meshes. The effect's texture and size still come
    /// from the resolved material row, including the material's no-mark guard.
    /// </para>
    /// </remarks>
    public const string FallbackMaterial = "Concrete";

    // The descendants of RigidDynCubeObj that register a NULL decal handler, so a round on them leaves
    // nothing: Switch (0x444D81), Wire (0x43F15C) and ElevatorFloorIndicator (0x43C7A6).
    private static readonly HashSet<string> RefusingTypes =
    [
        "Switch",
        "Wire",
        "ElevatorFloorIndicator",
    ];

    // The same for a magic object attached to a placement: the helicopter rotor (0x42D416).
    private const string RotorMagicType = "TASKTYPE_ROTOR";

    /// <summary>Initializes a new instance of the <see cref="BulletMarks"/> class.</summary>
    /// <param name="capacity">How many unowned marks to keep; the oldest are dropped past it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public BulletMarks(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _unownedCapacity = capacity;
    }

    /// <summary>Gets the marks, oldest first.</summary>
    public IReadOnlyList<BulletMark> Marks => _marks;

    /// <summary>Gets how many marks have ever been added, for diagnostics.</summary>
    public int TotalAdded { get; private set; }

    /// <summary>Gets how many objects currently carry marks.</summary>
    public int SurfaceCount => _surfaces.Count;

    /// <summary>Clears every mark, as changing level does.</summary>
    public void Clear()
    {
        _marks.Clear();
        _surfaces.Clear();
        TotalAdded = 0;
    }

    /// <summary>Removes every mark stuck to one object, as its destruction does.</summary>
    /// <param name="surface">The object — a placement or a task.</param>
    /// <remarks>
    /// The original's decals are a child task of the object they are on (<c>0x47D438</c>), so they go
    /// wherever it goes. A pane of glass that shatters takes its holes with it.
    /// </remarks>
    public void Forget(object surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _marks.RemoveAll(mark => ReferenceEquals(mark.Surface, surface));
        _surfaces.RemoveAll(entry => ReferenceEquals(entry.Key, surface));
    }

    /// <summary>Names the object a trace contact would put a mark on, or none.</summary>
    /// <param name="hit">The contact.</param>
    /// <returns>
    /// The attached placement, else the owning task, else the placement; <see langword="null"/> for
    /// terrain, a body, or a type that refuses marks.
    /// </returns>
    public static object? SurfaceOf(in WorldTraceHit hit) =>
        hit.Kind is WorldTraceHitKind.Terrain or WorldTraceHitKind.Human or WorldTraceHitKind.None
            ? null
            : SurfaceOf(hit.Owner, hit.Placement);

    /// <summary>Names the object a contact with a task and placement would put a mark on, or none.</summary>
    /// <param name="owner">The struck task.</param>
    /// <param name="placement">The struck placement.</param>
    /// <returns>The object the mark belongs to, or <see langword="null"/>.</returns>
    /// <remarks>
    /// An attached sub-mesh (a window, a barrel) is its own magic-object task in the original, so it
    /// owns its own marks. Otherwise the marks belong to the task, which is the object <c>0x4C2F08</c>
    /// writes into the trace.
    /// </remarks>
    public static object? SurfaceOf(GameTask? owner, WorldPlacement? placement)
    {
        if (RefusesMarks(owner, placement))
        {
            return null;
        }

        if (placement is { IsAttached: true })
        {
            return placement;
        }

        return (object?)owner ?? placement;
    }

    /// <summary>Reports whether a struck object's type refuses the decal message.</summary>
    /// <param name="owner">The struck task.</param>
    /// <param name="placement">The struck placement.</param>
    /// <returns><see langword="true"/> when nothing may be stuck to it.</returns>
    public static bool RefusesMarks(GameTask? owner, WorldPlacement? placement)
    {
        if (placement is { IsAttached: true })
        {
            return placement.IsRotor
                || string.Equals(placement.MagicTaskTypeName, RotorMagicType, StringComparison.Ordinal);
        }

        if (placement is { IsRotor: true })
        {
            return true;
        }

        return owner is not null && RefusingTypes.Contains(owner.Type.Name);
    }

    /// <summary>Records what one impact leaves behind, if anything.</summary>
    /// <param name="hit">Where the round landed, with the material its face names.</param>
    /// <param name="surface">The object it landed on — see <see cref="SurfaceOf(in WorldTraceHit)"/>.</param>
    /// <param name="materials">The game material table.</param>
    /// <param name="isEntry">The impact's entry flag, which picks between the block's two textures.</param>
    /// <param name="exitCrossing">
    /// Whether the round was <i>leaving</i> the surface. The march records such a crossing with the
    /// material's second block (<c>+604</c>/<c>+608</c>, <c>0x47BFDD</c>), which has its own textures and
    /// its own size.
    /// </param>
    /// <param name="probe">
    /// The world cast used for the corner test, or <see langword="null"/> to skip it.
    /// </param>
    /// <param name="placement">
    /// The placement the round struck, whose frame the mark is held in so it moves with it. A
    /// <paramref name="surface"/> that is itself a placement is used when this is omitted.
    /// </param>
    /// <returns><see langword="false"/> when nothing was left.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="materials"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// An entry is only recorded when the material's first block names an entry texture
    /// (<c>0x47BDED</c>); an exit is recorded without that guard (<c>0x47BFC1</c>). The handler then
    /// takes the block's entry texture when the flag is set and it names one, and its exit texture
    /// otherwise (<c>0x4384ED</c>).
    /// </remarks>
    public bool TryAdd(
        in WorldRayHit hit,
        object? surface,
        GameMaterialRegistry materials,
        bool isEntry = true,
        bool exitCrossing = false,
        WorldRayCast? probe = null,
        WorldPlacement? placement = null)
    {
        ArgumentNullException.ThrowIfNull(materials);

        if (surface is null || !TryResolve(hit, materials, out GameMaterial material))
        {
            return false;
        }

        if (!exitCrossing && !material.RecordsImpacts)
        {
            return false;
        }

        GunShotEffect block = exitCrossing ? material.Exit : material.Entry;
        string texture = block.TextureFor(isEntry);
        if (string.IsNullOrEmpty(texture) || !(block.SizeInUnits > 0f))
        {
            return false;
        }

        if (probe is not null && !CornersRest(hit.Position, hit.Normal, block.SizeInUnits, probe))
        {
            return false;
        }

        return Add(Anchored(new BulletMark(Lift(hit), hit.Normal, texture, block.SizeInUnits, surface), placement));
    }

    /// <summary>Records a decal that names its own texture rather than a material's.</summary>
    /// <param name="position">Where it sits, in world units, already lifted off the surface.</param>
    /// <param name="normal">The surface normal it faces along.</param>
    /// <param name="texture">The decal texture name.</param>
    /// <param name="sizeInUnits">Its half-width, in world units.</param>
    /// <param name="surface">
    /// The object it is stuck to, which subjects it to the per-object rules; <see langword="null"/>
    /// keeps it in the bounded list of marks nothing owns.
    /// </param>
    /// <param name="placement">
    /// The placement it landed on, whose frame it is held in; see <see cref="TryAdd"/>.
    /// </param>
    /// <returns><see langword="false"/> when nothing was left.</returns>
    /// <remarks>
    /// The explosion's <c>burn1.tex</c> scorch (<c>0x4811D4</c>) and a wound's blood splash
    /// (<c>0x463D1B</c>) both reach the struck object through the same decal message a bullet hole
    /// does, so they obey the same rules.
    /// </remarks>
    public bool AddMark(
        Vector3d position,
        Vector3f normal,
        string texture,
        float sizeInUnits,
        object? surface = null,
        WorldPlacement? placement = null)
    {
        if (string.IsNullOrEmpty(texture))
        {
            return false;
        }

        return Add(Anchored(new BulletMark(position, normal, texture, sizeInUnits, surface), placement));
    }

    /// <summary>Tests whether all four corners of a mark have a surface under them.</summary>
    /// <param name="position">The contact point, on the surface.</param>
    /// <param name="normal">The surface normal.</param>
    /// <param name="halfWidth">The mark's half-width.</param>
    /// <param name="probe">The world cast.</param>
    /// <returns><see langword="true"/> when every corner's probe meets a surface.</returns>
    /// <remarks>
    /// <c>0x47D920</c> lifts each corner <see cref="CornerProbeLiftInUnits"/> off the surface and casts
    /// <see cref="CornerProbeDepthInUnits"/> back through it. The sight test it uses (<c>0x4CE410</c>)
    /// reports blocked on any face at all, glass included. If any corner meets nothing, the mark hangs
    /// off an edge and is dropped (<c>0x47E0AA</c>).
    /// </remarks>
    public static bool CornersRest(Vector3d position, Vector3f normal, float halfWidth, WorldRayCast probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        Vector3f n = normal.NormalizedOr(Vector3f.UnitZ, 1e-6f);
        Vector3f reference = MathF.Abs(n.Z) < 0.9f ? new Vector3f(0f, 0f, 1f) : new Vector3f(1f, 0f, 0f);
        Vector3f right = (Vector3f.Cross(reference, n)).NormalizedOr(Vector3f.UnitZ, 1e-6f);
        Vector3f up = (Vector3f.Cross(n, right)).NormalizedOr(Vector3f.UnitZ, 1e-6f);

        for (int corner = 0; corner < 4; corner++)
        {
            float x = (corner & 1) == 0 ? halfWidth : -halfWidth;
            float y = (corner & 2) == 0 ? halfWidth : -halfWidth;
            Vector3d from = new(
                position.X + (right.X * x) + (up.X * y) + (n.X * CornerProbeLiftInUnits),
                position.Y + (right.Y * x) + (up.Y * y) + (n.Y * CornerProbeLiftInUnits),
                position.Z + (right.Z * x) + (up.Z * y) + (n.Z * CornerProbeLiftInUnits));
            Vector3d to = new(
                from.X - (n.X * CornerProbeDepthInUnits),
                from.Y - (n.Y * CornerProbeDepthInUnits),
                from.Z - (n.Z * CornerProbeDepthInUnits));

            if (!probe(from, to, out _))
            {
                return false;
            }
        }

        return true;
    }

    private bool Add(BulletMark mark)
    {
        if (mark.Surface is not { } key)
        {
            if (_marks.Count(static m => m.Surface is null) >= _unownedCapacity)
            {
                _marks.RemoveAt(_marks.FindIndex(static m => m.Surface is null));
            }

            _marks.Add(mark);
            TotalAdded++;
            return true;
        }

        // 0x47D510: a mark on top of one already on this object, facing the same way, is not added --
        // and the object's timestamp is left alone, since that is only refreshed on an add.
        int onSurface = 0;
        int oldest = -1;
        for (int i = 0; i < _marks.Count; i++)
        {
            BulletMark existing = _marks[i];
            if (!ReferenceEquals(existing.Surface, key))
            {
                continue;
            }

            onSurface++;
            if (oldest < 0)
            {
                oldest = i;
            }

            // 0x47D534 compares the two in the object's own frame, the only frame in which a mark made
            // before the object moved is still where it was made.
            bool local = existing.Anchor is not null && ReferenceEquals(existing.Anchor, mark.Anchor);
            Vector3d a = local ? existing.LocalPosition : existing.Position;
            Vector3d b = local ? mark.LocalPosition : mark.Position;
            double reach = existing.SizeInUnits * OverlapFactor;
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            if ((reach * reach) > ((dx * dx) + (dy * dy) + (dz * dz))
                && Vector3f.Dot(
                    (local ? existing.LocalNormal : existing.Normal).NormalizedOr(Vector3f.UnitZ, 1e-6f),
                    (local ? mark.LocalNormal : mark.Normal).NormalizedOr(Vector3f.UnitZ, 1e-6f)) > OverlapAlignment)
            {
                return false;
            }
        }

        int entry = _surfaces.FindIndex(e => ReferenceEquals(e.Key, key));
        if (entry < 0)
        {
            if (_surfaces.Count >= MaximumSurfaces)
            {
                int stalest = 0;
                for (int i = 1; i < _surfaces.Count; i++)
                {
                    if (_surfaces[i].Stamp < _surfaces[stalest].Stamp)
                    {
                        stalest = i;
                    }
                }

                Forget(_surfaces[stalest].Key);
            }

            _surfaces.Add((key, 0));
            entry = _surfaces.Count - 1;
        }
        else if (onSurface >= MarksPerSurface && oldest >= 0)
        {
            _marks.RemoveAt(oldest);
        }

        _surfaces[entry] = (key, ++_clock);
        _marks.Add(mark);
        TotalAdded++;
        return true;
    }

    /// <summary>Takes a point from a placement's frame into the world.</summary>
    /// <param name="originMetres">The placement's origin, in metres.</param>
    /// <param name="orientation">Its local-to-world orientation.</param>
    /// <param name="local">The point in its frame, in world units.</param>
    /// <returns>The point in the world, in world units.</returns>
    /// <remarks>
    /// The column-vector product <c>M·v</c>, the convention the renderer and the collision index share
    /// for a placement's <c>Real32x9</c>.
    /// </remarks>
    public static Vector3d ToWorld(Vector3d originMetres, Matrix3f orientation, Vector3d local) =>
        (originMetres * WorldScene.UnitsPerMetre) + orientation.Transposed().Transform(local);

    /// <summary>Turns a direction from a placement's frame into the world.</summary>
    /// <param name="orientation">The placement's local-to-world orientation.</param>
    /// <param name="local">The direction in its frame.</param>
    /// <returns>The direction in the world.</returns>
    public static Vector3f ToWorld(Matrix3f orientation, Vector3f local) =>
        orientation.Transposed().Transform(local);

    // Holds an owned mark in the frame of the placement it landed on, where that placement's pose is
    // known. The inverse of ToWorld: an orientation's transpose is its inverse, and Transform is the
    // row-vector product, which applies that transpose.
    private BulletMark Anchored(BulletMark mark, WorldPlacement? placement)
    {
        WorldPlacement? anchor = mark.Surface as WorldPlacement ?? placement;
        if (mark.Surface is null
            || anchor is null
            || Poses is not { } poses
            || !poses(anchor, out Vector3d originMetres, out Matrix3f orientation))
        {
            return mark;
        }

        Vector3d offset = mark.Position - (originMetres * WorldScene.UnitsPerMetre);
        return mark with
        {
            Anchor = anchor,
            LocalPosition = orientation.Transform(offset),
            LocalNormal = orientation.Transform(mark.Normal),
        };
    }

    // Lift the mark clear of the surface by the same five millimetres the shot uses to step off a
    // penetrated face, so it does not fight the wall it is on for depth.
    private static Vector3d Lift(in WorldRayHit hit) => new(
        hit.Position.X + (hit.Normal.X * GunShotBallistics.PenetrationStepOffUnits),
        hit.Position.Y + (hit.Normal.Y * GunShotBallistics.PenetrationStepOffUnits),
        hit.Position.Z + (hit.Normal.Z * GunShotBallistics.PenetrationStepOffUnits));

    private static bool TryResolve(in WorldRayHit hit, GameMaterialRegistry materials, out GameMaterial material)
    {
        // A collision face normally carries a valid global row. If a malformed or absent row reaches
        // this boundary, retail's shared Concrete effect is the safe authored fallback; a valid Air or
        // Water row must still win and remain mark-free through RecordsImpacts above.
        return materials.TryGet(hit.Material, out material!)
            || materials.TryGetByName(FallbackMaterial, out material);
    }

}
