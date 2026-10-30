using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// The level's list of what a human's place is told to each tick, <c>GameFunctions_ptNotifyQTaskList</c>: the fences the level
/// makes and the mines laid while it runs. A mine that goes is taken off with the last moved into its place, as
/// <c>QTaskList_Remove</c> does.
/// </summary>
public sealed class Igi2NotifyList
{
    private readonly List<IIgi2Notified> _items = [];

    /// <summary>Gets what is told, in the list's order.</summary>
    public IReadOnlyList<IIgi2Notified> Items => _items;

    /// <summary>Adds one at the end, <c>QTaskList_Register</c>.</summary>
    /// <param name="item">What is to be told.</param>
    public void Add(IIgi2Notified item) => _items.Add(item ?? throw new ArgumentNullException(nameof(item)));

    /// <summary>Takes one off, the last moved into its place.</summary>
    /// <param name="item">What is no longer told.</param>
    public void Remove(IIgi2Notified item)
    {
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        int last = _items.Count - 1;
        _items[index] = _items[last];
        _items.RemoveAt(last);
    }
}

/// <summary>What a mine needs of the level it is laid in.</summary>
public interface IIgi2MineWorld
{
    /// <summary>Gets the collision world, which its solid joins and lines are traced through.</summary>
    Igi2CollisionWorld Collision { get; }

    /// <summary>Gets the level's models.</summary>
    Func<string, MefModel?> Models { get; }

    /// <summary>Gets the explosions its blast goes off in, which also reach it, or <see langword="null"/>.</summary>
    Igi2ExplosionWorld? Explosions { get; }

    /// <summary>Gets the list humans tell their place to.</summary>
    Igi2NotifyList Notified { get; }

    /// <summary>Lays a mine: the level's dynamic task runs it from now on and draws it.</summary>
    /// <param name="mine">The mine.</param>
    void Lay(Igi2Mine mine);

    /// <summary>Takes a gone mine off the level.</summary>
    /// <param name="mine">The mine.</param>
    void Remove(Igi2Mine mine);

    /// <summary>Plays a sound where something is.</summary>
    /// <param name="sound">The sound.</param>
    /// <param name="position">Where.</param>
    void PlaySoundAt(string sound, Vector3d position);
}

/// <summary>
/// A mine laid where a proximity mine was fired, <c>mine.c</c>'s <c>Mine</c>: an <c>ExplodeObject</c> made under the level's dynamic
/// task (<c>Mine_New</c>) that arms after its time, then, the first time a human comes within its radius in front of it, counts down
/// and explodes; a bullet or a blast can set it off sooner. Once exploded it goes.
/// </summary>
public sealed class Igi2Mine : IDynamicTask, IIgi2ExplosiveParameters, IIgi2WeaponHitTarget, IIgi2ExplosionTarget, IIgi2Notified
{
    /// <summary>The script name of the type, <c>Mine</c>.</summary>
    public const string TypeName = "Mine";

    /// <summary>The sound its blast makes, <c>"explo_proxy"</c>.</summary>
    public const string BlastSound = "explo_proxy";

    /// <summary>The sound it makes as it arms, <c>"proxy_arm"</c>.</summary>
    public const string ArmSound = "proxy_arm";

    private readonly IIgi2MineWorld _world;
    private readonly Igi2Explosive _explosive;
    private readonly float[] _frame;
    private readonly Igi2RigidObject? _solid;
    private readonly ProximityMineFuse<Fpu> _fuse;

    /// <summary>
    /// Initializes a new instance of the <see cref="Igi2Mine"/> class, <c>Mine_New</c>: a blast of its radius, full damage at its
    /// heart and no falloff, five fragments and one fireball, set off at once by enough damage.
    /// </summary>
    /// <param name="world">The level.</param>
    /// <param name="position">Where it lies.</param>
    /// <param name="frame">How it is turned, row by row.</param>
    /// <param name="model">Its model, the weapon's fixed model.</param>
    /// <param name="armTicks">The ticks before it arms.</param>
    /// <param name="timeBeforeExplode">The seconds it counts down once set off.</param>
    /// <param name="detectionRadius">How near, in metres, a human sets it off.</param>
    /// <param name="explodeRadius">Its blast's full-damage radius, in metres.</param>
    /// <param name="isRemote">Whether it waits for a detonator instead of a passer-by.</param>
    public Igi2Mine(
        IIgi2MineWorld world,
        Vector3d position,
        IReadOnlyList<float> frame,
        string model,
        int armTicks,
        float timeBeforeExplode,
        float detectionRadius,
        float explodeRadius,
        bool isRemote)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(frame);
        Position = position;
        _frame = [.. frame];
        Model = model ?? string.Empty;
        TimeBeforeExplode = timeBeforeExplode;
        ExplosionRadius = explodeRadius;
        _explosive = new Igi2Explosive(this, new Igi2MineTaskName()) { Models = world.Models, Explosions = world.Explosions };
        _explosive.Reset(_frame);
        _fuse = new ProximityMineFuse<Fpu>(
            ProximityMineRules.Igi2, armTicks, Fpu.ToInt32((Fpu)TimeBeforeExplode * 30.0), detectionRadius, isRemote);
        if (world.Models(Model) is { } mesh && Igi2CollisionWorld.Meshes(mesh) is { } meshes)
        {
            _solid = new Igi2RigidObject(new Igi2CollisionOwner(TypeName, -1, Model, 0), meshes, mesh.NativeMarkerBound, isBuildingMesh: false)
            {
                Position = position,
                Handler = this,
            };
            _frame.CopyTo(_solid.Orientation, 0);
            _explosive.Bind([_solid]);
            _solid.Handler = this;
        }
    }

    /// <inheritdoc/>
    public Vector3d Position { get; }

    /// <inheritdoc/>
    public Vector3f Orientation => default;

    /// <summary>Gets its frame, row by row.</summary>
    public IReadOnlyList<float> Frame => _frame;

    /// <inheritdoc/>
    public string Model { get; }

    /// <inheritdoc/>
    public string DestroyedModel => string.Empty;

    /// <inheritdoc/>
    public float DamageScale => 1f;

    /// <inheritdoc/>
    public float ExplosionRadius { get; }

    /// <inheritdoc/>
    public float ExplosionFalloffRadius => 0f;

    /// <inheritdoc/>
    public float ExplosionDamageScale => 1f;

    /// <inheritdoc/>
    public float ExplosionDelay => 0f;

    /// <inheritdoc/>
    public int ExplosionFragments => 5;

    /// <inheritdoc/>
    public int ExplosionFireballs => 1;

    /// <inheritdoc/>
    public string ExplosionScript => string.Empty;

    /// <inheritdoc/>
    public string ExplosionSound => BlastSound;

    /// <summary>Gets the seconds it counts down once set off.</summary>
    public float TimeBeforeExplode { get; }

    /// <summary>Gets how near, in metres, a human sets it off.</summary>
    public float DetectionRadius => _fuse.ActivateRadiusMetres;

    /// <summary>Gets a value indicating whether it waits for a detonator.</summary>
    public bool IsRemote => _fuse.IsRemote;

    /// <summary>Gets a value indicating whether it has armed.</summary>
    public bool IsArmed => _fuse.IsArmed;

    /// <summary>Gets a value indicating whether something has set it off.</summary>
    public bool IsTriggered => _fuse.IsTriggered;

    /// <summary>Gets its exploding half.</summary>
    public Igi2Explosive Explosive => _explosive;

    /// <summary>Gets its solid in the collision world, or <see langword="null"/> for a model without one.</summary>
    public Igi2RigidObject? Solid => _solid;

    /// <summary>
    /// Its tick, <c>Mine_RunHandler</c>: the exploding object's first; then it arms once its time is up and nobody stands before
    /// it, sounding as it does; set off, it counts down and explodes; exploded, it goes.
    /// </summary>
    /// <returns><see langword="false"/> once it has gone.</returns>
    public bool Run()
    {
        _explosive.Run(null);
        ProximityMineStep step = _fuse.Run();
        if (step.ArmedNow)
        {
            _world.PlaySoundAt(ArmSound, Position);
        }

        if (step.Explode)
        {
            _explosive.Trigger();
        }

        if (_explosive.IsExploded)
        {
            _world.Remove(this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// A human's place, <c>Mine_NotifyHandler</c>: one within its radius (and the human's own) and in front of it is near; near
    /// and armed, a mine not yet set off is set off. A remote mine pays no heed.
    /// </summary>
    /// <param name="human">The human.</param>
    /// <param name="position">Its place, a metre up.</param>
    /// <param name="radius">Its size.</param>
    public void Notify(Igi2Human human, Vector3d position, float radius) =>
        _fuse.Sense(Position, new Vector3f(_frame[1], _frame[4], _frame[7]), position, radius);

    /// <summary>The computer's map sets a remote mine off, <c>ComputerMap_Trace</c>: it explodes nine ticks on.</summary>
    public void DetonateFromMap() => _fuse.SetOffFromMap();

    /// <summary>The ticks a mine set off from the map waits.</summary>
    public const int MapDetonationTicks = ProximityMineFuse<Fpu>.MapDetonationTicks;

    /// <summary>Gets a value indicating whether it has exploded.</summary>
    public bool IsExploded => _explosive.IsExploded;

    /// <inheritdoc/>
    public void WeaponHit(in Igi2WeaponHit hit) => _explosive.WeaponHit(hit, this);

    /// <inheritdoc/>
    public void ExplosionHit(in Igi2ExplosionHit hit) => _explosive.ExplosionHit(hit);

    // The name an exploding object's script would be compiled under; a mine has no script.
    private sealed class Igi2MineTaskName : Igi2Task
    {
    }
}
