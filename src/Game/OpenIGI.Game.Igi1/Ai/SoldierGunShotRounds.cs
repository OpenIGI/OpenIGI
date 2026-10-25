using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The soldiers' rounds in flight: each gun's <c>GunShot</c> task, the march through the world after the level's walk,
/// the humans a round passes, and the soldier skins a round crosses.
/// </summary>
/// <remarks>
/// With a world set, a round leaves the gun in the soldier's update but is cast only when the tasks tick, after the
/// level's walk; without one, an AI round is cast as it leaves (<see cref="TryShootAiRound"/>).
/// </remarks>
/// <param name="context">The soldiers, their brains, health and the level's stream.</param>
/// <param name="sight">The eyes a passing round rebuilds.</param>
/// <param name="noise">What the soldiers hear of a round going by.</param>
internal sealed class SoldierGunShotRounds(SoldierContext context, SoldierSightQueries sight, SoldierNoiseBroadcaster noise)
{
    private readonly SoldierContext _context = context;

    private readonly SoldierSightQueries _sight = sight;

    private readonly SoldierNoiseBroadcaster _noise = noise;

    // Each soldier's gun's GunShot task, in the order the guns were made: the order the level's dynamic container
    // ticks them in.
    private readonly Dictionary<SoldierView, GunShotTask> _gunShots = [];
    private readonly List<(SoldierView View, GunShotTask Task)> _gunShotOrder = [];
    private IGunShotWorld? _gunShotWorld;
    private GunShotSoundWindows? _gunShotSounds;
    private Action? _afterGunShots;

    /// <summary>Gets each soldier's gun-shot task, by the soldier that fires it.</summary>
    public IReadOnlyDictionary<SoldierView, GunShotTask> GunShots => _gunShots;

    /// <summary>Gets whether rounds are marched by the soldiers' <c>GunShot</c> tasks rather than cast as they leave.</summary>
    public bool MarchesRounds => _gunShotWorld is not null && _gunShotSounds is not null;

    /// <summary>Gets every soldier gun's live tracers, in the order the guns were made, for the tracer draw.</summary>
    public IEnumerable<GunShotTracer> GunShotTracers
    {
        get
        {
            foreach ((SoldierView _, GunShotTask task) in _gunShotOrder)
            {
                foreach (GunShotTracer tracer in task.Tracers)
                {
                    yield return tracer;
                }
            }
        }
    }

    /// <summary>Marches the soldiers' rounds through a world, as their guns' <c>GunShot</c> tasks do.</summary>
    /// <param name="world">The world the rounds are cast through.</param>
    /// <param name="sounds">The impact sound windows every gun shares.</param>
    /// <param name="afterMarch">Runs once every task has marched: the chips the impacts placed detonate.</param>
    public void SetGunShotWorld(IGunShotWorld? world, GunShotSoundWindows? sounds, Action? afterMarch)
    {
        _gunShotWorld = world;
        _gunShotSounds = sounds;
        _afterGunShots = afterMarch;
    }

    /// <summary>Forgets every gun's task, for a new level.</summary>
    public void Clear()
    {
        _gunShots.Clear();
        _gunShotOrder.Clear();
    }

    /// <summary>Gives a soldier's gun its <c>GunShot</c> task, after every gun made before it.</summary>
    /// <param name="view">The soldier.</param>
    public void AddGunShotTask(SoldierView view)
    {
        if (_gunShots.ContainsKey(view))
        {
            return;
        }

        GunShotTask task = new();
        _gunShots[view] = task;
        _gunShotOrder.Add((view, task));
    }

    /// <summary>
    /// The dynamic container's turn: every gun's GunShot task in the order the guns were made, each first offering
    /// its rounds to whoever they pass (0x47B690) and then marching them (0x47B750); then the chips detonate.
    /// </summary>
    public void MarchRounds()
    {
        if (_gunShotWorld is not { } world || _gunShotSounds is not { } sounds)
        {
            return;
        }

        foreach ((SoldierView view, GunShotTask task) in _gunShotOrder)
        {
            if (task.Rounds.Count == 0)
            {
                // 0x47B690 still runs 0x47C560 with nothing to march: last tick's drawn tracers are freed.
                task.AgeTracers();
                continue;
            }

            HearRoundsPassing(task.Rounds, view);
            task.Tick(_context.TickCount, _context.AiRandom, world, sounds);
        }

        _afterGunShots?.Invoke();
    }

    /// <summary><c>0x47B290</c>: the nearest point on each round's line, kept when it is within four metres.</summary>
    public static int RoundPointsNear(IReadOnlyList<GunShotRound> rounds, Vector3d human, Span<Vector3d> points)
    {
        int count = 0;
        foreach (GunShotRound round in rounds)
        {
            if (count >= points.Length)
            {
                break;
            }

            // x and y stay doubles, z is rounded to a float, and the midpoint's y is the rounded one.
            double sx = round.Direction.X * (double)round.Remaining;
            double sy = round.Direction.Y * (double)round.Remaining;
            float sz = round.Direction.Z * round.Remaining;
            double hx = sx * 0.5;
            double hy = (float)sy * 0.5;
            double hz = sz * 0.5;
            double mx = hx + round.Position.X - human.X;
            double my = hy + round.Position.Y - human.Y;
            double mz = hz + round.Position.Z - human.Z;

            // The midpoint's sphere first: half the segment plus four metres, squared.
            if (!((hx * hx) + (hy * hy) + (hz * hz) + 268435456.0 > (mx * mx) + (my * my) + (mz * mz)))
            {
                continue;
            }

            double t = (((human.Z - round.Position.Z) * sz) + ((human.Y - round.Position.Y) * sy)
                + ((human.X - round.Position.X) * sx)) / ((sz * sz) + (sy * sy) + (sx * sx));
            Vector3d point = new(round.Position.X + (t * sx), round.Position.Y + (t * sy), round.Position.Z + (sz * t));
            if (Vector3d.Distance(point, human) < 16384.0)
            {
                points[count++] = point;
            }
        }

        return count;
    }

    /// <summary>The nearest soldier skin one pass of a round crosses, through the Human evaluate's filter.</summary>
    /// <param name="origin">The pass's start.</param>
    /// <param name="end">The pass's end.</param>
    /// <param name="shooter">The soldier that fired, or <see langword="null"/>.</param>
    /// <param name="shooterTeam">The shooter's side; a body on it is not there for the round (<c>0x45E3E0</c>).</param>
    /// <param name="struck">The soldier crossed.</param>
    /// <param name="hit">The crossing: an entry, or the round leaving a body it went into.</param>
    /// <returns>Whether a soldier's skin lies on the pass.</returns>
    /// <remarks>Nothing is applied: the march decides what the crossing does, and <see cref="SoldierDamage.StrikeWithRound"/> hits.</remarks>
    public bool TryCastRoundAtSoldiers(
        Vector3d origin,
        Vector3d end,
        SoldierView? shooter,
        int shooterTeam,
        out SoldierView? struck,
        out HumanHit hit)
    {
        struck = null;
        hit = default;
        _sight.RefreshEyesAlongRound(origin, end, shooterTeam, shooter);
        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView soldier = member.View;

                // A corpse stays in the cast: the round that killed a body still leaves through its far side, and a
                // body on the ground still takes rounds and bleeds.
                if (ReferenceEquals(soldier, shooter)
                    || soldier.Soldier.Team == shooterTeam
                    || !ConditionalContainerGate.IsTaskActive(soldier.Soldier))
                {
                    continue;
                }

                // 0x45F800 reaches the body's shapes only through 0x45F580: the segment has to pass within the model's
                // bound (Human+160) of the origin (Human+32). The skinned mesh reaches past it -- a head in a flinch
                // stands out of the sphere -- and retail's rounds go over it.
                double bound = _sight.BoundUnitsOf(soldier);
                if (!SoldierSightQueries.SegmentMeetsSphere(origin, end, SoldierContext.OccupantOrigin(soldier), bound))
                {
                    continue;
                }

                // The body stands at its origin, Human+32 -- the feet it has plus the root -- not at a re-snapped ground.
                Matrix3f m = soldier.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                Vector3d feet = soldier.Position;
                bool met = group.Body.CollisionMesh is { } collision
                    ? HumanHitTest.TryCastMesh(
                        origin, end, feet, facing, collision, soldier.PoseAhead(), soldier.RootSample.Z, out HumanHit candidate)
                    : HumanHitTest.TryCast(
                        origin, end, feet, facing, soldier.PoseAhead(), out candidate, soldier.RootSample.Z);
                if (met && (struck is null || candidate.Distance < hit.Distance))
                {
                    struck = soldier;
                    hit = candidate;
                }
            }
        }

        return struck is not null;
    }

    /// <summary>
    /// Offers an AI-fired round to the soldiers and scores it against the nearest one it meets.
    /// This is the NPC-vs-NPC counterpart to <c>TryShoot</c>: a guard's round can strike a
    /// friendly, and a friendly's can strike a guard, so Harrison's team at the gate trades fire with
    /// the guards instead of only ever being aimed at the player.
    /// </summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where the round stops.</param>
    /// <param name="hitProbability">
    /// The firing AI's chance the round is on target (its <c>Human+1236</c>). Retail rolls it only against the
    /// player, so a soldier the round meets never consults it; it is kept for the callers' symmetry.
    /// </param>
    /// <param name="weapon">The firing weapon, whose damage factor and category apply on a hit.</param>
    /// <param name="shooter">The soldier that fired; its own body is never offered the round.</param>
    /// <param name="worldHit">
    /// The nearest world surface ahead, when the world blocks the ray; a soldier behind it is never
    /// offered the round (the wall already took it). <see langword="null"/> when no surface stopped it.
    /// </param>
    /// <param name="hit">The soldier struck, when one is.</param>
    /// <returns>
    /// <see langword="true"/> when a soldier consumed the round (damaged, or a same-team/invulnerable
    /// body the round met and stopped at); <see langword="false"/> when it met nobody and flies on.
    /// </returns>
    /// <remarks>
    /// <b>The damage is applied here</b>, exactly as in <c>TryShoot</c>. The Human's evaluate
    /// (<c>0x45E3E0</c>) decides each body the round meets, nearest first: one on the shooter's own side
    /// (<c>Human+252</c>) lets it pass through to the next, any other takes it -- with no probability roll,
    /// which retail makes only for the player. Scripted invulnerability (500.qvm sets
    /// <c>SetInvulnerability(TRUE)</c> on the friendly squad, so they cannot die) makes the round
    /// <em>meet</em> the body but do nothing; the body is still a physical obstacle that consumes it.
    /// </remarks>
    public bool TryShootAiRound(
        Vector3d origin,
        Vector3d end,
        float hitProbability,
        WeaponType weapon,
        SoldierView? shooter,
        WorldRayHit? worldHit,
        out HumanHit hit)
    {
        hit = default;
        _sight.RefreshEyesAlongRound(origin, end, shooter?.Soldier.Team ?? _context.PlayerTeam, shooter);

        List<(HumanHealth health, SoldierView view, HumanHit candidate, double distance)> candidates = [];

        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier)
                    || ReferenceEquals(soldier, shooter))
                {
                    continue;
                }
                HumanHealth health = _context.HealthOf(soldier);
                if (health.IsDead)
                {
                    continue;
                }

                // Same facing/feet/pose resolution as TryShoot: cast at the skinned collision mesh when
                // the model ships one, else the cylinder-and-nearest-bone stand-in, both on the grounded
                // feet the body is actually drawn on.
                Matrix3f m = soldier.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                double snappedZ = _context.SnapToGround(soldier.Position.X, soldier.Position.Y, soldier.Position.Z);
                Vector3d feet = new(soldier.Position.X, soldier.Position.Y, snappedZ);

                bool met = group.Body.CollisionMesh is { } collision
                    ? HumanHitTest.TryCastMesh(
                        origin, end, feet, facing, collision, soldier.Pose(), soldier.RootSample.Z, out HumanHit candidate)
                    : HumanHitTest.TryCast(
                        origin, end, feet, facing, soldier.Pose(), out candidate, soldier.RootSample.Z);
                if (!met)
                {
                    continue;
                }

                // A wall between the muzzle and this soldier stops the round before it arrives.
                double distance = candidate.Distance;
                if (worldHit is { } surface && surface.Distance < distance - 40.0)
                {
                    continue;
                }
                candidates.Add((health, soldier, candidate, distance));
            }
        }

        // Where the round stops if nobody takes it: the surface in its way, or the end of its reach.
        Vector3d unstopped = worldHit is { } wall ? wall.Position : end;
        if (candidates.Count == 0)
        {
            PassedBy(unstopped, null);
            return false;
        }

        // Nearest first: retail's cast meets the nearest human and rolls the probability there; a
        // failed roll flies the round on to the next one behind.
        candidates.Sort(static(a, b) => a.distance.CompareTo(b.distance));

        foreach ((HumanHealth health, SoldierView struckView, HumanHit candidate, _) in candidates)
        {
            // No probability roll against a soldier: the Human's evaluate (0x45E3E0) rolls the round's
            // probability only when the struck human is player 0. Any other body is hit outright unless
            // the shooter is on its side (Human+252), and then the round passes THROUGH it and flies on.
            if (!struckView.Soldier.EvaluateWeaponCollision(new WeaponCollisionQuery(shooter?.Soldier, null, hitProbability)))
            {
                continue;
            }

            hit = candidate;

            // The shot's WeaponHit to the struck soldier (0x47BA54), which its own handler takes.
            struckView.Soldier.WeaponHit(
                new Igi1WeaponHit(candidate, origin, end, weapon.DamageFactor, weapon.Category, shooter, FromPlayer: false).ToShared());
            PassedBy(candidate.Position, struckView);
            return true;
        }

        // Every candidate rolled a miss -- the round flew past them all and continues beyond.
        PassedBy(unstopped, null);
        return false;

        void PassedBy(Vector3d stop, SoldierView? struck)
        {
            if (shooter is not null)
            {
                _noise.NotifyRoundPassed(origin, stop, shooter, struck);
            }
        }
    }

    /// <summary>
    /// Offers a task's rounds to every human they pass, before the march: <c>0x4606F0</c> for each human, with
    /// <c>0x47B290</c>'s points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A human on the shooter's side, or the shooter, hears nothing. For any other, each round whose line passes
    /// within four metres of the human's origin gives the nearest point on it; the point is broadcast as
    /// GUNSHOTMISS with radius two metres, filter <c>0x46</c> and the shooter's side, and every AI on another
    /// side within two metres of it takes it. The line is the round's whole range, cast or not, so a body the
    /// round will strike hears it pass as well.
    /// </para>
    /// <para>The player hears its own whizz (<c>gunshotmiss</c>) instead; that is the host's.</para>
    /// </remarks>
    private void HearRoundsPassing(IReadOnlyList<GunShotRound> rounds, SoldierView shooter)
    {
        int side = shooter.Soldier.Team;
        int source = shooter.Soldier.Id;
        Span<Vector3d> points = stackalloc Vector3d[32];
        foreach ((SoldierView view, Igi1HumanAi _) in _context.Brains)
        {
            if (ReferenceEquals(view, shooter)
                || view.Soldier.Team == side
                || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            int count = RoundPointsNear(rounds, SoldierContext.OccupantOrigin(view), points);
            for (int i = 0; i < count; i++)
            {
                int heard = 0;
                foreach ((SoldierView listener, Igi1HumanAi brain) in _context.Brains)
                {
                    if (!ReferenceEquals(listener, shooter)
                        && ConditionalContainerGate.IsTaskActive(listener.Soldier)
                        && brain.ReceiveRoundPassing(points[i], listener.Soldier.Team != side, source, shooter.Position))
                    {
                        heard++;
                    }
                }

                _noise.ReportNoise("near-miss", heard, Igi1HumanAi.GunshotMissRadiusInUnits);
            }
        }
    }
}
