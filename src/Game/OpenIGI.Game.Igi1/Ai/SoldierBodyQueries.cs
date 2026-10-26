using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// What the rest of the game asks of the soldiers' bodies: where each stands and whether it lives, what a segment or a
/// mine's radius meets, what the map and the cameras see, and the collision bodies the player's sweep is stopped by.
/// </summary>
/// <remarks>
/// Every query here only reads: nothing is struck, heard or moved. The answers are taken from the same roster walk the
/// soldiers update in, so a query made during the walk sees each body where the walk has left it.
/// </remarks>
/// <param name="context">The soldiers, their brains and their health.</param>
internal sealed class SoldierBodyQueries(SoldierContext context)
{
    /// <summary>How high above a soldier's feet its broad-phase sphere is centred, in world units.</summary>
    public const double PickChestUnits = 3686.0;

    /// <summary>The broad-phase cull radius, in world units — generous (a soldier plus its outstretched arms).</summary>
    public const double PickCullRadiusUnits = 8192.0;

    private readonly SoldierContext _context = context;

    /// <summary>Gets how many soldiers have been killed.</summary>
    public int Killed
    {
        get
        {
            int dead = 0;
            foreach (HumanHealth health in _context.Health.Values)
            {
                if (health.IsDead)
                {
                    dead++;
                }
            }

            return dead;
        }
    }

    /// <summary>
    /// Lists every resolved soldier for the developer inspector, in a form that outlives them.
    /// </summary>
    /// <param name="into">The list to fill; cleared first.</param>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// A read-only projection rather than access to the soldiers themselves. The inspector runs on the
    /// far side of an interface that must not see gameplay types, and a panel holding a live
    /// <c>SoldierView</c> across a mission change would be holding a corpse the loader had forgotten
    /// about. Everything the panel needs is copied out here, once per frame it draws.
    /// </para>
    /// <para>
    /// The index into the filled list is the handle the inspector's commands use, which is why the
    /// order is the stable group-then-member walk rather than anything sorted.
    /// </para>
    /// </remarks>
    public void Snapshot(List<SoldierSnapshot> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView view = member.View;
                bool dead = view.IsDead || (_context.Health.TryGetValue(view, out HumanHealth? health) && health.IsDead);

                into.Add(new SoldierSnapshot(
                    view.Soldier.Name ?? string.Empty,
                    view.Position / WorldScene.UnitsPerMetre,
                    dead,
                    view.CurrentAnimation,
                    _context.Health.TryGetValue(view, out HumanHealth? hp) ? hp.RemainingFraction : -1f,
                    member.IsCutsceneActor));
            }
        }
    }

    /// <summary>Gets every soldier as the map computer reads the human list: where it is, which way it faces, whether it lives.</summary>
    /// <returns>The soldiers, in the order they were placed.</returns>
    public IReadOnlyList<(GameTask Task, int Team, Vector3d Position, float Facing, bool Live)> MapHumans()
    {
        List<(GameTask, int, Vector3d, float, bool)> humans = new(_context.Brains.Count);
        foreach ((SoldierView view, Igi1HumanAi brain) in _context.Brains)
        {
            bool live = ConditionalContainerGate.IsTaskActive(view.Soldier)
                && !view.IsDead
                && (!_context.Health.TryGetValue(view, out HumanHealth? health) || health is not { IsDead: true });
            humans.Add((view.Soldier, view.Soldier.Team, brain.Position, brain.Facing, live));
        }

        return humans;
    }

    /// <summary>Gets the living soldiers the retail map computer plots as guard contacts.</summary>
    public IReadOnlyList<Vector3d> MapContacts()
    {
        List<Vector3d> contacts = [];
        foreach ((SoldierView view, Igi1HumanAi brain) in _context.Brains)
        {
            if (ConditionalContainerGate.IsTaskActive(view.Soldier)
                && !view.IsDead
                && (!_context.Health.TryGetValue(view, out HumanHealth? health) || health is not { IsDead: true }))
            {
                contacts.Add(brain.Position);
            }
        }

        return contacts;
    }

    /// <summary>
    /// Every live soldier as a security camera's scan is offered it — <c>0x45FE30</c>'s description, in the
    /// global human list's order.
    /// </summary>
    /// <remarks>
    /// The lens scans for team 1 (<c>0x4415C0</c>), so a team-1 soldier counts as the cameras' own side: its
    /// body is what a camera raises the alarm on at once. The mark is the one the guards' own scan tests
    /// (<c>sub_465820</c>), the same for a body as for a standing soldier.
    /// </remarks>
    public IReadOnlyList<SecurityCameraSubject> CameraSubjects()
    {
        List<SecurityCameraSubject> subjects = [];
        foreach ((SoldierView view, Igi1HumanAi brain) in _context.Brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d feet = new(
                view.Position.X,
                view.Position.Y,
                _context.SnapToGround(view.Position.X, view.Position.Y, view.Position.Z));
            bool dead = view.IsDead
                || brain.Activity == AiSoldierActivity.Dead
                || (_context.Health.TryGetValue(view, out HumanHealth? health) && health.IsDead);
            subjects.Add(new SecurityCameraSubject(
                view,
                new Vector3d(feet.X, feet.Y, feet.Z + Igi1HumanAi.DetectionMarkAboveFeet(crouching: false)),
                CamerasSide: view.Soldier.Team == 1,
                Alive: !dead,
                view.Soldier));
        }

        return subjects;
    }

    /// <summary>
    /// Names the detection state of the soldier under a ray, toward a target eye — the overlay's "why won't this
    /// guard see me" readout. Picks the nearest soldier the ray's body sphere touches and asks its brain which
    /// gate (cone, line of sight) is passing.
    /// </summary>
    /// <param name="originUnits">The ray origin in engine units.</param>
    /// <param name="direction">The ray direction; need not be normalised.</param>
    /// <param name="reachUnits">How far to look, in units.</param>
    /// <param name="enemyEyeUnits">The eye the guards actually test against — the player's, in units.</param>
    /// <param name="info">The formatted detection line, empty when no soldier is under the ray.</param>
    /// <returns><see langword="true"/> when a soldier was under the ray and had a brain to ask.</returns>
    public bool TryDetectionInfo(
        Vector3d originUnits,
        Vector3d direction,
        double reachUnits,
        Vector3d enemyEyeUnits,
        out string info)
    {
        info = string.Empty;

        double length = Math.Sqrt(
            (direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
        if (length <= 0.0)
        {
            return false;
        }

        Vector3d dir = new(direction.X / length, direction.Y / length, direction.Z / length);
        Igi1HumanAi? nearest = null;
        SoldierView? nearestView = null;
        double nearestT = double.MaxValue;

        foreach ((SoldierView view, Igi1HumanAi brain) in _context.Brains)
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }
            double snappedZ = _context.SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
            Vector3d centre = new(view.Position.X, view.Position.Y, snappedZ + PickChestUnits);
            if (SoldierSightQueries.RaySphere(originUnits, dir, centre, PickCullRadiusUnits, reachUnits, out double t) && t < nearestT)
            {
                nearestT = t;
                nearest = brain;
                nearestView = view;
            }
        }

        if (nearest is null)
        {
            return false;
        }

        (double distU, bool inCone, bool los, bool alerted, bool visible) = nearest.ProbeDetection(enemyEyeUnits);
        string cone = inCone ? "IN" : "out";
        string clear = los ? "clear" : "BLOCKED";
        string alert = alerted ? "yes" : "no";
        string seen = visible ? "YES" : "no";
        SoldierAgent? member = nearestView is null ? null : _context.FindAgent(nearestView);
        string task = nearestView?.Soldier.Name ?? "?";
        string weapon = member?.WeaponType?.ScriptId ?? "NONE";
        string aim = nearest.Target?.ModeOf(nearest).ToString() ?? "None";
        int aimers = nearest.Target?.Count(AiAimMode.Aiming) ?? 0;
        string patrol = nearest.Arm == AiCombatArm.Patrol ? $"/{nearest.PatrolAction}" : string.Empty;
        string distance = (distU / WorldScene.UnitsPerMetre).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        info =
            $"detect: dist={distance}m cone={cone} los={clear} alert={alert} "
            + $"seen={seen} {nearest.Activity} task={task} ai={nearest.Type}/{nearest.Arm} script={(nearest.HasScript ? 1 : 0)} "
            + $"phase={nearest.CombatPhase}{patrol} aim={aim}:{aimers} weapon={weapon} "
            + $"ammo={member?.AmmoInClip ?? 0} trig={(nearest.Combat.PullTrigger ? 1 : 0)}";
        return true;
    }

    /// <summary>A body's heading out of its world 3×3 — the inverse of <c>Z(facing)</c>.</summary>
    /// <remarks>
    /// The second column is the body's forward, and a soldier's forward is
    /// <c>(-sin f, cos f)</c> (see <see cref="Igi1HumanAi"/>), so the angle reads straight back out.
    /// Orientations are pure Z rotations for every body this renderer draws.
    /// </remarks>
    public static float FacingOf(in Matrix3f orientation) => MathF.Atan2(-orientation.M12, orientation.M22);

    /// <summary>Evaluates a soldier's current animation at the heading actually displayed for this frame.</summary>
    /// <remarks>
    /// A <see cref="SoldierView"/> caches a faced pose, whereas drawing applies the r_smooth orientation
    /// after the identical unrotated animation. Re-evaluating only the cached pose here gives the hit mesh
    /// the same animated limbs and presented heading without changing the authoritative AI transform.
    /// </remarks>
    public static BonePose PoseAtPresentationOrientation(SoldierView soldier, in Matrix3f orientation)
    {
        Matrix3f simulationOrientation = soldier.Orientation;
        if (simulationOrientation == orientation)
        {
            return soldier.Pose();
        }

        Vector3d simulationPosition = soldier.Position;
        try
        {
            soldier.PlaceAt(simulationPosition, orientation);
            return soldier.Pose();
        }
        finally
        {
            soldier.PlaceAt(simulationPosition, simulationOrientation);
        }
    }

    /// <summary>Pushes every living, active soldier's body into the shared collision index.</summary>
    /// <remarks>
    /// Called once per tick after the roster has moved. A body is the analytic
    /// <see cref="RayCapsule"/> the player's wall sweep collides with; only soldiers that are alive
    /// and whose conditional container is live get one, so a dormant sentry or a corpse is no
    /// obstacle. The same <see cref="ObjectCollisionIndex"/> instance is the one the player camera
    /// uses for its walls, so a soldier presents a real wall in retail's spirit.
    /// </remarks>
    public void SyncCollisionBodies()
    {
        if (_context.Solids is not { } solids)
        {
            return;
        }

        int count = 0;
        foreach (SoldierView view in _context.Brains.Keys)
        {
            if (!view.IsDead && ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                count++;
            }
        }

        if (count == 0)
        {
            solids.SetHumanBodies([]);
            return;
        }

        HumanBody[] bodies = new HumanBody[count];
        int index = 0;
        foreach (SoldierView view in _context.Brains.Keys)
        {
            if (view.IsDead || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d pos = view.Position;
            double radius = 0.55 * WorldScene.UnitsPerMetre;
            bodies[index++] = new HumanBody(
                new Vector3d(pos.X, pos.Y, pos.Z),
                new Vector3d(pos.X, pos.Y, pos.Z + (1.8 * WorldScene.UnitsPerMetre)),
                radius);
        }

        solids.SetHumanBodies(bodies);
    }

    /// <summary>Every soldier where it stands at this moment, for an area counting as the level walk reaches it.</summary>
    public IEnumerable<HumanOccupant> LiveOccupants()
    {
        foreach (SoldierView view in _context.Brains.Keys)
        {
            yield return new HumanOccupant(view.Soldier.Id, view.Soldier.Team, SoldierContext.OccupantOrigin(view), view.IsDead);
        }
    }

    /// <summary>Reports every soldier's current presence for mission queries.</summary>
    /// <remarks>
    /// One occupant per placed soldier (dormant or active): its task id, team, current position and
    /// death. The host feeds this to the mission runtime on every tick so AreaActivate volumes count
    /// the friendly squad's advance and <c>HumanSoldier_N.isDead</c> tracks the guards the squad kills.
    /// </remarks>
    public IReadOnlyCollection<HumanOccupant> Occupants()
    {
        if (_context.Brains.Count == 0)
        {
            return Array.Empty<HumanOccupant>();
        }

        HumanOccupant[] occupants = new HumanOccupant[_context.Brains.Count];
        int index = 0;
        foreach (SoldierView view in _context.Brains.Keys)
        {
            occupants[index++] = new HumanOccupant(
                view.Soldier.Id,
                view.Soldier.Team,
                SoldierContext.OccupantOrigin(view),
                view.IsDead);
        }
        return occupants;
    }

    /// <summary>Finds the nearest living soldier inside a mine's activation radius.</summary>
    public bool TryProximity(Vector3d center, float radiusUnits, out HumanProximityTarget target)
    {
        target = default;
        double radiusSquared = radiusUnits * radiusUnits;
        bool found = false;

        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView soldier = member.View;
                if (!ConditionalContainerGate.IsTaskActive(soldier.Soldier)
                    || soldier.IsDead
                    || (_context.Health.TryGetValue(soldier, out HumanHealth? health) && health is { IsDead: true }))
                {
                    continue;
                }

                Vector3d position = soldier.Position;
                double distanceSquared = Vector3d.DistanceSquared(center, position);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                double distance = Math.Sqrt(distanceSquared);
                if (!found || distance < target.Distance)
                {
                    target = new HumanProximityTarget(position, distance);
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>Finds the nearest living soldier on a segment without scoring the hit.</summary>
    public bool TryIntersect(Vector3d origin, Vector3d end, out HumanHit hit) => TryNearest(origin, end, out hit, out _);

    /// <summary>Finds the nearest living soldier on a segment, and which it is, without scoring the hit.</summary>
    /// <param name="origin">Where the segment starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="hit">Where it meets the soldier.</param>
    /// <param name="soldier">The soldier met.</param>
    /// <returns>Whether it meets one.</returns>
    public bool TryNearest(Vector3d origin, Vector3d end, out HumanHit hit, out SoldierView? soldier)
    {
        hit = default;
        soldier = null;
        bool found = false;

        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView view = member.View;
                if (!ConditionalContainerGate.IsTaskActive(view.Soldier)
                    || view.IsDead
                    || (_context.Health.TryGetValue(view, out HumanHealth? health) && health is { IsDead: true }))
                {
                    continue;
                }

                Matrix3f m = view.Orientation;
                Vector3f facing = new(m.M12, m.M22, m.M32);
                double snappedZ = _context.SnapToGround(view.Position.X, view.Position.Y, view.Position.Z);
                Vector3d feet = new(view.Position.X, view.Position.Y, snappedZ);
                bool met = group.Body.CollisionMesh is { } collision
                    ? HumanHitTest.TryCastMesh(
                        origin, end, feet, facing, collision, view.Pose(), view.RootSample.Z, out HumanHit candidate)
                    : HumanHitTest.TryCast(
                        origin, end, feet, facing, view.Pose(), out candidate, view.RootSample.Z);
                if (met && (!found || candidate.Distance < hit.Distance))
                {
                    hit = candidate;
                    soldier = view;
                    found = true;
                }
            }
        }

        return found;
    }
}
