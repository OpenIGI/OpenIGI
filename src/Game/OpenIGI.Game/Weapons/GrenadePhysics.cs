using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Weapons;

/// <summary>The grenade's physical constants, as the retail data and code carry them.</summary>
/// <remarks>
/// <para>
/// The numbers come from two reads. <c>physicsobj/physicsobj.qvm</c>'s <c>DefinePhysicsObjType</c>
/// rows for <c>TASKTYPE_GRENADE</c> (dumped through this port's own interpreter) give the mass
/// (<c>0.3</c> kg), the half-extents (<c>0.1 × 0.1 × 0.15</c> m), a per-surface elasticity of
/// <c>0.3</c> on every one of the five collision groups, and the tail pair every entry carries. The
/// same rows' trailing arguments are the grenade class's own: the <b>explode time, 4.0 seconds</b>,
/// and the explosion type — <c>0</c> for the frag model <c>135_01_1</c>, <c>1</c> for the flashbang
/// <c>137_01_1</c>. The task init (<c>0x477040</c>) turns the seconds into ticks at thirty and
/// counts down in the per-tick (<c>0x476AC0</c>), detonating at zero.
/// </para>
/// <para>
/// The throw itself carries no constant of its own: the round's velocity is the aim direction times
/// the weapon record's <c>Muzzle Velocity</c> — fifteen metres a second for both shipped grenades —
/// exactly as every projectile spawn receives it.
/// </para>
/// </remarks>
public static class GrenadePhysics
{
    /// <summary>The procedural flight tumble used between fixed-tick contact poses.</summary>
    /// <remarks>The native solver owns the full angular state; this is only the presentation clock.</remarks>
    public const float TumbleRadiansPerTick = 0.31f;

    /// <summary>The grenade's mass, in kilograms.</summary>
    public const float MassKilograms = 0.3f;

    /// <summary>How much of the impact-normal speed survives a bounce.</summary>
    /// <remarks>The elasticity every one of the grenade's five surface groups declares.</remarks>
    public const float Restitution = 0.3f;

    /// <summary>What fraction of the surface-tangential speed survives a bounce.</summary>
    /// <remarks>
    /// <b>A chosen number, not a read one.</b> The groups carry two further unit values whose use in
    /// the solver is unread; a modest tangential loss is what keeps a grenade from skating like a
    /// puck without pretending to be the original's friction model.
    /// </remarks>
    public const float TangentialKeep = 0.75f;

    /// <summary>The grenade's vertical half-extent, in world units.</summary>
    /// <remarks><c>0.15</c> m of the physics row, times 4096.</remarks>
    public const float HalfHeightUnits = 0.15f * 4096f;

    /// <summary>The grenade's horizontal half-extent, in world units.</summary>
    /// <remarks><c>0.1</c> m of the physics row, times 4096.</remarks>
    public const float HalfWidthUnits = 0.1f * 4096f;

    /// <summary>The offset applied to a placed mine's native contact position.</summary>
    /// <remarks>
    /// The mine model <c>136_01_1</c> is authored with its contact plane at its origin. Native
    /// <c>0x475F30</c> consumes the physics task position at <c>+0x20</c> directly, unlike the thrown
    /// grenade path whose collision box needs a support projection. Keep this explicit zero so a
    /// future physics-box change cannot accidentally move mines away from walls and props again.
    /// </remarks>
    public const float MineContactOffsetUnits = 0f;

    /// <summary>The fuse, in seconds — the physics row's own trailing argument.</summary>
    public const float FuseSeconds = 4f;

    /// <summary>The fuse in ticks: seconds times the thirty-hertz rate, as <c>0x477040</c> computes it.</summary>
    public const int FuseTicks = (int)(FuseSeconds * 30f);

    /// <summary>Gravity per tick², in world units — the engine's own 9.8 m/s² object gravity.</summary>
    public const float GravityPerTick = HumanMotion.LadderSlideGravityPerTick;

    /// <summary>Below this speed on a floor-like surface the grenade comes to rest, in units/tick.</summary>
    /// <remarks>A chosen threshold (~0.3 m/s); the retail rest test is part of the unread solver.</remarks>
    public const float RestSpeedUnitsPerTick = 40.96f;

    /// <summary>How long, in ticks, a grenade must be out of contact before a bounce can sound again.</summary>
    /// <remarks>The debounce window from <c>0x00476CC0</c>: the flag clears once the tick gap exceeds ten.</remarks>
    public const int BounceQuietTicks = 10;

    /// <summary>Returns the support distance of the grenade box along a collision normal.</summary>
    /// <param name="normal">The surface normal reported by the world cast.</param>
    /// <returns>The half-extent needed to keep the physics object outside the face.</returns>
    public static float SupportExtentUnits(Vector3f normal) =>
        (MathF.Abs(normal.X) * HalfWidthUnits)
        + (MathF.Abs(normal.Y) * HalfWidthUnits)
        + (MathF.Abs(normal.Z) * HalfHeightUnits);

    /// <summary>Returns the native placement orientation for a mine or held grenade.</summary>
    /// <param name="direction">The weapon task's forward direction.</param>
    /// <remarks>
    /// <para>
    /// The retail fire bridge at <c>0x462E50</c> passes the current weapon orientation in the
    /// placement message. The mine callback at <c>0x4754C0</c> copies that message's ten orientation
    /// words into task <c>+0x70</c>; it does not derive a new pose from the contact normal.
    /// </para>
    /// <para>
    /// The native basis helper at <c>0x414A60</c> stores the supplied forward direction in matrix
    /// column two. The mine model's authored contact face is the opposite local axis, so the placed
    /// task supplies the reversed weapon direction to that helper. This is why a downward placement
    /// ray produces an upright mine instead of an upside-down one.
    /// </para>
    /// </remarks>
    public static Matrix3f PlacementOrientation(Vector3f direction)
    {
        float length = direction.Length;
        return length > float.Epsilon
            ? M203Physics.LaunchOrientation(direction / length)
            : Matrix3f.Identity;
    }

    /// <summary>Returns the same native basis for callers that name the input as a surface axis.</summary>
    /// <remarks>Retained for existing contact-pose callers; the actual placed-mine path uses the weapon direction.</remarks>
    public static Matrix3f SurfaceOrientation(Vector3f normal) => PlacementOrientation(normal);

    /// <summary>The model a thrown grenade of a kind is, which its physics type is found by.</summary>
    /// <param name="type">The kind.</param>
    /// <returns><c>135_01_1</c> for the frag, <c>137_01_1</c> for the flashbang.</returns>
    public static string ModelOf(GrenadeExplosionType type) => type == GrenadeExplosionType.Flashbang ? "137_01_1" : "135_01_1";

    /// <summary>Returns the pose used by the grenade renderer for the current fixed-tick state.</summary>
    /// <param name="grenade">The live grenade whose captured contact pose may be drawn.</param>
    /// <param name="alpha">The frame interpolation fraction, from zero to one.</param>
    /// <remarks>
    /// A settled physics object owns its orientation. Do not advance the airborne presentation clock
    /// after it has landed: that visual-only spin was the reason grenades and flashbangs kept rotating
    /// on a surface while their native task was waiting on its fuse.
    /// </remarks>
    public static Matrix3f PresentationOrientation(LiveGrenade grenade, float alpha)
    {
        ArgumentNullException.ThrowIfNull(grenade);
        return grenade.Body is not null || grenade.Resting || grenade.LandedPoseCaptured || grenade.SurfaceContact
            ? grenade.Orientation
            : FlightOrientation(grenade.TumbleTicks + alpha);
    }

    /// <summary>Places the retail mine model at the physics task's contact point.</summary>
    /// <param name="contact">The point returned by the rich world trace.</param>
    /// <param name="normal">The contacted surface normal.</param>
    /// <remarks>The current native contact offset is zero; retaining the normal parameter documents the contact basis.</remarks>
    public static Vector3d MineContactPosition(Vector3d contact, Vector3f normal) =>
        new(
            contact.X + (normal.X * MineContactOffsetUnits),
            contact.Y + (normal.Y * MineContactOffsetUnits),
            contact.Z + (normal.Z * MineContactOffsetUnits));

    /// <summary>Builds the presentation orientation for a grenade that is still airborne.</summary>
    public static Matrix3f FlightOrientation(float clock) =>
        Matrix3f.RotationY(clock * TumbleRadiansPerTick)
        * Matrix3f.FromEngineEulerAngles(clock * TumbleRadiansPerTick * 0.63f, 0f, 0f);
}

/// <summary>One detonation the simulation produced, for the owner to apply and show.</summary>
/// <param name="Position">Where the grenade was when the fuse ran out, in world units.</param>
/// <param name="Type">Which explosion this is — the blast, or the flash.</param>
/// <param name="DamageFactor">The blast's damage factor; the shipped default is 1.</param>
/// <param name="RadiusUnits">The full-damage radius; the shipped default is five metres.</param>
/// <param name="FalloffUnits">The taper past it; the shipped default is zero — a step.</param>
/// <param name="IsProximityMine">Whether this detonation came from a placed mine.</param>
public readonly record struct GrenadeDetonation(
    Vector3d Position,
    GrenadeExplosionType Type,
    float DamageFactor,
    float RadiusUnits,
    float FalloffUnits,
    bool IsProximityMine = false);

/// <summary>One grenade in the air or at rest, waiting on its fuse.</summary>
public sealed class LiveGrenade
{
    /// <summary>Initializes a new instance of the <see cref="LiveGrenade"/> class.</summary>
    /// <param name="position">Where it starts, in world units.</param>
    /// <param name="velocity">Its initial velocity, in world units per tick.</param>
    /// <param name="type">Which explosion it ends in.</param>
    /// <param name="fuseTicks">How many ticks until it does.</param>
    /// <param name="isProximityMine">Whether this object is a placed proximity mine.</param>
    /// <param name="ownerPosition">The placing actor's position.</param>
    /// <param name="radiusMetres">Optional config radius for a thrown weapon.</param>
    /// <param name="falloffMetres">Optional config falloff for a thrown weapon.</param>
    /// <param name="damageFactor">Optional config damage factor for a thrown weapon.</param>
    public LiveGrenade(
        Vector3d position,
        Vector3f velocity,
        GrenadeExplosionType type,
        int fuseTicks,
        bool isProximityMine = false,
        Vector3d ownerPosition = default,
        float? radiusMetres = null,
        float? falloffMetres = null,
        float? damageFactor = null)
    {
        Position = position;
        PreviousPosition = position;
        Velocity = velocity;
        Type = type;
        FuseTicks = fuseTicks;
        IsProximityMine = isProximityMine;
        OwnerPosition = ownerPosition;
        ArmingTicksRemaining = isProximityMine ? ProximityMinePhysics.ArmingDelayTicks : 0;
        TriggerTicksRemaining = ProximityMinePhysics.TriggerCountdownTicks;
        float defaultRadius = type == GrenadeExplosionType.Flashbang
            ? ExplosionDamage.DefaultFlashbangRadiusMetres
            : ExplosionDamage.DefaultGrenadeRadiusMetres;
        float defaultFalloff = type == GrenadeExplosionType.Flashbang
            ? ExplosionDamage.DefaultFlashbangFalloffMetres
            : ExplosionDamage.DefaultGrenadeFalloffMetres;
        float defaultDamage = type == GrenadeExplosionType.Flashbang
            ? ExplosionDamage.DefaultFlashbangDamageFactor
            : ExplosionDamage.DefaultGrenadeDamageFactor;
        RadiusUnits = isProximityMine
            ? ProximityMinePhysics.ExplosionRadiusUnits
            : ExplosionDamage.ToUnits(radiusMetres ?? defaultRadius);
        FalloffUnits = isProximityMine
            ? 0f
            : ExplosionDamage.ToUnits(falloffMetres ?? defaultFalloff);
        DamageFactor = isProximityMine
            ? ExplosionDamage.DefaultGrenadeDamageFactor
            : damageFactor ?? defaultDamage;
    }

    /// <summary>Gets or sets where the grenade is, in world units.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets where it was last tick, for frame interpolation.</summary>
    public Vector3d PreviousPosition { get; set; }

    /// <summary>Gets or sets its velocity, in world units per tick.</summary>
    public Vector3f Velocity { get; set; }

    /// <summary>Gets which explosion this grenade ends in.</summary>
    public GrenadeExplosionType Type { get; }

    /// <summary>Gets whether this physics object is a proximity mine rather than a thrown grenade.</summary>
    public bool IsProximityMine { get; }

    /// <summary>Gets the position of the actor that placed the mine.</summary>
    public Vector3d OwnerPosition { get; }

    /// <summary>Gets or sets the native mine's arming countdown.</summary>
    public int ArmingTicksRemaining { get; set; }

    /// <summary>Gets or sets the native mine's post-trigger countdown.</summary>
    public int TriggerTicksRemaining { get; set; }

    /// <summary>Gets or sets whether the mine has detected a target.</summary>
    public bool Triggered { get; set; }

    /// <summary>Gets the damage factor carried by this object.</summary>
    public float DamageFactor { get; }

    /// <summary>Gets the explosion radius carried by this object.</summary>
    public float RadiusUnits { get; }

    /// <summary>Gets the explosion falloff carried by this object.</summary>
    public float FalloffUnits { get; }

    /// <summary>Gets or sets the ticks left on the fuse.</summary>
    public int FuseTicks { get; set; }

    /// <summary>Gets or sets whether the grenade has settled and no longer integrates.</summary>
    public bool Resting { get; set; }

    /// <summary>Gets or sets whether the last physics step touched a surface.</summary>
    /// <remarks>
    /// Contact freezes the presentation pose for that step. This prevents a grenade or flashbang from
    /// visibly spinning while it is lying on a face, while the velocity solver can still produce a
    /// subsequent bounce when the impact is energetic enough.
    /// </remarks>
    public bool SurfaceContact { get; set; }

    /// <summary>Gets or sets whether the first impact pose now owns the visual model.</summary>
    /// <remarks>
    /// The native rigid body can leave the contact flag for a rebound tick, but the grenade model does
    /// not restart its airborne tumble clock from that transient flag. Capture the first impact pose
    /// once and keep drawing it until detonation; this is the visible landing behavior of IGI1.
    /// </remarks>
    public bool LandedPoseCaptured { get; set; }

    /// <summary>Gets or sets the orientation captured at the last surface contact.</summary>
    public Matrix3f Orientation { get; set; } = Matrix3f.Identity;

    /// <summary>Gets whether a proximity mine is latched to a placed object.</summary>
    /// <remarks>
    /// Retail's mine physics object remains on an object contact rather than reflecting like a thrown
    /// grenade. This flag keeps that distinction visible to the renderer and diagnostics.
    /// </remarks>
    public bool AttachedToObject { get; set; }

    /// <summary>Gets the placed task the mine latched to, when the rich trace supplied one.</summary>
    public GameTask? AttachedOwner { get; set; }

    /// <summary>Gets or sets how many ticks it has been moving — the renderer's tumble clock.</summary>
    /// <remarks>Stops with the grenade, so a settled one lies still instead of spinning in place.</remarks>
    public int TumbleTicks { get; set; }

    /// <summary>Gets or sets whether a bounce sound has already fired for the current contact.</summary>
    /// <remarks>
    /// The retail debounce flag <c>a1+1832</c> (<c>0x00476CC0</c>): set when the bounce sounds, cleared
    /// only once the grenade has been out of contact for a spell, so a grenade rolling along a floor
    /// sounds once rather than every tick it touches.
    /// </remarks>
    public bool BounceSounded { get; set; }

    /// <summary>Gets or sets how many ticks since the grenade last touched a surface.</summary>
    public int TicksSinceContact { get; set; }

    /// <summary>Gets or sets its body, when it flies as the original's physics object does; <see langword="null"/> for one that does not.</summary>
    public RetailPhysicsObjectBody? Body { get; set; }

    /// <summary>Gets or sets the tick it last bounced, which re-arms its bounce once it has been clear of every surface for ten (<c>+1828</c>).</summary>
    public int LastBounceTick { get; set; }
}

/// <summary>
/// Flies every live grenade, bounces it off the world, and detonates it when its fuse runs out.
/// </summary>
/// <remarks>
/// <para>
/// The compact port of the grenade's physics-object flight: gravity at the engine's own object
/// 9.8 m/s², one world cast per tick along the tick's travel, a reflected bounce at
/// <see cref="GrenadePhysics.Restitution"/> when it meets a surface, and rest once it is slow on
/// something floor-like. The original's full rigid-body solver (tumbling, the per-surface groups)
/// is deliberately not reproduced — what is kept is every number that decides where a grenade ends
/// up and when it goes off, which is what the game is played against.
/// </para>
/// <para>
/// Detonations land in <see cref="Detonations"/> for the owner to apply — damage through
/// <see cref="ExplosionDamage"/>, the flash through <see cref="Flashbang"/>, and whatever is drawn
/// and heard. The simulation itself hurts nobody.
/// </para>
/// </remarks>
public sealed class GrenadeSimulation
{
    /// <summary>The most grenades in the air at once; one past it is dropped.</summary>
    public const int MaximumLive = 32;

    private readonly List<LiveGrenade> _grenades = [];
    private readonly List<GrenadeDetonation> _detonations = [];
    private readonly List<Vector3d> _bounces = [];
    private readonly List<Vector3d> _thrown = [];
    private readonly List<Vector3d> _mineTriggers = [];
    private int _tick;

    /// <summary>Gets the grenades in flight or at rest.</summary>
    public IReadOnlyList<LiveGrenade> Grenades => _grenades;

    /// <summary>Gets what the last <see cref="Tick"/> detonated, for the owner to apply.</summary>
    public IReadOnlyList<GrenadeDetonation> Detonations => _detonations;

    /// <summary>Gets where grenades bounced audibly this tick, for the owner to sound and broadcast.</summary>
    /// <remarks>
    /// One entry per grenade whose bounce cleared the debounce, in world units. The owner plays
    /// <c>grenade_bnc_1</c> there and lets the AI hear it — the two things <c>0x00476CC0</c> does.
    /// </remarks>
    public IReadOnlyList<Vector3d> Bounces => _bounces;

    /// <summary>Gets grenade origins that raised IGI1's grenade-thrown event.</summary>
    /// <remarks>
    /// The task posts <c>AIEVENT_GRENADETHROWN</c> at creation, before the next AI tick. The desktop
    /// owner drains this list after the physics tick so it can deliver the event before soldiers run.
    /// Mines are placement tasks rather than thrown grenade tasks and are intentionally not included.
    /// </remarks>
    public IReadOnlyList<Vector3d> Thrown => _thrown;

    /// <summary>Gets mine positions whose proximity trigger fired during the last tick.</summary>
    /// <remarks>The retail mine task emits the <c>proxy_arm</c> cue on this rising edge.</remarks>
    public IReadOnlyList<Vector3d> MineTriggers => _mineTriggers;

    /// <summary>
    /// Gets or sets what the grenades' contact points are cast through, the world the cars step in; <see langword="null"/>
    /// flies them the simple way.
    /// </summary>
    public Func<IRetailCarWorld?>? World { get; set; }

    /// <summary>
    /// Gets or sets what makes a thrown grenade's body from its model's physics type, at its start and speed (<c>0x477040</c>);
    /// <see langword="null"/>, or no body, flies it the simple way.
    /// </summary>
    public Func<string, Vector3d, Vector3f, RetailPhysicsObjectBody?>? Bodies { get; set; }

    /// <summary>Clears grenade-thrown notifications after the owning game loop has delivered them.</summary>
    public void ClearThrownEvents() => _thrown.Clear();

    /// <summary>Clears mine-trigger notifications after the owning audio pass consumes them.</summary>
    public void ClearMineTriggerEvents() => _mineTriggers.Clear();

    /// <summary>Puts one thrown grenade into flight.</summary>
    /// <param name="origin">Where it leaves the hand, in world units.</param>
    /// <param name="direction">The throw direction, unit length.</param>
    /// <param name="speedMetresPerSecond">The weapon's muzzle velocity — fifteen for both shipped grenades.</param>
    /// <param name="type">Which explosion it ends in.</param>
    /// <param name="fuseTicks">The fuse; the retail four seconds by default.</param>
    /// <param name="radiusMetres">Optional config radius in metres.</param>
    /// <param name="falloffMetres">Optional config falloff in metres.</param>
    /// <param name="damageFactor">Optional config damage factor.</param>
    /// <returns><see langword="false"/> when the pool is full.</returns>
    public bool Spawn(
        Vector3d origin,
        Vector3f direction,
        float speedMetresPerSecond,
        GrenadeExplosionType type,
        int fuseTicks = GrenadePhysics.FuseTicks,
        float? radiusMetres = null,
        float? falloffMetres = null,
        float? damageFactor = null)
    {
        float unitsPerTick = speedMetresPerSecond * ExplosionDamage.UnitsPerMetre / 30f;
        return SpawnWithVelocity(origin, direction * unitsPerTick, type, fuseTicks, radiusMetres, falloffMetres, damageFactor);
    }

    /// <summary>Puts a grenade into flight with an explicit velocity — the AI's computed lob.</summary>
    /// <param name="origin">Where it leaves the hand.</param>
    /// <param name="velocityUnitsPerTick">The initial velocity, in world units per tick.</param>
    /// <param name="type">Which explosion it ends in.</param>
    /// <param name="fuseTicks">The fuse.</param>
    /// <param name="radiusMetres">Optional config radius in metres.</param>
    /// <param name="falloffMetres">Optional config falloff in metres.</param>
    /// <param name="damageFactor">Optional config damage factor.</param>
    /// <returns><see langword="false"/> when the pool is full.</returns>
    public bool SpawnWithVelocity(
        Vector3d origin,
        Vector3f velocityUnitsPerTick,
        GrenadeExplosionType type,
        int fuseTicks = GrenadePhysics.FuseTicks,
        float? radiusMetres = null,
        float? falloffMetres = null,
        float? damageFactor = null)
    {
        if (_grenades.Count >= MaximumLive)
        {
            return false;
        }

        _grenades.Add(new LiveGrenade(
            origin,
            velocityUnitsPerTick,
            type,
            Math.Max(0, fuseTicks),
            radiusMetres: radiusMetres,
            falloffMetres: falloffMetres,
            damageFactor: damageFactor)
        {
            Body = Bodies?.Invoke(GrenadePhysics.ModelOf(type), origin, velocityUnitsPerTick),
        });
        _thrown.Add(origin);
        return true;
    }

    /// <summary>Creates the legacy unplaced mine used by isolated physics callers.</summary>
    /// <param name="origin">The hand/eye origin, in world units.</param>
    /// <param name="ownerPosition">The placing actor's feet, used for ownership bookkeeping.</param>
    /// <param name="direction">The view direction, used for the small placement offset.</param>
    /// <returns><see langword="false"/> when the shared pool is full.</returns>
    public bool SpawnMine(Vector3d origin, Vector3d ownerPosition, Vector3f direction)
    {
        if (_grenades.Count >= MaximumLive)
        {
            return false;
        }

        Vector3d position = new(
            origin.X + (direction.X * GrenadePhysics.HalfWidthUnits),
            origin.Y + (direction.Y * GrenadePhysics.HalfWidthUnits),
            origin.Z + (direction.Z * GrenadePhysics.HalfWidthUnits));
        _grenades.Add(new LiveGrenade(
            position,
            default,
            GrenadeExplosionType.Normal,
            int.MaxValue,
            isProximityMine: true,
            ownerPosition: ownerPosition)
        {
            // Keep the legacy no-trace path on the same authored mine basis as PlaceMine. The
            // fallback is used by isolated callers, but it must not reintroduce the upside-down
            // presentation the native placement task avoids.
            Orientation = GrenadePhysics.PlacementOrientation(-direction),
        });
        return true;
    }

    /// <summary>
    /// Places a proximity mine on the first surface in the player's aim ray.
    /// </summary>
    /// <param name="origin">The hand/eye origin, in world units.</param>
    /// <param name="ownerPosition">The placing actor's feet, used for ownership bookkeeping.</param>
    /// <param name="direction">The view direction, unit length.</param>
    /// <param name="rangeInUnits">How far the placement ray may reach.</param>
    /// <param name="trace">The rich world trace that identifies the contacted surface.</param>
    /// <returns><see langword="true"/> when a mine was placed, otherwise <see langword="false"/>.</returns>
    /// <remarks>
    /// The proximity-mine task is not the grenade physics task. Retail gives it a contact pose at the
    /// aimed surface and leaves its physics velocity at zero; it never starts with a throw velocity and
    /// never falls away from the wall or prop the player selected. A terrain contact is still a valid
    /// floor placement, but object contacts retain their owner so the renderer and diagnostics can
    /// distinguish a mine stuck to a prop from one resting on terrain.
    /// </remarks>
    public bool PlaceMine(
        Vector3d origin,
        Vector3d ownerPosition,
        Vector3f direction,
        float rangeInUnits,
        WorldTraceCast? trace)
    {
        if (_grenades.Count >= MaximumLive || trace is null || rangeInUnits <= 0f)
        {
            return false;
        }

        Vector3d end = new(
            origin.X + (direction.X * rangeInUnits),
            origin.Y + (direction.Y * rangeInUnits),
            origin.Z + (direction.Z * rangeInUnits));
        if (!trace(
                new WorldTraceRequest(origin, end, WorldTraceMask.World),
                out WorldTraceHit hit))
        {
            return false;
        }

        Vector3d position = GrenadePhysics.MineContactPosition(hit.Position, hit.Normal);
        _grenades.Add(new LiveGrenade(
            position,
            default,
            GrenadeExplosionType.Normal,
            int.MaxValue,
            isProximityMine: true,
            ownerPosition: ownerPosition)
        {
            Resting = true,
            SurfaceContact = true,
            // Native 0x4754C0 receives the weapon task orientation in the placement message. The
            // trace normal positions the task but must not replace that authored orientation.
            Orientation = GrenadePhysics.PlacementOrientation(-direction),
            AttachedToObject = hit.Kind is WorldTraceHitKind.StaticObject or WorldTraceHitKind.DynamicObject,
            AttachedOwner = hit.Owner,
            TumbleTicks = 0,
            TicksSinceContact = 0,
        });
        return true;
    }

    /// <summary>Advances every grenade by one game tick.</summary>
    /// <param name="walls">What the world stops a grenade with, or <see langword="null"/> for open air.</param>
    /// <param name="humans">The living-human query used by armed proximity mines.</param>
    /// <param name="richWalls">
    /// The rich world trace used to tell object contact from terrain contact. When present it must
    /// describe the same world as <paramref name="walls"/>; the legacy cast remains the fallback for
    /// callers and for the ordinary grenade path.
    /// </param>
    public void Tick(
        WorldRayCast? walls,
        HumanProximityCast? humans = null,
        WorldTraceCast? richWalls = null)
    {
        _detonations.Clear();
        _bounces.Clear();
        // Thrown notifications are owned by the desktop loop and are deliberately not cleared here:
        // AI grenades can be spawned during SoldierRenderer.Tick, after this simulation tick, and must
        // survive until the next pre-AI delivery pass.

        _tick++;
        IRetailCarWorld? world = World?.Invoke();
        for (int i = _grenades.Count - 1; i >= 0; i--)
        {
            LiveGrenade grenade = _grenades[i];
            if (grenade.Body is { } body && world is not null)
            {
                // 0x476AC0: with fuse left it counts down and moves (0x476D90) and bounces (0x476CC0); at none it goes off.
                if (grenade.FuseTicks <= 0)
                {
                    _detonations.Add(new GrenadeDetonation(
                        grenade.Position, grenade.Type, grenade.DamageFactor, grenade.RadiusUnits, grenade.FalloffUnits));
                    _grenades.RemoveAt(i);
                    continue;
                }

                grenade.FuseTicks--;
                Fly(grenade, body, world);
                continue;
            }

            if (--grenade.FuseTicks <= 0)
            {
                // The shipped tags: factor 1, radius five metres, falloff zero -- the same literals
                // the update passes when a level declares no block, so the two paths agree.
                _detonations.Add(new GrenadeDetonation(
                    grenade.Position,
                    grenade.Type,
                    grenade.DamageFactor,
                    grenade.RadiusUnits,
                    grenade.FalloffUnits,
                    grenade.IsProximityMine));
                _grenades.RemoveAt(i);
                continue;
            }

            if (grenade.Resting)
            {
                grenade.PreviousPosition = grenade.Position;
                grenade.SurfaceContact = true;
                if (grenade.IsProximityMine && TickMine(grenade, humans))
                {
                    _detonations.Add(new GrenadeDetonation(
                        grenade.Position,
                        grenade.Type,
                        grenade.DamageFactor,
                        grenade.RadiusUnits,
                        grenade.FalloffUnits,
                        IsProximityMine: true));
                    _grenades.RemoveAt(i);
                }

                continue;
            }

            grenade.PreviousPosition = grenade.Position;
            grenade.TumbleTicks++;
            bool wasInContact = grenade.SurfaceContact;

            Vector3f velocity = new(
                grenade.Velocity.X,
                grenade.Velocity.Y,
                grenade.Velocity.Z - GrenadePhysics.GravityPerTick);

            Vector3d candidate = new(
                grenade.Position.X + velocity.X,
                grenade.Position.Y + velocity.Y,
                grenade.Position.Z + velocity.Z);

            WorldRayHit hit = default;
            WorldTraceHit richHit = default;
            bool struck = richWalls is not null
                ? richWalls(
                    new WorldTraceRequest(grenade.Position, candidate, WorldTraceMask.World),
                    out richHit)
                : walls is not null && walls(grenade.Position, candidate, out hit);
            if (struck)
            {
                if (richWalls is not null)
                {
                    hit = richHit.RayHit;
                }

                Vector3f normal = hit.Normal;

                // The native ProximityMine callback is a physics-object contact, not a grenade
                // bounce. On a placed object it stores the contact pose and remains there, which is
                // what lets an armed mine watch the object-facing side of a wall. Terrain still uses
                // the ordinary floor/rest path below.
                if (grenade.IsProximityMine
                    && richWalls is not null
                    && richHit.Kind is WorldTraceHitKind.StaticObject or WorldTraceHitKind.DynamicObject)
                {
                    grenade.Position = GrenadePhysics.MineContactPosition(hit.Position, normal);
                    grenade.Velocity = default;
                    grenade.Resting = true;
                    grenade.SurfaceContact = true;
                    grenade.AttachedToObject = true;
                    grenade.AttachedOwner = richHit.Owner;
                    grenade.BounceSounded = false;
                    grenade.TicksSinceContact = 0;

                    if (TickMine(grenade, humans))
                    {
                        _detonations.Add(new GrenadeDetonation(
                            grenade.Position,
                            grenade.Type,
                            grenade.DamageFactor,
                            grenade.RadiusUnits,
                            grenade.FalloffUnits,
                            IsProximityMine: true));
                        _grenades.RemoveAt(i);
                    }

                    continue;
                }

                float alongNormal = (velocity.X * normal.X) + (velocity.Y * normal.Y) + (velocity.Z * normal.Z);

                // Reflect the normal component at the read 0.3 elasticity; the tangential part keeps
                // most of its speed. The five-millimetre step-off is the shot loop's own, and stops
                // the next tick's cast starting inside the face it just left.
                Vector3f reflected = new(
                    (velocity.X - ((1f + GrenadePhysics.Restitution) * alongNormal * normal.X)) * GrenadePhysics.TangentialKeep,
                    (velocity.Y - ((1f + GrenadePhysics.Restitution) * alongNormal * normal.Y)) * GrenadePhysics.TangentialKeep,
                    (velocity.Z - ((1f + GrenadePhysics.Restitution) * alongNormal * normal.Z)) * GrenadePhysics.TangentialKeep);

                float support = GrenadePhysics.SupportExtentUnits(normal);
                grenade.Position = new Vector3d(
                    hit.Position.X + (normal.X * support),
                    hit.Position.Y + (normal.Y * support),
                    hit.Position.Z + (normal.Z * support));
                grenade.SurfaceContact = true;
                if (!wasInContact)
                {
                    grenade.Orientation = GrenadePhysics.FlightOrientation(grenade.TumbleTicks);
                    grenade.LandedPoseCaptured = true;
                }

                // The bounce sounds once per contact: a grenade rolling along a floor stays in contact, so
                // its flag holds until it has been clear of a surface for the debounce window.
                if (!grenade.BounceSounded)
                {
                    _bounces.Add(hit.Position);
                    grenade.BounceSounded = true;
                }

                grenade.TicksSinceContact = 0;

                float speed = MathF.Sqrt(
                    (reflected.X * reflected.X) + (reflected.Y * reflected.Y) + (reflected.Z * reflected.Z));

                if (speed < GrenadePhysics.RestSpeedUnitsPerTick && normal.Z > 0.7f)
                {
                    grenade.Resting = true;
                    grenade.Velocity = default;
                }
                else
                {
                    grenade.Velocity = reflected;
                }
            }
            else
            {
                grenade.Position = candidate;
                grenade.Velocity = velocity;
                grenade.SurfaceContact = false;

                // Out of contact this tick; once it has been clear long enough the next bounce sounds.
                if (++grenade.TicksSinceContact > GrenadePhysics.BounceQuietTicks)
                {
                    grenade.BounceSounded = false;
                }
            }

            if (grenade.IsProximityMine && TickMine(grenade, humans))
            {
                _detonations.Add(new GrenadeDetonation(
                    grenade.Position,
                    grenade.Type,
                    grenade.DamageFactor,
                    grenade.RadiusUnits,
                    grenade.FalloffUnits,
                    IsProximityMine: true));
                _grenades.RemoveAt(i);
            }
        }
    }

    /// <summary>Removes every grenade, as changing level does.</summary>
    public void Clear()
    {
        _grenades.Clear();
        _detonations.Clear();
        _bounces.Clear();
        _thrown.Clear();
        _mineTriggers.Clear();
    }

    // 0x476D90 then 0x476CC0: the body's step, then its bounce -- heard on first touching something, and heard again only
    // once it has been clear of every surface for ten ticks since.
    private void Fly(LiveGrenade grenade, RetailPhysicsObjectBody body, IRetailCarWorld world)
    {
        grenade.PreviousPosition = grenade.Position;
        body.StepGrenade(world);
        grenade.Position = new Vector3d(body.Position[0], body.Position[1], body.Position[2]);
        grenade.Velocity = new Vector3f(body.Velocity[0], body.Velocity[1], body.Velocity[2]);
        grenade.Orientation = body.Orientation();
        grenade.SurfaceContact = body.ContactCount != 0;
        grenade.Resting = false;
        if (grenade.SurfaceContact)
        {
            if (!grenade.BounceSounded)
            {
                _bounces.Add(grenade.Position);
                grenade.BounceSounded = true;
                grenade.LastBounceTick = _tick;
            }
        }
        else if (_tick - grenade.LastBounceTick > GrenadePhysics.BounceQuietTicks)
        {
            grenade.BounceSounded = false;
        }
    }

    private bool TickMine(LiveGrenade mine, HumanProximityCast? humans)
    {
        if (mine.ArmingTicksRemaining > 0)
        {
            mine.ArmingTicksRemaining--;
            return false;
        }

        if (!mine.Triggered)
        {
            if (humans is null || !humans(mine.Position, ProximityMinePhysics.ActivationRadiusUnits, out _))
            {
                return false;
            }

            mine.Triggered = true;
            mine.TriggerTicksRemaining = ProximityMinePhysics.TriggerCountdownTicks;
            _mineTriggers.Add(mine.Position);
        }

        return --mine.TriggerTicksRemaining <= 0;
    }
}
