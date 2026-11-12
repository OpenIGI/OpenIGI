using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One exact destructible object, including an attached magic-object placement when present.</summary>
public sealed class DestructibleObject : Saves.IExchangedState
{
    /// <summary>Initializes a new instance of the <see cref="DestructibleObject"/> class.</summary>
    public DestructibleObject(
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
    public Vector3d PositionUnits { get; set; }

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
    public string DestroyedModelName { get; set; } = string.Empty;

    /// <summary>Gets the authored explosion radius in engine units.</summary>
    public float ExplosionRadiusUnits { get; set; }

    /// <summary>Gets the authored explosion falloff radius in engine units.</summary>
    public float ExplosionFalloffUnits { get; set; }

    /// <summary>Gets the authored explosion damage factor.</summary>
    public float ExplosionDamageFactor { get; set; }

    /// <summary>Gets the accumulated damage required before this object detonates or shatters.</summary>
    /// <remarks>
    /// Retail <c>ExplodeObject</c> compares <c>+0x1e8</c> with <c>+0x1ec</c>; attached
    /// <c>ExplodeMagicObj</c> uses the equivalent <c>+0x13c</c>/<c>+0x140</c> pair, and so does a
    /// <c>Glass</c> pane, which is the same magic-object layout. A pane's threshold is not authored:
    /// its init (<c>0x4497F0</c>) draws it as <c>0.75 + 0.75 * rand</c>, so every pane takes a
    /// different number of rounds to break.
    /// </remarks>
    public float ExplosionDamageThreshold { get; set; } = 1f;

    /// <summary>Gets the damage accumulated by this object since it was created.</summary>
    public float ExplosionDamageAccumulated { get; set; }

    /// <summary>Gets the authored explosion sound definition.</summary>
    public string ExplosionSound { get; set; } = string.Empty;

    /// <summary>Gets the authored fireball count.</summary>
    /// <remarks>Retail stores this as a floating-point fireball scale, not an integer count.</remarks>
    public float ExplosionFireballs { get; set; }

    /// <summary>Gets the authored fragment count for the retail <c>Fragment</c> effect.</summary>
    public int ExplosionFragments { get; set; }

    /// <summary>Gets whether the retail explosion owns an <c>ExplosionSmoke</c> effect.</summary>
    /// <remarks>
    /// This is true only for an <c>ExplodeObject</c> authored by the level's <c>OBJECTS.QVM</c>.
    /// Attached magic-object families keep their own authored FireBall/Fragment recipe, but do not
    /// inherit an <c>ExplosionSmoke</c> child merely because they are explosive.
    /// </remarks>
    public bool HasExplosionSmoke { get; set; }

    /// <summary>Gets the authored explosion expression, when one gates the blast.</summary>
    public string ExplosionExpression { get; set; } = string.Empty;

    /// <summary>Gets the authored delay before the blast, in simulation ticks.</summary>
    public int ExplosionDelayTicks { get; set; }

    /// <summary>Gets whether the retail object has already been destroyed.</summary>
    public bool IsDestroyed { get; set; }

    /// <summary>Gets whether the object stays drawn and solid after it blows up.</summary>
    /// <remarks>
    /// An <c>ExplodeObject</c> that authors no <c>Destroyed model</c>: <c>0x481A60</c> only swaps the
    /// model when the name resolves, so the blast goes off and <c>isExploded</c> rises on an object
    /// that looks exactly as it did.
    /// </remarks>
    public bool KeepsModelWhenDestroyed { get; set; }

    /// <summary>Gets whether damage only arms a countdown and the object is destroyed when it runs out.</summary>
    /// <remarks>
    /// The <c>ExplodeObject</c> family (see <c>ExplodeObjectFamily</c>): a round (<c>0x481E60</c>) sets the countdown at <c>+500</c>
    /// to 0 and a blast (<c>0x481EA0</c>) to <c>Explosion delay * 30</c>; the update (<c>0x481970</c>) counts
    /// it down and explodes at 0, or at once when the Explosion expression holds. Only that explosion
    /// (<c>0x481A60</c>) swaps the model and raises <c>isExploded</c>.
    /// </remarks>
    public bool ExplodesOnCountdown { get; set; }

    /// <summary>Gets the explosion countdown, <c>ExplodeObject+500</c>: -1 idle, 0 explode on the next update.</summary>
    public int Countdown { get; set; } = -1;

    /// <summary>Gets whether the object exploded this tick and its Explosion0 goes off on the next.</summary>
    public bool BlastDue { get; set; }

    /// <summary>Gets the previous tick's destroyed state exposed as <c>isLastDestroyed</c>.</summary>
    public bool IsLastDestroyed { get; set; }

    /// <summary>Gets the object's current orientation, which places a car's explosion points.</summary>
    public Matrix3f Orientation { get; set; } = Matrix3f.Identity;

    /// <summary>Gets a car's explosion points, as magic-vertex indices of <see cref="Placement"/>'s model.</summary>
    public IReadOnlyList<int> ExplosionPoints { get; set; } = [];

    /// <summary>Gets whether a car has been hit and is blowing up — <c>Car+2332</c>.</summary>
    /// <remarks>
    /// It is not yet <see cref="IsDestroyed"/>: the car keeps its model, its collision and its script
    /// state until the countdown the explosion set (<see cref="DestructionTicks"/>) runs out.
    /// </remarks>
    public bool IsDying { get; set; }

    /// <summary>Gets a dying car's countdown to its wreck — <c>Car+2336</c>, <c>-1</c> before it starts.</summary>
    public int DestructionTicks { get; set; } = -1;

    /// <summary>Gets or sets the ticks left before a blast that has been set off goes; -1 for none.</summary>
    public int PendingExplosionTicks { get; set; }

    /// <summary>Gets or sets a value indicating whether the object's explosion has been set off.</summary>
    public bool ExplosionStarted { get; set; }

    /// <summary>Gets or sets a value indicating whether the object's explosion is due this tick.</summary>
    public bool ExplosionReady { get; set; }

    // What play changes of it, as the level stood when built: put back by a match's round.
    private Start _start;

    /// <summary>Marks the state the level starts in, to be put back by <see cref="ResetToLevelStart"/>.</summary>
    public void MarkLevelStart() => _start = new Start(
        PositionUnits, Orientation, ExplosionDamageAccumulated, IsDestroyed, Countdown, BlastDue, IsLastDestroyed, IsDying,
        DestructionTicks, PendingExplosionTicks, ExplosionStarted, ExplosionReady);

    /// <summary>Puts back the state the level started in.</summary>
    public void ResetToLevelStart()
    {
        PositionUnits = _start.PositionUnits;
        Orientation = _start.Orientation;
        ExplosionDamageAccumulated = _start.Damage;
        IsDestroyed = _start.IsDestroyed;
        Countdown = _start.Countdown;
        BlastDue = _start.BlastDue;
        IsLastDestroyed = _start.IsLastDestroyed;
        IsDying = _start.IsDying;
        DestructionTicks = _start.DestructionTicks;
        PendingExplosionTicks = _start.PendingExplosionTicks;
        ExplosionStarted = _start.ExplosionStarted;
        ExplosionReady = _start.ExplosionReady;
    }

    // Not retail: Project I.G.I. kept no save games. What a level reset puts back is what a save keeps, through the same
    // record: the level's start is set aside, the record passed through, and put back when a save is read.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange)
    {
        Start levelStart = _start;
        MarkLevelStart();
        _start = new Start(
            exchange.Position(_start.PositionUnits),
            exchange.Frame(_start.Orientation),
            exchange.Real(_start.Damage),
            exchange.Bool(_start.IsDestroyed),
            exchange.Int(_start.Countdown),
            exchange.Bool(_start.BlastDue),
            exchange.Bool(_start.IsLastDestroyed),
            exchange.Bool(_start.IsDying),
            exchange.Int(_start.DestructionTicks),
            exchange.Int(_start.PendingExplosionTicks),
            exchange.Bool(_start.ExplosionStarted),
            exchange.Bool(_start.ExplosionReady));
        if (exchange.IsReading)
        {
            ResetToLevelStart();
        }

        _start = levelStart;
    }

    private readonly record struct Start(
        Vector3d PositionUnits,
        Matrix3f Orientation,
        float Damage,
        bool IsDestroyed,
        int Countdown,
        bool BlastDue,
        bool IsLastDestroyed,
        bool IsDying,
        int DestructionTicks,
        int PendingExplosionTicks,
        bool ExplosionStarted,
        bool ExplosionReady);
}
