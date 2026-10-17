using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Weapons.Ballistics;

/// <summary>What one round's flight ended on.</summary>
/// <param name="Position">Where the round stopped, or the end of its range when it met nothing.</param>
/// <param name="StruckHuman">Whether a human took it.</param>
/// <param name="Human">The human contact, when one was struck.</param>
/// <param name="StruckWall">Whether the world stopped it.</param>
/// <param name="Wall">The world contact, when a surface stopped it.</param>
/// <param name="RangeInUnits">The weapon's whole range, carried for the impact record.</param>
/// <param name="Direction">Which way the round was travelling when it landed, unit length.</param>
/// <param name="Owner">The exact task owning the struck world placement, when available.</param>
/// <param name="Placement">The exact rendered placement struck, when available.</param>
public readonly record struct BallisticImpact(
    Vector3d Position,
    bool StruckHuman,
    HumanHit Human,
    bool StruckWall,
    WorldRayHit Wall,
    float RangeInUnits,
    Vector3f Direction = default,
    GameTask? Owner = null,
    WorldPlacement? Placement = null);

/// <summary>
/// The rounds in flight under the tactical-ballistics option, stepped once per game tick.
/// </summary>
/// <remarks>
/// <para>
/// <b>This whole class is a deliberate divergence, and it is opt-in.</b> Retail's round is hit-scan,
/// resolved entirely in the tick it is fired (<see cref="GunShotBallistics"/>); with
/// <c>g_ballistics</c> on, a round instead flies across ticks at its weapon's real muzzle velocity,
/// drops under the engine's own 9.8 m/s² (the same constant the ragdoll falls at —
/// <see cref="HumanMotion.LadderSlideGravityPerTick"/>) and slows by its profile's quadratic drag.
/// With the variable off this class is never constructed and the retail path runs untouched.
/// </para>
/// <para>
/// Velocity is integrated in <see cref="SubstepsPerTick"/> substeps for the drag's sake, but each
/// tick casts <b>one</b> segment over the whole tick's chord — the arc a round bends inside 33 ms is
/// millimetres, and one cast keeps a full magazine's worth of rounds no dearer than the hit-scan it
/// replaces. The cast uses the same two delegates the hit-scan path does, in the same both-then-nearer
/// order, so a guard in front of a wall still takes the round and the wall stays clean. Like that
/// path, the first surface stops the round — penetration and ricochet need the per-surface data the
/// collision index does not carry (see <c>GunImpact</c>'s divergence note).
/// </para>
/// </remarks>
public sealed class BallisticSimulation
{
    /// <summary>How many rounds may be in flight at once.</summary>
    /// <remarks>
    /// Not retail's fifteen (<see cref="GunShotBallistics.MaximumLiveShots"/>) — this mode is already
    /// non-retail, and a shotgun spawning one round per pellet would exhaust fifteen in two trigger
    /// pulls. A sixty-fifth is dropped the way retail drops its sixteenth.
    /// </remarks>
    public const int MaximumLiveRounds = 64;

    /// <summary>How many velocity substeps one tick integrates.</summary>
    public const int SubstepsPerTick = 4;

    /// <summary>How many ticks a dead round's trail takes to fade away.</summary>
    /// <remarks>Ninety — three seconds, over which the whole line thins, dims and shortens away.</remarks>
    public const int FadeLifetimeTicks = 90;

    /// <summary>Every how many fade ticks the oldest trail point is dropped.</summary>
    /// <remarks>
    /// Every other tick: forty-five removals over the fade against a capacity of forty-eight, so the
    /// line is still shortening visibly when the last of its brightness goes.
    /// </remarks>
    public const int FadeShrinkInterval = 2;

    /// <summary>How many past head positions a round keeps for its trail.</summary>
    /// <remarks>Forty-eight ticks is 1.6 s of path — a rifle's whole flight, a pistol's first 600 m.</remarks>
    public const int TrailCapacity = 48;

    /// <summary>Real gravity, in world units per second squared.</summary>
    /// <remarks>
    /// The engine's own 9.8 m/s² — <see cref="HumanMotion.LadderSlideGravityPerTick"/> is that value
    /// per tick², so times the tick rate squared it is per second². Not the 18.6 the player jumps
    /// under; a bullet is an object, not a platformer.
    /// </remarks>
    public const float GravityUnitsPerSecondSquared =
        HumanMotion.LadderSlideGravityPerTick * GameClock.DefaultTickRate * GameClock.DefaultTickRate;

    private readonly List<BallisticRound> _rounds = [];
    private readonly List<BallisticImpact> _impacts = [];

    /// <summary>Gets the rounds in flight and the dead ones still fading.</summary>
    public IReadOnlyList<BallisticRound> Rounds => _rounds;

    /// <summary>Gets what the last <see cref="Tick"/> resolved, for the owner to apply.</summary>
    /// <remarks>
    /// The owner replicates the hit-scan path's side-effects from these — the decal, the hit
    /// counters, the ground-impact notice. Damage needs no entry here: the
    /// <see cref="HumanShot"/> delegate applies it as it is called, exactly as it does for a ray.
    /// </remarks>
    public IReadOnlyList<BallisticImpact> Impacts => _impacts;

    /// <summary>Puts one fired projectile into flight.</summary>
    /// <param name="shot">The projectile, as <see cref="HeldGun.Fire"/> emitted it.</param>
    /// <param name="weapon">The weapon that fired it.</param>
    /// <param name="ammo">The ammunition record, when the weapon names one.</param>
    /// <param name="damageFactor">The weapon's damage factor, which the round carries.</param>
    /// <param name="category">The firing weapon's category, which decides how a killed body falls.</param>
    /// <param name="visualOffset">
    /// Where the round is drawn from relative to where it flies — the gun against the eye. Purely
    /// presentation; see <see cref="BallisticRound.VisualOffset"/>.
    /// </param>
    /// <param name="minimumFlightSeconds">
    /// The least open-air flight the round's range allows, in seconds — <c>g_ballistics_min_flight</c>.
    /// </param>
    /// <returns><see langword="false"/> when the weapon flies no projectile or the pool is full.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    public bool Spawn(
        in GunProjectile shot,
        WeaponType weapon,
        AmmoType? ammo,
        float damageFactor,
        WeaponCategory category,
        Vector3d? visualOffset = null,
        float minimumFlightSeconds = 3f)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        if (BallisticProfile.TryCreate(weapon, ammo) is not { } profile || !HasRoom())
        {
            return false;
        }

        // Guns stay on the profile speed so the Dragunov's authored 6000 m/s remains capped at
        // 1500. Charged throws (and any explicit sub-profile override) still ride the projectile.
        float launchUnitsPerSecond = profile.UnitsPerSecond;
        if (shot.SpeedMetresPerSecond > 0f
            && (weapon.Category == WeaponCategory.Grenade
                || shot.SpeedMetresPerSecond * GunShotBallistics.UnitsPerMetre < profile.UnitsPerSecond))
        {
            launchUnitsPerSecond = shot.SpeedMetresPerSecond * GunShotBallistics.UnitsPerMetre;
        }
        Vector3d velocity = new(
            shot.Direction.X * launchUnitsPerSecond,
            shot.Direction.Y * launchUnitsPerSecond,
            shot.Direction.Z * launchUnitsPerSecond);

        // The table's effective ranges were authored for the hit-scan ray and are SHORT for a
        // flying round -- an MP5's 80 m is under a fifth of a second of flight, which removed the
        // round long before a far target. The range is raised to at least this many seconds of
        // open-air flight (g_ballistics_min_flight); anything the round MEETS still stops it.
        _rounds.Add(new BallisticRound(shot.Origin, velocity, profile, weapon, ammo)
        {
            DamageFactor = damageFactor,
            Category = category,
            RemainingRange = Math.Max(
                shot.RangeInUnits,
                launchUnitsPerSecond * Math.Max(0f, minimumFlightSeconds)),
            VisualOffset = visualOffset,
        });
        return true;
    }

    /// <summary>Puts a visual-only streak into flight along an already-resolved ray.</summary>
    /// <param name="origin">The muzzle.</param>
    /// <param name="direction">The resolved ray's direction, unit length.</param>
    /// <param name="end">Where that ray was found to stop.</param>
    /// <param name="weapon">The weapon fired.</param>
    /// <param name="ammo">Its ammunition record, when it names one.</param>
    /// <returns><see langword="false"/> when the weapon flies nothing or the pool is full.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// For AI fire: the guard's round is resolved by its own combat code before this is called, so
    /// the streak neither casts nor damages. It flies the resolved line without gravity — bending
    /// would land the light away from the decal its ray already left — but keeps the drag, so the
    /// streak slows the way the player's own rounds do.
    /// </remarks>
    public bool SpawnVisual(Vector3d origin, Vector3f direction, Vector3d end, WeaponType weapon, AmmoType? ammo)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        if (BallisticProfile.TryCreate(weapon, ammo) is not { } profile || !HasRoom())
        {
            return false;
        }

        Vector3d velocity = new(
            direction.X * profile.UnitsPerSecond,
            direction.Y * profile.UnitsPerSecond,
            direction.Z * profile.UnitsPerSecond);

        _rounds.Add(new BallisticRound(origin, velocity, profile, weapon, ammo)
        {
            IsVisualOnly = true,
            VisualEndDistance = (end - origin).Length,
            RemainingRange = GunShotBallistics.RangeInUnits(weapon),
        });
        return true;
    }

    /// <summary>Advances every round by one game tick.</summary>
    /// <param name="walls">What the world stops a round with, or <see langword="null"/> for open air.</param>
    /// <param name="humans">Who a round may strike — the delegate applies the damage itself.</param>
    /// <param name="gravityScale">What fraction of real gravity to drop at (<c>g_ballistics_gravity</c>).</param>
    /// <param name="dragScale">What fraction of profile drag to fly against (<c>g_ballistics_drag</c>).</param>
    /// <param name="richWalls">The identity-preserving world trace used for live object-hit callbacks.</param>
    public void Tick(
        WorldRayCast? walls,
        HumanShot? humans,
        float gravityScale = 1f,
        float dragScale = 1f,
        WorldTraceCast? richWalls = null)
    {
        _impacts.Clear();

        const double dt = 1.0 / (GameClock.DefaultTickRate * (double)SubstepsPerTick);

        for (int i = _rounds.Count - 1; i >= 0; i--)
        {
            BallisticRound round = _rounds[i];

            if (!round.Alive)
            {
                if (round.FadeTicks % FadeShrinkInterval == 0)
                {
                    round.ShrinkTrail();
                }

                if (--round.FadeTicks <= 0)
                {
                    _rounds.RemoveAt(i);
                }

                continue;
            }

            round.PreviousPosition = round.Position;

            Vector3d start = round.Position;
            Vector3d velocity = round.Velocity;
            Vector3d position = start;

            for (int step = 0; step < SubstepsPerTick; step++)
            {
                if (!round.IsVisualOnly)
                {
                    velocity = new Vector3d(
                        velocity.X,
                        velocity.Y,
                        velocity.Z - (GravityUnitsPerSecondSquared * gravityScale * dt));
                }

                double speed = velocity.Length;
                double slow = 1.0 - (round.Profile.DragPerUnit * dragScale * speed * dt);
                velocity *= slow < 0.0 ? 0.0 : slow;

                position += velocity * dt;
            }

            round.Velocity = velocity;

            // Defence, not physics: nothing here is known to produce a non-finite number, but one
            // that slipped through would poison every quad drawn from this round. A poisoned round
            // is removed outright rather than allowed to fade.
            if (!IsFinite(position) || !IsFinite(velocity))
            {
                _rounds.RemoveAt(i);
                continue;
            }

            Vector3d chord = position - start;
            double chordLength = chord.Length;
            bool spent = false;

            if (chordLength >= round.RemainingRange && chordLength > 0.0)
            {
                position = start + (chord * (round.RemainingRange / chordLength));
                chordLength = round.RemainingRange;
                spent = true;
            }

            if (round.IsVisualOnly && round.TravelledUnits + chordLength >= round.VisualEndDistance)
            {
                double left = round.VisualEndDistance - round.TravelledUnits;
                if (chordLength > 0.0)
                {
                    position = start + (chord * (left / chordLength));
                }

                chordLength = left > 0.0 ? left : 0.0;
                spent = true;
            }

            bool stopped = spent;
            if (!round.IsVisualOnly)
            {
                // Both, then the nearer -- exactly the hit-scan path's order, so a guard in front of
                // a wall takes the round and the wall stays clean.
                WorldRayHit wallHit = default;
                WorldTraceHit richWallHit = default;
                bool struckWall = richWalls is not null
                    && richWalls(
                        new WorldTraceRequest(start, position, WorldTraceMask.World),
                        out richWallHit);
                if (struckWall)
                {
                    wallHit = richWallHit.RayHit;
                }

                // The rich trace supplies placement identity for static objects, but does not own
                // terrain.  Keep the body's world cast in the query so an object-free ground hit
                // remains a collision, and discard rich identity if terrain is the nearer surface.
                if (walls is not null
                    && walls(start, position, out WorldRayHit plainWallHit)
                    && (!struckWall || plainWallHit.Distance < wallHit.Distance))
                {
                    struckWall = true;
                    wallHit = plainWallHit;
                    richWallHit = default;
                }

                HumanHit humanHit = default;
                bool struckHuman = humans is not null
                    && humans(start, position, round.DamageFactor, round.Category, out humanHit)
                    && (!struckWall || humanHit.Distance < wallHit.Distance);

                // The round's own heading over this tick's chord, which the struck material's effect
                // needs as the axis its chips fly along.
                Vector3d step = position - start;
                double stepDistance = step.Length;
                Vector3f travel = stepDistance > 1e-9
                    ? new Vector3f(
                        (float)(step.X / stepDistance),
                        (float)(step.Y / stepDistance),
                        (float)(step.Z / stepDistance))
                    : default;

                if (struckHuman || struckWall)
                {
                    position = struckHuman ? humanHit.Position : wallHit.Position;
                    _impacts.Add(new BallisticImpact(
                        position,
                        struckHuman,
                        humanHit,
                        !struckHuman && struckWall,
                        wallHit,
                        RangeOf(round),
                        travel,
                        struckHuman ? null : richWallHit.Owner,
                        struckHuman ? null : richWallHit.Placement));
                    stopped = true;
                }
                else if (spent)
                {
                    _impacts.Add(new BallisticImpact(
                        position, false, default, false, default, RangeOf(round), travel));
                }
            }

            round.Position = position;
            round.TravelledUnits += chordLength;
            round.RemainingRange -= (float)chordLength;
            round.PushTrail(position);

            if (stopped)
            {
                round.Alive = false;
                round.FadeTicks = FadeLifetimeTicks;
            }
        }
    }

    private static float RangeOf(BallisticRound round) => GunShotBallistics.RangeInUnits(round.Weapon);

    private static bool IsFinite(Vector3d v) =>
        double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private bool HasRoom()
    {
        int alive = 0;
        foreach (BallisticRound round in _rounds)
        {
            if (round.Alive)
            {
                alive++;
            }
        }

        return alive < MaximumLiveRounds;
    }
}
