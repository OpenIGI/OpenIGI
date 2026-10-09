using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>One object placed in the world: where it is, and which pack instance draws it.</summary>
/// <remarks>
/// Geometry and placement come from different files. A pack's <c>INST</c> record binds a model to its
/// textures under a name like <c>435_01_1</c>; <c>objects.qvm</c> places that name somewhere with a task
/// carrying <c>Position</c>, <c>Orientation</c> and <c>Model</c>. This is the join.
/// </remarks>
/// <param name="Position">The world position, in metres.</param>
/// <param name="Orientation">The world orientation.</param>
/// <param name="Instance">The pack instance, which names the model and its per-slot textures.</param>
/// <param name="Pack">The pack the instance belongs to.</param>
/// <param name="Model">The model's geometry.</param>
/// <param name="TaskName">The placing task's name, for diagnostics.</param>
/// <param name="Owner">The exact task instance that owns this placement.</param>
/// <param name="LightmapSet">
/// The baked-lightmap set this object belongs to — a <c>LightmapInfo</c> task's <c>Filename</c> — or an
/// empty string when nothing bakes it.
/// </param>
/// <param name="LightmapIndex">
/// The entry within <paramref name="LightmapSet"/> holding this object's bake, or <c>-1</c> when it has
/// none. Entry <c>LightmapIndex + n</c> belongs to detail level <c>n</c>; only level 0 is drawn.
/// </param>
public sealed record WorldPlacement(
    Vector3d Position,
    Matrix3f Orientation,
    MtpInstance Instance,
    LoadedPack Pack,
    MefModel Model,
    string TaskName,
    GameTask Owner,
    string LightmapSet = "",
    int LightmapIndex = -1)
{
    /// <summary>The queue an object goes in when nothing says otherwise.</summary>
    /// <remarks>
    /// Five. <c>World_InitDynCubeObj</c> (<c>igi2ed 0x412C90</c>) writes it into every object as it is
    /// created, and most never change it.
    /// </remarks>
    public const int DefaultRenderGroup = 5;

    /// <summary>Gets which of the frame's sixteen queues this placement is drawn in.</summary>
    /// <remarks>
    /// <para>
    /// The engine draws nothing directly. <c>World_Draw</c> (<c>igi2ed 0x417F50</c>) walks the world cube
    /// tree and lets the traversal <em>enqueue</em> each visible object into one of sixteen call queues;
    /// <c>QCube_CallHandlers</c> (<c>0x4171B0</c>) then empties them <b>0 to 15</b> with a shadow-volume
    /// flush after each. So the group is the object's place in the frame, and objects in different groups
    /// are separated by everything the groups between them draw.
    /// </para>
    /// <para>
    /// <b>Mostly the engine's, not the level's.</b> An author can set it — I.G.I. 2 registers
    /// <c>RenderGroup</c> as a level parameter on its rigid objects and all 24 levels use it — but the
    /// values a level ships are largely the ones the engine would have chosen anyway. Read out of the
    /// create handlers: the sky takes <b>0</b>, the sector object the camera is standing inside is forced
    /// to <b>1</b>, terrain takes <b>2</b>, a sector object <b>3</b>, everything else <b>5</b>, a vehicle
    /// <b>7</b>, a human or the first-person view <b>9</b>, a sector — and anything registered as
    /// interior to one — <b>12</b>, and water <b>14</b>.
    /// </para>
    /// <para>
    /// Project I.G.I. authors none, so every placement of that game keeps
    /// <see cref="DefaultRenderGroup"/> and the sixteen queues collapse to one — which is why a fixed
    /// pass order is not a shortcut that happens to work there, but the same behaviour.
    /// </para>
    /// </remarks>
    public int RenderGroup { get; init; } = DefaultRenderGroup;

    /// <summary>Gets the model binding drawn after this object's destructible head is shot out.</summary>
    /// <remarks>
    /// Security cameras keep their holder but replace the live <c>313_02_1</c> head with the authored
    /// <c>Camera Destroyed Model</c> (<c>313_03_1</c> in mission 1). Most placements have no replacement.
    /// </remarks>
    public WorldModelBinding? DestroyedReplacement { get; init; }

    /// <summary>Gets whether the live task has no authored replacement and should no longer draw.</summary>
    public bool IsSuppressed { get; init; }

    /// <summary>Gets pre-deformed geometry that replaces the model's own, or <see langword="null"/>.</summary>
    /// <remarks>
    /// A spline segment's vertices are bent along its waypoints' curve, so the mesh is unique to the
    /// placement rather than shared by every instance of the model. When set, the renderer uploads this
    /// under <see cref="TaskName"/> — which must then be unique — and draws it with the placement's
    /// transform as usual.
    /// </remarks>
    public MeshGeometry? BentGeometry { get; init; }

    /// <summary>Gets whether this attachment is a retail <c>TASKTYPE_ROTOR</c> object.</summary>
    /// <remarks>
    /// Rotor geometry has a draw-time phase of its own. Keeping this on the expanded placement lets the
    /// renderer spin only rotor pieces while the helicopter's other magic-object attachments remain rigid.
    /// </remarks>
    public bool IsRotor { get; init; }

    /// <summary>Gets whether this placement is an attached child of another object rather than its own task.</summary>
    /// <remarks>
    /// The world builder expands a model's children (such as a character riding a vehicle) into their own
    /// placements. Those inherit the parent's world transform, so they sit on the parent object rather than
    /// the terrain; the retail load only terrain-snaps a task's own placement, never an attached rider.
    /// </remarks>
    public bool IsAttached { get; init; }

    /// <summary>Gets the native magic-object task type for an attached placement, when one was declared.</summary>
    /// <remarks>
    /// Retail gives attached windows and explosive barrels their behaviour through <c>magicobj.qvm</c>,
    /// not through the parent task's type. Keeping the type on the expanded placement preserves the
    /// identity needed by destruction and collision code; using <see cref="Owner"/> alone would destroy
    /// every attachment owned by the same building.
    /// </remarks>
    public string MagicTaskTypeName { get; init; } = string.Empty;

    /// <summary>Gets the attached magic object's authored FireBall count, when it has one.</summary>
    public float MagicExplosionFireballs { get; init; }

    /// <summary>Gets the attached magic object's authored Fragment count, when it has one.</summary>
    public int MagicExplosionFragments { get; init; }

    /// <summary>Gets the attached magic object's authored explosion sound, when it has one.</summary>
    public string MagicExplosionSound { get; init; } = string.Empty;

    /// <summary>Gets the attached magic object's authored damaged model, when it has one.</summary>
    public string MagicDestroyedModel { get; init; } = string.Empty;

    /// <summary>Gets the attached magic object's authored explosion delay, in simulation ticks.</summary>
    public int MagicExplosionDelayTicks { get; init; }

    /// <summary>Gets the attached magic object's authored explosion damage scale.</summary>
    public float MagicExplosionDamageFactor { get; init; }

    /// <summary>Gets the attached magic object's authored explosion falloff radius, in metres.</summary>
    public float MagicExplosionFalloffMetres { get; init; }

    /// <summary>Gets the attached magic object's authored full-damage radius, in metres.</summary>
    public float MagicExplosionRadiusMetres { get; init; }

    /// <summary>Gets the attached magic object's authored damage threshold.</summary>
    public float MagicExplosionDamageThreshold { get; init; }

    /// <summary>Gets the archive entry name when the title knows it outright, rather than by formula.</summary>
    /// <remarks>
    /// The two games name the entries of the same archive differently. Project I.G.I. writes
    /// <c>&lt;set&gt;_&lt;%05d&gt;.olm</c>, which <see cref="LightmapEntryName"/> can compute from
    /// <see cref="LightmapSet"/> and <see cref="LightmapIndex"/>; I.G.I. 2 writes
    /// <c>%04d_%04d_%1d.olm</c> over a task id, a model index and a level of detail, and it reads the
    /// name out of the archive rather than rebuilding it — one of its levels registers full paths where
    /// the other 23 register bare names, so rebuilding would work on 23 levels and fail on the 24th.
    /// A title that has the name simply supplies it.
    /// </remarks>
    public string LightmapEntry { get; init; } = string.Empty;

    /// <summary>
    /// Gets the level id of the task that placed this object, shared by everything hanging off it, or <c>-1</c> for a
    /// title whose placements carry none.
    /// </summary>
    /// <remarks>What lets a title move what one of its tasks placed (<see cref="IPlacementMotion"/>).</remarks>
    public int LevelId { get; init; } = -1;

    /// <summary>Gets a value indicating whether a baked lightmap covers this object.</summary>
    public bool HasLightmap => LightmapEntry.Length > 0 || (LightmapIndex >= 0 && LightmapSet.Length > 0);

    /// <summary>The cached result of <see cref="LightmapEntryName"/>; the inputs are init-only.</summary>
    private string? _lightmapEntryName;

    /// <summary>Gets the archive entry name holding this object's bake.</summary>
    /// <returns>The entry name, or an empty string when <see cref="HasLightmap"/> is false.</returns>
    /// <remarks>
    /// Computed once and cached: the renderer asks every drawn placement for this every frame, and
    /// formatting it each time was a measurable share of the frame's garbage.
    /// </remarks>
    public string LightmapEntryName() =>
        LightmapEntry.Length > 0
            ? LightmapEntry
            : HasLightmap
                ? _lightmapEntryName ??= $"{LightmapSet}_{LightmapIndex:D5}{ObjectLightmap.Extension}"
                : string.Empty;

    /// <inheritdoc/>
    public override string ToString() => $"{Instance.Name} at {Position}";
}

/// <summary>A pack instance and the geometry it binds, without a second world transform.</summary>
/// <param name="Instance">The pack instance, including its texture bindings.</param>
/// <param name="Pack">The pack that owns the instance.</param>
/// <param name="Model">The instance's geometry.</param>
public sealed record WorldModelBinding(MtpInstance Instance, LoadedPack Pack, MefModel Model);
