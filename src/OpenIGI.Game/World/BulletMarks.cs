using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>One mark left on a surface by a round.</summary>
/// <param name="Position">Where it sits, in world units.</param>
/// <param name="Normal">The surface's normal, which the mark faces along.</param>
/// <param name="Texture">The material's own texture name for it.</param>
/// <param name="SizeInUnits">How wide it is, from the material.</param>
public readonly record struct BulletMark(Vector3d Position, Vector3f Normal, string Texture, float SizeInUnits);

/// <summary>
/// The marks rounds have left on the world, and the rules for adding one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The bullet does not choose its mark.</b> The shot resolution records an impact, and
/// <c>0x4083C0</c> dispatches it to the <i>material's</i> own task class — every material in the
/// retail data names <c>TASKTYPE_GUNSHOTEFFECT</c>, whose handler (<c>0x438440</c>) picks the entry or
/// exit texture and hands it, with a size, to the struck object. A round through glass and into brick
/// leaves two different marks without the shot knowing about either.
/// </para>
/// <para>
/// Two limits come straight out of the original and are what stop a firefight filling memory: a round
/// records at most <see cref="GunShotBallistics.MaximumRecordedImpacts"/> impacts however many surfaces
/// it passes through, and a material that names no texture is <b>skipped entirely</b> rather than
/// marked with a default.
/// </para>
/// </remarks>
public sealed class BulletMarks
{
    private readonly List<BulletMark> _marks = [];
    private readonly int _capacity;

    /// <summary>How many marks are kept before the oldest is dropped.</summary>
    /// <remarks>
    /// <b>A port-level limit, not the original's.</b> The original's marks belong to the struck object
    /// and live as long as it does; nothing in the port owns them per object yet, so they are kept in
    /// one list with a bound. Recorded in <c>TODO.md</c>.
    /// </remarks>
    public const int DefaultCapacity = 512;

    /// <summary>Initializes a new instance of the <see cref="BulletMarks"/> class.</summary>
    /// <param name="capacity">How many marks to keep; the oldest are dropped past it.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public BulletMarks(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    /// <summary>Gets the marks, oldest first.</summary>
    public IReadOnlyList<BulletMark> Marks => _marks;

    /// <summary>Gets how many marks have ever been added, for diagnostics.</summary>
    public int TotalAdded { get; private set; }

    /// <summary>Clears every mark, as changing level does.</summary>
    public void Clear()
    {
        _marks.Clear();
        TotalAdded = 0;
    }

    /// <summary>The shared Concrete row name retained for focused impact fixtures.</summary>
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

    /// <summary>Records what one impact leaves behind, if the material leaves anything.</summary>
    /// <param name="hit">Where the round landed, with the material its face names.</param>
    /// <param name="materials">The game material table.</param>
    /// <param name="isEntry">Whether the round was entering the surface.</param>
    /// <returns><see langword="false"/> when the material leaves no mark, or names no row.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="materials"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The material's <b>entry</b> effect is the one consulted for the guard, because that is the block
    /// the shot loop tests before it records an impact at all.
    /// </remarks>
    public bool TryAdd(in WorldRayHit hit, GameMaterialRegistry materials, bool isEntry = true)
    {
        ArgumentNullException.ThrowIfNull(materials);

        if (!TryResolve(hit, materials, out GameMaterial material) || !material.RecordsImpacts)
        {
            return false;
        }

        string texture = material.Entry.TextureFor(isEntry);
        if (string.IsNullOrEmpty(texture))
        {
            return false;
        }

        // Lift the mark clear of the surface by the same five millimetres the shot uses to step off a
        // penetrated face, so it does not fight the wall it is on for depth.
        Vector3d position = new(
            hit.Position.X + (hit.Normal.X * GunShotBallistics.PenetrationStepOffUnits),
            hit.Position.Y + (hit.Normal.Y * GunShotBallistics.PenetrationStepOffUnits),
            hit.Position.Z + (hit.Normal.Z * GunShotBallistics.PenetrationStepOffUnits));

        if (_marks.Count >= _capacity)
        {
            _marks.RemoveAt(0);
        }

        _marks.Add(new BulletMark(position, hit.Normal, texture, material.Entry.SizeInUnits));
        TotalAdded++;
        return true;
    }

    /// <summary>Records a decal directly, for an effect that names its own texture rather than a material's.</summary>
    /// <param name="position">Where it sits, in world units, already lifted off the surface.</param>
    /// <param name="normal">The surface normal it faces along.</param>
    /// <param name="texture">The decal texture name.</param>
    /// <param name="sizeInUnits">How wide it is, in world units.</param>
    /// <remarks>
    /// The retail missile impact projects <c>burn1.tex</c> (<c>dword_5C1164</c>) onto the object it hits.
    /// A body's zone material decides nothing here — the effect carries its own scorch — so this bypasses
    /// the material lookup <see cref="TryAdd"/> does for a bullet.
    /// </remarks>
    public void AddMark(Vector3d position, Vector3f normal, string texture, float sizeInUnits)
    {
        if (string.IsNullOrEmpty(texture))
        {
            return;
        }

        if (_marks.Count >= _capacity)
        {
            _marks.RemoveAt(0);
        }

        _marks.Add(new BulletMark(position, normal, texture, sizeInUnits));
        TotalAdded++;
    }

    // The face's own row, which the collision index now reports as a global material index rather
    // than a per-mesh face index. Standing Concrete in for every surface -- as this did while that
    // mapping was unread -- put a concrete hole on wood, glass and metal alike, and put one on the
    // rows that record no impact at all: Air, Water, Fence, MetalFence, MetalLadder and
    // HumanCollision each name no entry texture, and the original skips them outright.
    private static bool TryResolve(in WorldRayHit hit, GameMaterialRegistry materials, out GameMaterial material) =>
        materials.TryGet(hit.Material, out material!);
}
