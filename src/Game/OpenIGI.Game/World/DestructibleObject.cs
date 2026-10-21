using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One exact destructible object, including an attached magic-object placement when present.</summary>
public sealed class DestructibleObject
{
    internal DestructibleObject(
        DestructibleObjectKind kind,
        GameTask owner,
        WorldPlacement? placement,
        string typeName,
        Vector3d positionUnits)
    {
        Kind = kind;
        Owner = owner;
        Placement = placement;
        TypeName = typeName;
        PositionUnits = positionUnits;
        // ExplodeMagicObj_ExplosionHitHandler (0x43c2d0) passes the target's native model bound to
        // the shared contact-dose routine (0x416c90).  FUN_004d0950 reads that value from MESH+0x78;
        // keep the raw engine-unit value on the identity rather than substituting a generic radius.
        // A loaded world placement always has geometry, but preserve the default contact radius for
        // model-less synthetic/modded task declarations just as we do for a model-less task.
        ExplosionContactRadiusUnits = placement?.Model?.NativeMarkerBound ?? 1f;
    }

    /// <summary>Gets whether this is a glass pane or an explosive object.</summary>
    public DestructibleObjectKind Kind { get; }

    /// <summary>Gets the exact task that owns the object.</summary>
    public GameTask Owner { get; }

    /// <summary>Gets the exact rendered placement, or <see langword="null"/> for a model-less task.</summary>
    public WorldPlacement? Placement { get; }

    /// <summary>Gets the native task or magic-object type name.</summary>
    public string TypeName { get; }

    /// <summary>Gets the object's world position in engine units.</summary>
    public Vector3d PositionUnits { get; internal set; }

    /// <summary>
    /// Gets the target's native contact half-extent used by <c>ExplodeMagicObj_ExplosionHitHandler</c>.
    /// </summary>
    /// <remarks>
    /// Retail reads <c>MESH+0x78</c> through <c>FUN_004d0950</c> and subtracts it in
    /// <c>FUN_00416c90</c> before evaluating blast falloff.  The value is already in raw IGI engine
    /// units, matching <see cref="PositionUnits"/>.
    /// </remarks>
    public float ExplosionContactRadiusUnits { get; }

    /// <summary>Gets the authored replacement model name, when the task provides one.</summary>
    public string DestroyedModelName { get; internal set; } = string.Empty;

    /// <summary>Gets the authored explosion radius in engine units.</summary>
    public float ExplosionRadiusUnits { get; internal set; }

    /// <summary>Gets the authored explosion falloff radius in engine units.</summary>
    public float ExplosionFalloffUnits { get; internal set; }

    /// <summary>Gets the authored explosion damage factor.</summary>
    public float ExplosionDamageFactor { get; internal set; }

    /// <summary>Gets the accumulated damage required before this object detonates or shatters.</summary>
    /// <remarks>
    /// Retail <c>ExplodeObject</c> compares <c>+0x1e8</c> with <c>+0x1ec</c>; attached
    /// <c>ExplodeMagicObj</c> uses the equivalent <c>+0x13c</c>/<c>+0x140</c> pair, and so does a
    /// <c>Glass</c> pane, which is the same magic-object layout. A pane's threshold is not authored:
    /// its init (<c>0x4497F0</c>) draws it as <c>0.75 + 0.75 * rand</c>, so every pane takes a
    /// different number of rounds to break.
    /// </remarks>
    public float ExplosionDamageThreshold { get; internal set; } = 1f;

    /// <summary>Gets the damage accumulated by this object since it was created.</summary>
    public float ExplosionDamageAccumulated { get; internal set; }

    /// <summary>Gets the authored explosion sound definition.</summary>
    public string ExplosionSound { get; internal set; } = string.Empty;

    /// <summary>Gets the authored fireball count.</summary>
    /// <remarks>Retail stores this as a floating-point fireball scale, not an integer count.</remarks>
    public float ExplosionFireballs { get; internal set; }

    /// <summary>Gets the authored fragment count for the retail <c>Fragment</c> effect.</summary>
    public int ExplosionFragments { get; internal set; }

    /// <summary>Gets whether the retail explosion owns an <c>ExplosionSmoke</c> effect.</summary>
    /// <remarks>
    /// This is true only for an <c>ExplodeObject</c> authored by the level's <c>OBJECTS.QVM</c>.
    /// Attached magic-object families keep their own authored FireBall/Fragment recipe, but do not
    /// inherit an <c>ExplosionSmoke</c> child merely because they are explosive.
    /// </remarks>
    public bool HasExplosionSmoke { get; internal set; }

    /// <summary>Gets the authored explosion expression, when one gates the blast.</summary>
    public string ExplosionExpression { get; internal set; } = string.Empty;

    /// <summary>Gets the authored delay before the blast, in simulation ticks.</summary>
    public int ExplosionDelayTicks { get; internal set; }

    /// <summary>Gets whether the retail object has already been destroyed.</summary>
    public bool IsDestroyed { get; internal set; }

    /// <summary>Gets whether the object stays drawn and solid after it blows up.</summary>
    /// <remarks>
    /// An <c>ExplodeObject</c> that authors no <c>Destroyed model</c>: <c>0x481A60</c> only swaps the
    /// model when the name resolves, so the blast goes off and <c>isExploded</c> rises on an object
    /// that looks exactly as it did.
    /// </remarks>
    public bool KeepsModelWhenDestroyed { get; internal set; }

    /// <summary>Gets whether damage only arms a countdown and the object is destroyed when it runs out.</summary>
    /// <remarks>
    /// The <c>ExplodeObject</c> family (see <c>ExplodeObjectFamily</c>): a round (<c>0x481E60</c>) sets the countdown at <c>+500</c>
    /// to 0 and a blast (<c>0x481EA0</c>) to <c>Explosion delay * 30</c>; the update (<c>0x481970</c>) counts
    /// it down and explodes at 0, or at once when the Explosion expression holds. Only that explosion
    /// (<c>0x481A60</c>) swaps the model and raises <c>isExploded</c>.
    /// </remarks>
    public bool ExplodesOnCountdown { get; internal set; }

    /// <summary>Gets the explosion countdown, <c>ExplodeObject+500</c>: -1 idle, 0 explode on the next update.</summary>
    internal int Countdown { get; set; } = -1;

    /// <summary>Gets whether the object exploded this tick and its Explosion0 goes off on the next.</summary>
    internal bool BlastDue { get; set; }

    /// <summary>Gets the previous tick's destroyed state exposed as <c>isLastDestroyed</c>.</summary>
    public bool IsLastDestroyed { get; internal set; }

    /// <summary>Gets the object's current orientation, which places a car's explosion points.</summary>
    public Matrix3f Orientation { get; internal set; } = Matrix3f.Identity;

    /// <summary>Gets a car's explosion points, as magic-vertex indices of <see cref="Placement"/>'s model.</summary>
    public IReadOnlyList<int> ExplosionPoints { get; internal set; } = [];

    /// <summary>Gets whether a car has been hit and is blowing up — <c>Car+2332</c>.</summary>
    /// <remarks>
    /// It is not yet <see cref="IsDestroyed"/>: the car keeps its model, its collision and its script
    /// state until the countdown the explosion set (<see cref="DestructionTicks"/>) runs out.
    /// </remarks>
    public bool IsDying { get; internal set; }

    /// <summary>Gets a dying car's countdown to its wreck — <c>Car+2336</c>, <c>-1</c> before it starts.</summary>
    public int DestructionTicks { get; internal set; } = -1;

    internal int PendingExplosionTicks { get; set; }

    internal bool ExplosionStarted { get; set; }

    internal bool ExplosionReady { get; set; }
}
