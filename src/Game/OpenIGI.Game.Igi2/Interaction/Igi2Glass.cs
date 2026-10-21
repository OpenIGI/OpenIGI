using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>How a pane of glass broke, for its shatter to be drawn (<c>GlassShatter_NewLocalImpact</c>, <c>GlassShatter_NewExplosion</c>).</summary>
/// <param name="Glass">The pane.</param>
/// <param name="IsLocalImpact">Whether a bullet broke it, at one point, rather than a blast or a thrown object.</param>
/// <param name="Point">Where: the bullet's impact, or the blast's centre.</param>
/// <param name="Velocity">A bullet's shot vector scaled to its shards' speed; nought otherwise.</param>
/// <param name="Force">A blast's or an object's force.</param>
/// <param name="Spread">How far the shards are thrown.</param>
public readonly record struct Igi2GlassShatter(Igi2Glass Glass, bool IsLocalImpact, Vector3d Point, Vector3f Velocity, float Force, float Spread);

/// <summary>
/// A pane of glass, <c>Glass</c> (glass.c): a magic object a building's attach point makes. It gathers the damage of what strikes it
/// and, past a threshold drawn as it is made, breaks; on its next glass tick it leaves the world, so nothing stands on it, shoots
/// through it or draws it. Sight always passes it.
/// </summary>
public sealed class Igi2Glass : IIgi2WeaponHitTarget, IIgi2ExplosionTarget
{
    private readonly IReadOnlyList<Igi2CollisionMesh?> _meshes;
    private readonly float _radius;

    /// <summary>
    /// Initializes a new instance of the <see cref="Igi2Glass"/> class, <c>Glass_CreateHandler</c>: no damage, and a threshold of
    /// three quarters plus up to three quarters more, one draw of the run stream.
    /// </summary>
    /// <param name="solid">Its solid, which it handles the hits of.</param>
    /// <param name="key">Its key in the glass container.</param>
    /// <param name="model">Its model, whose render sphere sizes it for a blast.</param>
    /// <param name="random">The level's run stream.</param>
    public Igi2Glass(Igi2RigidObject solid, string key, MefModel? model, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        Solid = solid ?? throw new ArgumentNullException(nameof(solid));
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Model = model;
        _meshes = [.. Enumerable.Range(0, 8).Select(solid.Mesh).TakeWhile(static m => m is not null)];
        _radius = solid.Radius;
        Threshold = (float)(((double)(ushort)random.NextRaw() * IgiRandom.Scale * 0.75) + 0.75f);
        solid.Handler = this;
    }

    /// <summary>Gets its solid.</summary>
    public Igi2RigidObject Solid { get; }

    /// <summary>Gets its key in the glass container, <c>GlassContainer_MakeHashKey</c>.</summary>
    public string Key { get; }

    /// <summary>Gets its model.</summary>
    public MefModel? Model { get; }

    /// <summary>Gets the damage it has taken.</summary>
    public float Damage { get; private set; }

    /// <summary>Gets the damage that breaks it.</summary>
    public float Threshold { get; }

    /// <summary>Gets a value indicating whether it is broken, <c>Glass_IsBroken</c>'s second byte.</summary>
    public bool IsBroken { get; private set; }

    /// <summary>Gets a value indicating whether it has left the world, which the glass tick does once it is broken.</summary>
    public bool IsRemoved { get; private set; }

    /// <summary>Gets or sets what is told of a pane that breaks.</summary>
    public Action<Igi2GlassShatter>? Shattered { get; set; }

    /// <summary>Gets or sets the explosions it is listed with, <c>GameFunctions_ptExplosionQTaskList</c>, while it stands.</summary>
    public Igi2ExplosionWorld? Explosions { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Glass_WeaponHitHandler</c>: the bullet's damage is added, and once it reaches the threshold the pane breaks at the impact,
    /// its shards thrown along the shot at seven thousandths of its speed.
    /// </remarks>
    public void WeaponHit(in Igi2WeaponHit hit)
    {
        Damage = ((Fpu)hit.Damage + Damage).F;
        if (Damage >= Threshold && !IsBroken)
        {
            IsBroken = true;
            Vector3f shot = hit.ShotVector;
            Vector3f velocity = new(((Fpu)shot.X * 0.007000000216066837).F, ((Fpu)shot.Y * 0.007000000216066837).F, ((Fpu)shot.Z * 0.007000000216066837).F);
            Shattered?.Invoke(new Igi2GlassShatter(this, true, hit.Intersection.Position, velocity, 0f, 0f));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Glass_ExplosionHitHandler</c>: the blast is resolved at the centre of the pane's render sphere, taken through its frame, and
    /// sized by the sphere's radius; any damage at all breaks it.
    /// </remarks>
    public void ExplosionHit(in Igi2ExplosionHit hit)
    {
        if (Model is not { } model)
        {
            return;
        }

        float[] m = Solid.Orientation;
        Vector3f c = model.RenderSphereCentre;
        Fpu y = ((Fpu)m[4] * c.Y) + ((Fpu)m[3] * c.X) + ((Fpu)m[5] * c.Z);
        Fpu z = ((Fpu)m[7] * c.Y) + ((Fpu)m[6] * c.X) + ((Fpu)m[8] * c.Z);
        Fpu x = ((Fpu)m[1] * c.Y) + ((Fpu)m[2] * c.Z) + ((Fpu)c.X * m[0]);
        // Y stays on the FPU's stack; Z is kept in a float local first (igi2_frame.v8).
        Vector3d centre = new((x + Solid.Position.X).Value, (y + Solid.Position.Y).Value, ((Fpu)z.F + Solid.Position.Z).Value);
        Fpu damage = hit.Resolve(centre, model.RenderSphereRadius, this);
        Damage = (damage + Damage).F;
        if (damage.Value != 0.0 && !IsBroken)
        {
            IsBroken = true;
            Shattered?.Invoke(new Igi2GlassShatter(this, false, hit.Position, default, hit.Damage, 409.6000061035156f));
        }
    }

    /// <summary>
    /// A thrown or moving object strikes it, <c>Glass_PhysicsObjHitHandler</c>: it breaks, its damage made its threshold, its shards
    /// thrown from where the object is.
    /// </summary>
    /// <param name="at">Where the object is.</param>
    public void PhysicsHit(Vector3d at)
    {
        if (IsBroken)
        {
            return;
        }

        IsBroken = true;
        Damage = Threshold;
        Shattered?.Invoke(new Igi2GlassShatter(this, false, at, default, 12288f, 204.8000030517578f));
    }

    /// <summary>The glass tick, <c>Glass_GlassRunHandler</c>: a broken pane still in the world leaves it and the explosions' list.</summary>
    public void Run()
    {
        if (IsBroken && !IsRemoved)
        {
            Explosions?.RemoveTarget(this);
            Solid.ChangeModel([], 0f);
            IsRemoved = true;
        }
    }

    /// <summary>
    /// Sets it broken or whole as a save game kept it, <c>Glass_SetBroken</c>: broken, it leaves the world on its next tick; whole, it is
    /// put back in the world and the explosions' list if it had left them.
    /// </summary>
    /// <param name="isBroken">Whether it is broken.</param>
    public void SetBroken(bool isBroken)
    {
        if (isBroken)
        {
            IsBroken = true;
            return;
        }

        if (IsRemoved)
        {
            Explosions?.AddTarget(this);
            Solid.ChangeModel(_meshes, _radius);
            IsRemoved = false;
        }

        IsBroken = false;
    }
}
