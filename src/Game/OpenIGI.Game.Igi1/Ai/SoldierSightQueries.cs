using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using System.Runtime.InteropServices;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// What the soldiers see with: the sight line's own rule, each soldier's eye as its palette holds it, the rays and rounds
/// that rebuild those palettes, and the humans the detection scan is offered.
/// </summary>
/// <remarks>
/// The eye a guard looks along is not its pose: it is the bone palette's eye entry, rebuilt only when the camera draws
/// the soldier or a ray or round passes it (<c>0x45F240</c>). Everything that rebuilds it lives here, beside the casts
/// that test what it then sees.
/// </remarks>
/// <param name="context">The soldiers and their brains.</param>
/// <param name="scriptSound">Plays a definition a brain's script asks for, at a position.</param>
internal sealed class SoldierSightQueries(SoldierContext context, Action<string, Vector3d, bool> scriptSound)
{
    /// <summary>The soldier model's <c>MESH+0x78</c> bound, <c>Human+160</c>, when the model's own is not to hand.</summary>
    public const float DefaultHumanBoundMetres = 0.94f;

    /// <summary>How far past a see-through hit the next probe starts, so it clears that thin surface.</summary>
    private const double SightSkipEpsilonUnits = 64d;

    /// <summary>The most see-through layers a single sight probe steps through before giving up.</summary>
    private const int SightSkipLimit = 8;

    /// <summary>
    /// Bone 5's offset from the origin (Human+32) in the palette a soldier is created with, in its body's frame: the eye
    /// position of an undrawn guard, read off mission 4's level-start guards 421, 422 and 427 (placed at heading 0), whose
    /// eyes stayed this far from their origins while the origins drifted with the idle clip.
    /// </summary>
    public static readonly Vector3f RestEyeOffset = new(-31.9f, 33.0f, 3502.6f);

    /// <summary>
    /// The gun's offset from the origin in the palette a soldier is created with, in its body's frame -- read off the same
    /// guards (and 426, placed at 24 degrees, which turns back to it), whose guns rode their origins this far off.
    /// </summary>
    public static readonly Vector3f RestGunOffset = new(-228.9f, 872.8f, 1336.1f);

    /// <summary>
    /// Bone 5's rest orientation in the body's frame -- the eye matrix of an undrawn guard placed at heading 0, read
    /// off every level-start guard of mission 1 by the probe (the cone test's snapshot, +24). One placed at another
    /// heading looks along that heading times this (BindViewTracking).
    /// </summary>
    public static readonly Matrix3f RestEyeOrientation = new(
        0.9956f, -0.0927f, 0.0076f,
        0.0919f, 0.9926f, 0.0784f,
        -0.0148f, -0.0774f, 0.9968f);

    private readonly SoldierContext _context = context;

    private readonly Action<string, Vector3d, bool> _scriptSound = scriptSound;

    // One combat-target record per opposing soldier, created lazily as each is first engaged. The
    // friendly-vs-guard gate fight needs a real enemy record to aim at and fight, not just the player:
    // per-brain enemy selection swaps the default target onto one of these when an opposing soldier is
    // nearer than any hostile player, so Harrison's team at the gate trades fire with the guards.
    private readonly Dictionary<SoldierView, AiCombatTarget> _npcTargets = [];

    /// <summary>Gets or sets the world trace the level's sight lines are tested with, kept for the scenario runs.</summary>
    public WorldTraceCast? SightTraceForTesting { get; set; }

    /// <summary>Whether the eye can see a point along a plain cast, any contact blocking.</summary>
    /// <remarks>
    /// The binocular overlay's rule: the retail detector culls by sector/portal visibility, which
    /// see-through surfaces do not bound. See <see cref="HasLineOfSight(WorldTraceCast, Vector3d, Vector3d, double, GameTask)"/>.
    /// </remarks>
    public static bool HasLineOfSight(WorldRayCast cast, Vector3d from, Vector3d to)
    {
        ArgumentNullException.ThrowIfNull(cast);
        return HasLineOfSight(
            (in WorldTraceRequest request, out WorldTraceHit hit) =>
            {
                bool found = cast(request.Origin, request.End, out WorldRayHit plain);
                hit = found
                    ? WorldTraceHit.FromRayHit(request, plain, WorldTraceHitKind.StaticObject)
                    : default;
                return found;
            },
            from,
            to,
            objectTolerance: 1.0);
    }

    /// <summary>Whether the eye can see a point, treating glass and fences as non-occluding.</summary>
    /// <param name="trace">The world trace.</param>
    /// <param name="from">The eye.</param>
    /// <param name="to">The point looked at.</param>
    /// <param name="objectTolerance">
    /// How far along the eye-to-point segment, as a fraction, an object contact has to be before it stops
    /// counting as cover. <c>1</c> makes every contact block.
    /// </param>
    /// <remarks>
    /// <para>
    /// A cast returns only its NEAREST contact, so a single probe would stop at a chain-link fence and
    /// never notice a solid wall right behind it. This steps the ray past each transparent surface
    /// (glass, wire) and keeps looking, so a guard behind a fence is seen but one behind the wall the
    /// fence stands in front of is not. See <see cref="GameMaterial.IsSightTransparent"/>.
    /// </para>
    /// <para>
    /// Terrain always blocks. An object contact blocks only nearer than the tolerance: the AI's sight ray
    /// (<c>0x489B20</c>) is visible when <c>0x4CE410</c> reports no contact <em>or</em> the contact's segment
    /// fraction is at least 0.79, and <c>0x4CE410</c> forces the fraction to zero for a terrain hit.
    /// </para>
    /// </remarks>
    /// <param name="ignoredOwner">The viewer itself, when it has a body the ray would otherwise meet -- a vehicle.</param>
    /// <returns><see langword="true"/> when the line is clear.</returns>
    public static bool HasLineOfSight(
        WorldTraceCast trace,
        Vector3d from,
        Vector3d to,
        double objectTolerance,
        GameTask? ignoredOwner = null)
    {
        ArgumentNullException.ThrowIfNull(trace);

        Vector3d segment = new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        double segmentLength = Math.Sqrt((segment.X * segment.X) + (segment.Y * segment.Y) + (segment.Z * segment.Z));
        Vector3d origin = from;
        double travelled = 0d;
        for (int step = 0; step <= SightSkipLimit; step++)
        {
            // Portal faces are not traced. They describe the doorway and window openings for the renderer's
            // sector visibility, not collision, so 0x4CE410's object handlers never meet them; a closed
            // door leaf is its own collision geometry and still blocks.
            if (!trace(new WorldTraceRequest(origin, to, WorldTraceMask.World, IgnoredOwner: ignoredOwner), out WorldTraceHit hit))
            {
                return true;
            }

            bool transparent = GameMaterial.IsSightTransparent(hit.Material);
            if (!transparent || step == SightSkipLimit)
            {
                // The traversal budget is a safety bound, not evidence that the remainder is clear: past
                // it, whatever the cast reports is cover.
                // A static object blocks wherever it is met. 0x489B20 does accept a returned fraction of 0.79 or
                // more, but the handlers never return one for placed geometry: 359 retail sight casts logged by
                // the probe came back either clear at 1.0 or blocked at exactly 0.0 -- including a mission-1
                // tower guard's ray that met the small barracks' roof 82% of the way along, which the old
                // "past 0.79 is clear" rule let through. Only a moving object keeps the tolerance, unmeasured.
                if (hit.Kind is WorldTraceHitKind.Terrain or WorldTraceHitKind.StaticObject || segmentLength <= 0d)
                {
                    return false;
                }

                return (travelled + hit.Distance) / segmentLength >= objectTolerance;
            }

            double remaining = segmentLength - travelled;
            double advance = hit.Distance + SightSkipEpsilonUnits;
            if (advance >= remaining)
            {
                return true;
            }

            travelled += advance;
            double fraction = travelled / segmentLength;
            origin = new(from.X + (segment.X * fraction), from.Y + (segment.Y * fraction), from.Z + (segment.Z * fraction));
        }

        return true;
    }

    /// <summary>Nearest positive entry of a ray (unit direction) into a sphere, within reach. Distance is along the ray.</summary>
    public static bool RaySphere(Vector3d origin, Vector3d dir, Vector3d centre, double radius, double reach, out double t)
    {
        t = 0.0;
        double ox = origin.X - centre.X;
        double oy = origin.Y - centre.Y;
        double oz = origin.Z - centre.Z;
        double b = (ox * dir.X) + (oy * dir.Y) + (oz * dir.Z);
        double c = (ox * ox) + (oy * oy) + (oz * oz) - (radius * radius);
        double disc = (b * b) - c;
        if (disc < 0.0)
        {
            return false;
        }

        double root = Math.Sqrt(disc);
        double near = -b - root;
        double far = -b + root;
        t = near >= 0.0 ? near : far;
        return t >= 0.0 && t <= reach;
    }

    /// <summary><c>0x45F580</c>'s test of one segment against a human's sphere.</summary>
    /// <param name="from">The segment's start.</param>
    /// <param name="to">The segment's end.</param>
    /// <param name="centre">The sphere's centre.</param>
    /// <param name="radius">The sphere's radius.</param>
    /// <returns>Whether the segment meets the sphere.</returns>
    /// <remarks>
    /// The squared distance from the sphere's centre to the segment's LINE against the radius squared, and the
    /// segment's box against the sphere's box: the two tests together, with no clamp to the segment's ends.
    /// </remarks>
    public static bool SegmentMeetsSphere(Vector3d from, Vector3d to, Vector3d centre, double radius)
    {
        double ax = from.X - centre.X, ay = from.Y - centre.Y, az = from.Z - centre.Z;
        double bx = to.X - centre.X, by = to.Y - centre.Y, bz = to.Z - centre.Z;
        if (Math.Min(ax, bx) > radius || Math.Min(ay, by) > radius || Math.Min(az, bz) > radius
            || Math.Max(ax, bx) < -radius || Math.Max(ay, by) < -radius || Math.Max(az, bz) < -radius)
        {
            return false;
        }

        double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        double length = (dx * dx) + (dy * dy) + (dz * dz);
        double along = (-ax * dx) + (-ay * dy) + (-az * dz);
        double offLine = (ax * ax) + (ay * ay) + (az * az) - (length > 0.0 ? along * along / length : 0.0);
        return radius * radius >= offLine;
    }

    /// <summary>The radius of a soldier's sphere (<c>Human+160</c>) in world units: its model's bound, or the default.</summary>
    /// <param name="view">The soldier.</param>
    /// <returns>The radius the ray and segment visits test against.</returns>
    public double BoundUnitsOf(SoldierView view) =>
        (_context.DetectorMarkerScaleMetres.TryGetValue(view, out float bound) && bound > 0f
            ? bound
            : DefaultHumanBoundMetres) * WorldScene.UnitsPerMetre;

    /// <summary>Gives a new brain its eye, its view and its sight cast, from the palette its body is created with.</summary>
    /// <param name="brain">The brain.</param>
    /// <param name="view">Its body.</param>
    public void BindViewTracking(Igi1HumanAi brain, SoldierView view)
    {
        brain.SetScriptSound((name, attached) => _scriptSound(name, view.Position, attached));

        // A new human's palette is built once, at its creation, from its authored heading (0x463220 sets the facing
        // and rebuilds it through 0x4D38C0): the eye of a soldier nobody has drawn or looked past is the rest eye
        // turned by the heading it was placed with, B * R. Probed on mission 4: thirteen undrawn soldiers placed at
        // 0, 24, -54, -78, 96 and 108 degrees, B * R to 1e-4 each, constant for as long as nothing refreshed them --
        // and patrol 408 kept its placed -96 degrees although it turned on its first tick. Every mission-1 guard is
        // placed at 0, which is why they all shared R itself.
        Matrix3f placedEye = brain.Orientation * RestEyeOrientation;
        if (_context.FindAgent(view) is { } placed)
        {
            placed.EyeOrientation = null;
            placed.EyeOffset = null;
            Vector3f restGun = brain.Orientation.Transposed().Transform(RestGunOffset);
            placed.GunOffset = new Vector3d(restGun.X, restGun.Y, restGun.Z);
            placed.OriginBeforeTick = SoldierContext.OccupantOrigin(view);
        }

        brain.SightCast = (from, to, target) => RefreshEyesAlong(from, to, view.Soldier.Id, target);
        brain.ViewPositionProvider = () => SoldierContext.ViewPosition(view, brain);

        // The eye's position comes out of the same palette entry (0x44F410: Human+32 plus bone 5's translation, model
        // +684..692), so it too is the placed rest eye until something rebuilds the palette -- and between rebuilds it
        // follows the origin, not the pose.
        Vector3f restOffset = brain.Orientation.Transposed().Transform(RestEyeOffset);
        Vector3d placedEyeOffset = new(restOffset.X, restOffset.Y, restOffset.Z);
        brain.SetEyeTransform(
            () =>
            {
                if (view.Pose().BoneCount <= HumanAttachPoints.AiEye)
                {
                    return new Vector3d(
                        view.Position.X,
                        view.Position.Y,
                        view.Position.Z + Igi1HumanAi.HumanSeparationEyeHeight);
                }

                SoldierAgent? member = _context.FindAgent(view);
                if (member is { InView: true })
                {
                    RebuildEye(member, view, brain);
                }

                return SoldierContext.OccupantOrigin(view) + (member?.EyeOffset ?? placedEyeOffset);
            },
            // The matrix is the eye bone's, as the palette holds it (0x44F410 copies bone 5's entry, +696): the body's
            // own matrix times the bone's rotation in the pose the soldier had when the palette was last built.
            // Probed on mission 4: running squad member 404's refreshed eye, taken back into his body's frame,
            // matched clip 60's eye bone to the clip phase, pitched 12 degrees down where the body's heading is level.
            //
            // But only when the palette is rebuilt. 0x44F410 copies the eye's matrix out of the bone palette
            // ([Human+0x70]+0x2B8), and 0x45F240 rebuilds it -- while the soldier's dirty byte (+216, set by its
            // per-tick 0x45D080) is up -- from two places: the camera pass 0x45F140, for a soldier inside the
            // render camera's frustum at LOD < 4 (ObserveView), and the ray and segment visits 0x45F500 / 0x45F800,
            // for a soldier some cast passes (RefreshEyesAlong). A guard nothing refreshes keeps looking the way it
            // last did -- and one never refreshed along its placed heading's rest eye.
            () =>
            {
                SoldierAgent? member = _context.FindAgent(view);
                if (member is null)
                {
                    return brain.ViewOrientation;
                }

                if (member.InView)
                {
                    RebuildEye(member, view, brain);
                }

                return member.EyeOrientation ?? placedEye;
            });
    }

    /// <summary>What a round does to the humans along its flight: rebuilds their palettes, and so their eyes.</summary>
    /// <param name="from">Where the round starts, in world units.</param>
    /// <param name="to">The end of its reach.</param>
    /// <param name="shooterTeam">The firing human's side (Human+252).</param>
    /// <param name="shooter">The firing soldier, or <see langword="null"/> for the player.</param>
    /// <remarks>
    /// The round's cast (<c>0x47B750</c> -> <c>0x4CC2A0</c>) visits the humans along its remaining path through the
    /// Human's ray visit <c>0x45F500</c>, which asks the class filter first -- the evaluate <c>0x45E3E0</c>, which
    /// lets a round pass through its shooter's own side unvisited -- then <c>0x45F580</c>'s sphere test, then rebuilds
    /// the palette (<c>0x45F240</c>), whether or not the round goes on to strike that body. Probed on mission 4: guard
    /// 411's eye was rebuilt every three ticks by the squad's rounds from tick 4559.
    /// </remarks>
    public void RefreshEyesAlongRound(Vector3d from, Vector3d to, int shooterTeam, SoldierView? shooter)
    {
        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView view = member.View;
                if (ReferenceEquals(view, shooter) || view.IsDead || view.Soldier.Team == shooterTeam
                    || !ConditionalContainerGate.IsTaskActive(view.Soldier)
                    || !_context.Brains.TryGetValue(view, out Igi1HumanAi? brain))
                {
                    continue;
                }

                double radius = BoundUnitsOf(view);
                if (SegmentMeetsSphere(from, to, SoldierContext.OccupantOrigin(view), radius))
                {
                    RebuildEye(member, view, brain);
                    SoldierSimulation.PaletteRebuildTraced?.Invoke(view.Soldier.Id, "round", from, to);
                }
            }
        }
    }

    /// <summary>A round of the player's passes: the eyes along it are rebuilt, as for any round (see <see cref="RefreshEyesAlongRound"/>).</summary>
    /// <param name="from">Where the round starts, in world units.</param>
    /// <param name="to">The end of its reach.</param>
    public void RefreshEyesAlongPlayerRound(Vector3d from, Vector3d to) =>
        RefreshEyesAlongRound(from, to, _context.PlayerTeam, shooter: null);

    /// <summary>
    /// The humans the detection scan offers one soldier this tick — <c>0x489750</c> over each human's own
    /// description (<c>0x45FE30</c>).
    /// </summary>
    /// <remarks>
    /// The global human list's order: the player first, then every soldier whose container is live, the
    /// soldier itself left out. Each is described from this soldier's side, so the scan can register the
    /// living of the other side as enemies and the dead of its own as fallen friends.
    /// </remarks>
    public List<AiDetectionCandidate> CandidatesFor(
        SoldierView self,
        Igi1HumanAi brain,
        Vector3d playerFeet,
        Vector3d playerMark,
        bool playerAlive,
        bool playerCrouching)
    {
        List<AiDetectionCandidate> humans =
        [
            new(AiDetectionCandidate.PlayerId, brain.PlayerIsEnemy, playerAlive, playerMark, playerFeet,
                playerCrouching, _context.PlayerTarget),
        ];
        foreach ((SoldierView view, Igi1HumanAi other) in _context.Brains)
        {
            if (ReferenceEquals(view, self) || !ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                continue;
            }

            Vector3d soldierFeet = new(
                view.Position.X,
                view.Position.Y,
                _context.SnapToGround(view.Position.X, view.Position.Y, view.Position.Z));
            // The mark a sight ray aims at is the human's view position (0x465820): its origin raised 0.7 m, or 0.35 m
            // with the crouch byte -- so it kneels with a kneeling soldier and lies with a corpse.
            Vector3d soldierMark = SoldierContext.ViewPosition(view, other);
            AiCombatTarget record = _npcTargets.GetValueOrDefault(view)
                ?? (_npcTargets[view] = new AiCombatTarget($"Soldier{view.Soldier.Id}"));
            humans.Add(new AiDetectionCandidate(
                view.Soldier.Id,
                view.Soldier.Team != self.Soldier.Team,
                !view.IsDead && other.Activity != AiSoldierActivity.Dead && !_context.HealthOf(view).IsDead,
                soldierMark,
                soldierFeet,
                Crouching: other.Body.CrouchByte,
                record,
                SoldierContext.OccupantOrigin(view)));
        }

        return humans;
    }

    /// <summary>Whether an AI sight ray from one point to another is clear, for scenario runs.</summary>
    public bool ClearSightForTesting(Vector3d from, Vector3d to) =>
        SightTraceForTesting is { } trace
        && HasLineOfSight(trace, from, to, Igi1HumanAi.SightObstructionTolerance);

    /// <summary>What an AI sight ray from one point to another meets first, for scenario traces.</summary>
    public string DescribeSightForTesting(Vector3d from, Vector3d to)
    {
        if (SightTraceForTesting is not { } trace)
        {
            return "no trace";
        }

        bool clear = HasLineOfSight(trace, from, to, Igi1HumanAi.SightObstructionTolerance);
        WorldTraceRequest request = new(from, to, WorldTraceMask.World) { TracePortals = false };
        Vector3d along = to - from;
        return trace(in request, out WorldTraceHit hit)
            ? $"clear={clear} facing={((hit.Normal.X * along.X) + (hit.Normal.Y * along.Y) + (hit.Normal.Z * along.Z) < 0 ? "front" : "BACK")} hit {hit.Kind} at {hit.Distance / 4096.0:0.00} m of {Vector3d.Distance(from, to) / 4096.0:0.00} m, fraction {hit.Fraction:0.000}, material {hit.Material} '{hit.Label}' owner {hit.Owner?.Id}, at ({hit.Position.X / 4096.0:0.0},{hit.Position.Y / 4096.0:0.0},{hit.Position.Z / 4096.0:0.0})"
            : $"clear={clear} no contact";
    }

    /// <summary>Rebuilds a soldier's palette as far as its eye goes -- <c>0x45F240</c>: the eye bone's matrix and its offset.</summary>
    private static void RebuildEye(SoldierAgent member, SoldierView view, Igi1HumanAi brain)
    {
        member.EyeOrientation = PosedEye(view, brain);

        // The palette holds each bone's offset from the origin (Human+32), turned by the body's matrix: the eye's and the
        // gun's are kept that way, and follow the origin until the next rebuild. Probed on mission 4: squad member 405's
        // gun matched its hand in this pose -- the clocks a tick on -- on every tick a rebuild had run, and otherwise
        // trailed the body by the offset it last had.
        BonePose pose = view.UnrotatedPoseAhead();
        Matrix3f toWorld = view.Orientation.Transposed();
        if (HumanAttachPoints.AiEye < pose.BoneCount)
        {
            Vector3f eye = toWorld.Transform(pose.Positions[HumanAttachPoints.AiEye]);
            member.EyeOffset = new Vector3d(eye.X, eye.Y, eye.Z);
        }

        if (HumanAttachPoints.ThirdPersonWeapon < pose.BoneCount)
        {
            Vector3f hand = toWorld.Transform(pose.Positions[HumanAttachPoints.ThirdPersonWeapon]);
            member.GunOffset = new Vector3d(hand.X, hand.Y, hand.Z);
        }
    }

    /// <summary>The eye matrix a palette rebuilt now would hold: the body's matrix times the eye bone's posed rotation.</summary>
    /// <param name="view">The soldier's rig.</param>
    /// <param name="brain">Its brain, whose matrix is the body's (Human+120).</param>
    /// <returns>The eye's orientation, or the view's own when the rig has no eye bone.</returns>
    private static Matrix3f PosedEye(SoldierView view, Igi1HumanAi brain)
    {
        // The palette is rebuilt between ticks, from clocks already a tick past the pose the body was stepped with.
        BonePose pose = view.UnrotatedPoseAhead();
        return HumanAttachPoints.AiEye < pose.BoneCount
            ? brain.Orientation * pose.Rotations[HumanAttachPoints.AiEye]
            : brain.ViewOrientation;
    }

    /// <summary>
    /// What an AI sight ray does to the humans it passes: rebuilds their bone palettes, and so their eyes.
    /// </summary>
    /// <param name="from">The ray's start, the looker's eye, in world units.</param>
    /// <param name="to">The ray's end, the target's mark, in world units.</param>
    /// <param name="lookerId">The looking soldier's task id.</param>
    /// <param name="targetId">The looked-at human's id.</param>
    /// <remarks>
    /// <para>
    /// <c>0x489B20</c> casts through <c>0x4CE410</c>, which visits every dynamic object along the whole segment
    /// (message 8, <c>0x4C8130</c>) before it consults static geometry -- so a wall between does not spare anyone.
    /// The human filter it hands the visit (<c>0x42FC50</c>) admits every human but the looker and the target. A
    /// human it admits is tested by <c>0x45F580</c>: the segment against a sphere of the model's bound
    /// (<c>Human+160</c>) about the origin (<c>Human+32</c>), the perpendicular distance from the line and the two
    /// boxes' overlap. On a hit <c>0x45F800</c> rebuilds the palette (<c>0x45F240</c>).
    /// </para>
    /// <para>
    /// Probed on mission 4: squad member 404 ran beside 405, and every 12 ticks guard 411's ray to 405 refreshed
    /// 404 -- never 405 itself. 404's ray to 411 at tick 4544 refreshed bystander 403, whose eye then turned
    /// south and saw guards 409 and 410 three ticks later.
    /// </para>
    /// </remarks>
    private void RefreshEyesAlong(Vector3d from, Vector3d to, int lookerId, int targetId)
    {
        foreach (SoldierModelGroup group in _context.Roster)
        {
            foreach (SoldierAgent member in group.Agents)
            {
                SoldierView view = member.View;
                if (view.Soldier.Id == lookerId || view.Soldier.Id == targetId || view.IsDead
                    || !ConditionalContainerGate.IsTaskActive(view.Soldier)
                    || !_context.Brains.TryGetValue(view, out Igi1HumanAi? brain))
                {
                    continue;
                }

                double radius = BoundUnitsOf(view);
                if (SegmentMeetsSphere(from, to, SoldierContext.OccupantOrigin(view), radius))
                {
                    RebuildEye(member, view, brain);
                    SoldierSimulation.PaletteRebuildTraced?.Invoke(view.Soldier.Id, "ray", from, to);
                }
            }
        }
    }
}
