using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>What a soldier senses: the detection scan, its view cones and eye, and the events it hears and is sent.</content>
public sealed partial class Igi1HumanAi
{
    /// <summary>
    /// The fraction of the eye-to-mark segment beyond which an object contact no longer hides the target —
    /// <c>0x489B20</c>'s <c>fraction &gt;= 0.79000002</c>.
    /// </summary>
    /// <remarks>
    /// Terrain always blocks (<c>0x4CE410</c> forces its fraction to zero). An object — a crate, a wall, a
    /// building, most of what the levels are made of — hides the target only when it is nearer than this,
    /// so cover pressed up against the target leaves it visible.
    /// </remarks>
    public const double SightObstructionTolerance = 0.79000002;

    /// <summary>
    /// How far above its feet a standing human reports itself to detection — the Z offset the descriptor
    /// handler adds in <c>sub_465820</c>: 2826.24 units (0.69 m), stored as the dwords <c>0xE147AE14</c> and
    /// <c>0x40A6147A</c>.
    /// </summary>
    /// <remarks>
    /// Vision does <b>not</b> test the target's eye. The candidate describes itself for the sweep
    /// (<c>sub_45FE30</c>) as its <em>position plus this raise</em> — mid-body — and the cone test, the
    /// sight ray and the stored distance all take that one point. Testing the eye let a guard acquire a
    /// player whose head cleared a sill while retail's waist-height mark stayed hidden.
    /// </remarks>
    public static readonly double DetectionRaiseStandingInUnits =
        BitConverter.Int64BitsToDouble(0x40A6147AE147AE14);

    /// <summary>
    /// The crouched raise — <c>sub_465820</c>'s other constant, 1433.6 units (0.35 m), the dwords
    /// <c>0x66666666</c> and <c>0x40966666</c>.
    /// </summary>
    public static readonly double DetectionRaiseCrouchedInUnits =
        BitConverter.Int64BitsToDouble(0x4096666666666666);

    /// <summary>How far above its feet a standing human's origin (<c>Human+32</c>) is — 0.97 m.</summary>
    /// <remarks>
    /// The port keeps a human's position at its feet; IGI.exe keeps it at the model origin, which its ground
    /// query stands at the node or ground height plus the root bone's height (<c>0x462390</c> via
    /// <c>sub_48A340</c>). Measured on the retail game: the player landed at 0.97 m over the port's ground at
    /// the mission-1 spawn, and guard 2003's origin matches the port's drawn origin (the soldier model's
    /// lowest vertex is 0.970 m below it) to the centimetre.
    /// </remarks>
    public const double HumanOriginStandingInUnits = 0.97 * 4096.0;

    /// <summary>How far above its feet a crouched human's origin is — 0.46 m, measured on the retail player.</summary>
    public const double HumanOriginCrouchedInUnits = 0.46 * 4096.0;

    /// <summary>How far above its feet a human's detection mark is: its origin plus <c>sub_465820</c>'s raise.</summary>
    /// <param name="crouching">Whether the human is crouched.</param>
    /// <returns>The height in units.</returns>
    /// <remarks>
    /// <c>sub_465820</c> adds the raise to <c>Human+32</c>, the origin -- not the feet. Adding it to the
    /// feet put every mark, and so every sight ray, about a metre low.
    /// </remarks>
    public static double DetectionMarkAboveFeet(bool crouching) => crouching
        ? HumanOriginCrouchedInUnits + DetectionRaiseCrouchedInUnits
        : HumanOriginStandingInUnits + DetectionRaiseStandingInUnits;

    /// <summary>Tests whether the line between two world points is clear enough to see along.</summary>
    /// <param name="from">The eye.</param>
    /// <param name="to">The target.</param>
    /// <returns><see langword="true"/> when the target can be seen.</returns>
    public delegate bool LineOfSight(Vector3d from, Vector3d to);

    /// <summary>Returns whether a world hit leaves the target visible.</summary>
    /// <param name="targetDistance">The distance from the observer to the target.</param>
    /// <param name="blockerDistance">The distance from the observer to the nearest world hit.</param>
    /// <returns><see langword="true"/> when no world hit occurs before the target.</returns>
    public static bool WorldBlockerAllowsSight(double targetDistance, double blockerDistance) =>
        targetDistance <= 0.0 || blockerDistance >= targetDistance;

    /// <summary>Finds the ground height at a world position.</summary>
    /// <param name="x">World X, in engine units.</param>
    /// <param name="y">World Y, in engine units.</param>
    /// <param name="z">The Z to search downwards from, in engine units.</param>
    /// <param name="groundZ">Receives the ground height when found.</param>
    /// <returns><see langword="true"/> when a surface was found.</returns>
    public delegate bool GroundQuery(double x, double y, double z, out double groundZ);

    /// <summary>Whether an event being handled lies behind the soldier — <c>AIFunction_IsEventBehind</c> (<c>0x44E860</c>).</summary>
    /// <returns>The answer for the event type being dispatched; <see langword="false"/> outside a dispatch.</returns>
    public bool IsEventBehind() => _currentEventType is { } type && IsEventBehind(type);

    /// <summary>Whether any queued event of a type lies behind the soldier's eye — <c>0x44E860</c>.</summary>
    /// <param name="type">The event type whose queue is walked.</param>
    /// <returns><see langword="true"/> when one does.</returns>
    /// <remarks>
    /// Every record in the queue is tested, from the eye (<c>HumanAI+0x38</c>) along the view's forward axis
    /// (<c>view[1]</c>, <c>view[4]</c>, <c>view[7]</c>), pitch included: one behind the plane answers yes. An empty
    /// queue answers no.
    /// </remarks>
    public bool IsEventBehind(AiEvent type)
    {
        Vector3d eye = EyePosition();
        Vector3f forward = EyeOrientation().Column2;
        foreach (AiEventRecord record in Events.RecordsOf(type))
        {
            double dx = record.Position.X - eye.X;
            double dy = record.Position.Y - eye.Y;
            double dz = record.Position.Z - eye.Z;
            if (!((forward.Z * dz) + (forward.X * dx) + (forward.Y * dy) >= 0.0))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Delivers a gunshot the soldier might hear — posts <see cref="AiEvent.Gunshot"/> if it is in earshot.</summary>
    /// <param name="shotPosition">Where the shot was fired, in engine units.</param>
    /// <param name="silenced">Whether the weapon is silenced, which cuts the audible range to a thirtieth.</param>
    /// <returns><see langword="true"/> when the shot was within earshot and an event was queued.</returns>
    /// <remarks>
    /// <para>
    /// The ranges are the two constants the AI module registers: <see cref="AiScriptApi.GunshotRange"/> (30 m)
    /// and <see cref="AiScriptApi.SilencedGunshotRange"/> (1 m) — a silenced weapon is worth carrying because
    /// it is heard a thirtieth as far. The event carries the shot's position, so a script can ask
    /// <c>AIFunction_GetEventDistance</c>, and it is pumped on the soldier's next tick like any other event —
    /// so a guard already busy with a higher-priority event this tick never learns of the shot, which is the
    /// original's discard-not-defer rule.
    /// </para>
    /// <para>
    /// <b>Recorded as a delivery, not a reaction.</b> What a guard <em>does</em> about a heard shot is its
    /// archetype script's <c>AIEVENT_GUNSHOT</c> branch; this only puts the event where the script can see
    /// it. A projectile passing close to the head is a separate <see cref="AiEvent.GunshotMiss"/> delivery,
    /// modelled by <see cref="ReceiveGunshotMiss(Vector3d, Vector3d, Vector3d)"/>.
    /// </para>
    /// </remarks>
    public bool HearGunshot(Vector3d shotPosition, bool silenced)
        => HearGunshot(
            shotPosition,
            silenced ? AiScriptApi.SilencedGunshotRange : AiScriptApi.GunshotRange,
            fromSameSide: !PlayerIsEnemy);

    /// <summary>Delivers a gunshot using the weapon record's exact AI detection range.</summary>
    /// <param name="shotPosition">Where the round was fired.</param>
    /// <param name="range">The weapon's <c>AI Detection Range</c>, in engine units.</param>
    /// <returns><see langword="true"/> when a gunshot event was posted.</returns>
    public bool HearGunshot(Vector3d shotPosition, double range)
        => HearGunshot(shotPosition, range, fromSameSide: !PlayerIsEnemy);

    /// <summary>
    /// Delivers a gunshot using the weapon record's exact AI detection range, knowing whose side fired it.
    /// </summary>
    /// <param name="shotPosition">Where the round was fired.</param>
    /// <param name="range">The weapon's <c>AI Detection Range</c>, in engine units.</param>
    /// <param name="fromSameSide">Whether the shooter is on this soldier's own side.</param>
    /// <returns><see langword="true"/> when a gunshot event was posted.</returns>
    /// <remarks>
    /// The weapon's fire handler (<c>0x479780</c>) posts the record with the shooter's side and filter
    /// <c>0x46</c>, and admission (<c>0x450110</c>) rejects a record whose side is the listener's own under
    /// bit <c>0x40</c>. So nobody hears their own side firing: a guard does not come running because a
    /// teammate opened up, and a friendly does not react to the player's shots.
    /// </remarks>
    public bool HearGunshot(Vector3d shotPosition, double range, bool fromSameSide) =>
        HearGunshot(shotPosition, range, fromSameSide, AiDetectionCandidate.PlayerId);

    /// <summary>Delivers a gunshot, knowing whose side fired it and who fired it.</summary>
    /// <param name="shotPosition">Where the round was fired.</param>
    /// <param name="range">The weapon's <c>AI Detection Range</c>, in engine units.</param>
    /// <param name="fromSameSide">Whether the shooter is on this soldier's own side.</param>
    /// <param name="shooter">The shooter's id — the record's source and subject (<c>0x479780</c>).</param>
    /// <returns><see langword="true"/> when a gunshot event was posted.</returns>
    public bool HearGunshot(Vector3d shotPosition, double range, bool fromSameSide, int shooter)
    {
        if (Activity == AiSoldierActivity.Dead || fromSameSide)
        {
            return false;
        }

        range = Math.Max(0.0, range);
        double distance = Vector3d.Distance(Position, shotPosition);
        if (distance > range)
        {
            return false;
        }

        bool posted = Events.Post(
            new AiEventRecord(AiEvent.Gunshot, -1, AiEventFilter.None, (float)range, shotPosition, shooter, shooter),
            owner: RuntimeId,
            worldTests: null);
        if (!posted)
        {
            return false;
        }

        LastEventDistance = distance;

        RefreshBelievedTarget(shooter);

        return true;
    }

    /// <summary>Points the target record at where the target really is, after an admitted stimulus.</summary>
    /// <remarks>
    /// <c>sub_48A920</c> runs <c>sub_451380</c> over the shared record whenever the tick drained one of
    /// ALERT, WALK, GROUNDIMPACT, DOOR, FENCE, LADDER, TAKINGDAMAGE, GUNSHOT, GRENADELAND or GUNSHOTMISS,
    /// even though the target is not in sight. EXPLOSION is not among them. It also has to run for the
    /// first stimulus an idle guard receives, or SetCombat starts it fighting the zero vector.
    /// </remarks>
    /// <param name="subject">The human the stimulus names; the player unless the caller says otherwise.</param>
    private void RefreshBelievedTarget(int subject = AiDetectionCandidate.PlayerId)
    {
        // Before a fight the stimulus turns the soldier's attention to whoever it names; in one, the record
        // keeps its human and is only refreshed.
        if (!_combatInstalled && _tracked.ContainsKey(subject))
        {
            _targetId = subject;
        }

        if (!HasTarget)
        {
            return;
        }

        _lastSeenFeet = TargetFeet;
        SetTarget(TargetRecord);
        Target?.Refresh(_lastSeenFeet);
    }

    /// <summary>
    /// How far the EXPLOSION event of a demolition, a placed mine, a missile, a wreck or a camera carries —
    /// 25 m (<c>0x47C80000</c>), whatever the size of the blast itself.
    /// </summary>
    /// <remarks>
    /// Every poster hands <c>0x480FE0</c> its own fixed radius for the AI event, separate from the blast's
    /// damage radius: <c>ExplodeObject</c> (<c>0x481A60</c>), <c>ExplodeMagicObj</c> (<c>0x43C0B0</c>), the
    /// missile (<c>0x428390</c>), a destroyed vehicle (<c>0x42E5A0</c>), the proximity mine
    /// (<c>0x476340</c>) and the security camera (<c>0x441770</c>) all pass this.
    /// </remarks>
    public const double LargeExplosionEventRadiusInUnits = 25.0 * 4096.0;

    /// <summary>How far a hand grenade's EXPLOSION event carries — 10 m (<c>0x47200000</c>, <c>0x476AC0</c>).</summary>
    public const double GrenadeExplosionEventRadiusInUnits = 10.0 * 4096.0;

    /// <summary>Delivers the native type-21 explosion event when the blast reaches this soldier.</summary>
    /// <param name="explosionPosition">The world-space centre of the explosion.</param>
    /// <param name="rangeInUnits">The event's own radius, fixed by the kind of explosion, in engine units.</param>
    /// <returns><see langword="true"/> when the event was queued.</returns>
    /// <remarks>
    /// IGI1 registers <c>AIEVENT_EXPLOSION</c> as event 21 in the native AI registrar at
    /// <c>0x44EEB0</c>/<c>0x450B20</c>. It is a sensed event with no human subject, so the native
    /// equivalent is an in-range position record rather than a gunshot record or a subject filter.
    /// </remarks>
    public bool ReceiveExplosion(
        Vector3d explosionPosition,
        double rangeInUnits = GrenadeLandRadiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || !double.IsFinite(rangeInUnits) || rangeInUnits < 0.0)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, explosionPosition);
        if (distance > rangeInUnits)
        {
            return false;
        }

        if (!Events.Post(
                new AiEventRecord(
                    AiEvent.Explosion,
                    -1,
                    AiEventFilter.InRange,
                    (float)rangeInUnits,
                    explosionPosition,
                    -1,
                    0),
                owner: RuntimeId,
                worldTests: null))
        {
            return false;
        }

        LastEventDistance = distance;
        return true;
    }

    /// <summary>
    /// Delivers the distinct <see cref="AiEvent.GunshotMiss"/> event when a live round passes within two
    /// metres of this human's eye.
    /// </summary>
    /// <param name="segmentStart">The beginning of the round's live segment.</param>
    /// <param name="segmentEnd">The end of the round's live segment.</param>
    /// <param name="shooterPosition">The originator's position, used by actions which inspect its range.</param>
    /// <returns><see langword="true"/> when the strict two-metre test passed and the event was posted.</returns>
    /// <remarks>
    /// IGI2's symbolised <c>GunShot_FindHearableIntersections</c> exposes the engine algorithm shared with
    /// IGI1: test the start, end, then the closest projected point on the segment, and admit only a distance
    /// <em>less than</em> 8192 units. This is not ordinary weapon hearing; that remains one
    /// <see cref="AiEvent.Gunshot"/> per fired round at the weapon record's AI detection range.
    /// </remarks>
    public bool ReceiveGunshotMiss(Vector3d segmentStart, Vector3d segmentEnd, Vector3d shooterPosition) =>
        ReceiveGunshotMiss(segmentStart, segmentEnd, shooterPosition, PlayerIsEnemy, PlayerSource);

    /// <summary>Delivers a near miss from any shooter — <c>0x4606F0</c>, which asks only whose side fired.</summary>
    /// <param name="segmentStart">The beginning of the round's live segment.</param>
    /// <param name="segmentEnd">The end of the round's live segment, where it stopped.</param>
    /// <param name="shooterPosition">The shooter's position, for the actions that read the event's source.</param>
    /// <param name="fromOtherSide">Whether the shooter is on the other side from this soldier.</param>
    /// <param name="source">The shooter's id — a soldier's task id, or <c>-1</c> for the player.</param>
    /// <returns><see langword="true"/> when the strict two-metre test passed and the event was posted.</returns>
    /// <remarks>
    /// The handler runs for any round, the player's or a soldier's: a human the round passes that is not on
    /// the shooter's side broadcasts GUNSHOTMISS at each hearable point, with the shooter's side and filter
    /// <c>0x46</c>, so only the other side hears it — a guard hears a friendly's round going past, and a
    /// friendly a guard's.
    /// </remarks>
    public bool ReceiveGunshotMiss(
        Vector3d segmentStart, Vector3d segmentEnd, Vector3d shooterPosition, bool fromOtherSide, int source)
    {
        if (Activity == AiSoldierActivity.Dead || !fromOtherSide)
        {
            return false;
        }

        Vector3d eye = EyePosition();
        Vector3d closest = Vector3d.ClosestOnSegment(segmentStart, segmentEnd, eye);
        double distance = Vector3d.Distance(eye, closest);
        if (distance >= GunshotMissRadiusInUnits)
        {
            return false;
        }

        // -1 is the renderer's player-originator sentinel. The event position is the actual nearest point
        // on the round, while BeginFallFlat resolves the source separately to the player's live position.
        if (!Events.Post(
                new AiEventRecord(AiEvent.GunshotMiss, -1, AiEventFilter.None, 0.0f, closest, source, source),
                owner: RuntimeId,
                worldTests: null))
        {
            return false;
        }

        LastEventDistance = distance;
        RefreshBelievedTarget(source);
        return true;
    }

    /// <summary>Takes a round's near-miss point, as <c>0x4606F0</c> broadcasts it before the round is marched.</summary>
    /// <param name="point">The nearest point on the round's line to the human it passed, in world units.</param>
    /// <param name="fromOtherSide">Whether the shooter is on another side from this soldier (filter <c>0x40</c>).</param>
    /// <param name="source">The shooter's id.</param>
    /// <param name="shooterPosition">The shooter's position, for the actions that read the event's source.</param>
    /// <returns>Whether the event was admitted.</returns>
    /// <remarks>
    /// The broadcast carries radius 8192 and filter <c>0x46</c>; <c>0x450110</c> measures the radius from the
    /// AI's own position (<c>+32</c>), inclusive.
    /// </remarks>
    public bool ReceiveRoundPassing(Vector3d point, bool fromOtherSide, int source, Vector3d shooterPosition)
    {
        if (Activity == AiSoldierActivity.Dead || !fromOtherSide)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, point);
        if (distance > GunshotMissRadiusInUnits)
        {
            return false;
        }

        if (!Events.Post(
                new AiEventRecord(AiEvent.GunshotMiss, -1, AiEventFilter.None, 0.0f, point, source, source),
                owner: RuntimeId,
                worldTests: null))
        {
            return false;
        }

        LastEventDistance = distance;
        RefreshBelievedTarget(source);
        return true;
    }

    // The source a player-originated record carries -- the renderer's sentinel for "the player".
    private const int PlayerSource = -1;

    /// <summary>The strict near-miss radius: two metres, or 8192 engine units.</summary>
    public const double GunshotMissRadiusInUnits = 2.0 * 4096.0;

    /// <summary>
    /// Delivers the impact event raised when an enemy round strikes the world within five metres.
    /// </summary>
    /// <param name="impactPosition">The actual world contact, in engine units.</param>
    /// <param name="shooterPosition">The player's live position, used by source-distance action gates.</param>
    /// <param name="radiusInUnits">
    /// The event radius. Bullet impacts use five metres; a damaging player landing uses ten.
    /// </param>
    /// <returns><see langword="true"/> when the inclusive radius test admitted the event.</returns>
    /// <remarks>
    /// A Mission 1 capture of IGI1's broadcaster at <c>0x450CE0</c> records type 11 with filter
    /// <c>0x46</c>, radius <c>5.00 m</c>, and the projectile's real contact position. This is separate from
    /// both muzzle audibility and the strict two-metre <see cref="AiEvent.GunshotMiss"/> test, so even a
    /// suppressed round wakes a guard when it hits the wall or ground beside them.
    /// </remarks>
    public bool ReceiveGroundImpact(
        Vector3d impactPosition,
        Vector3d shooterPosition,
        double radiusInUnits = GroundImpactRadiusInUnits) =>
        ReceiveHumanNoise(AiEvent.GroundImpact, impactPosition, shooterPosition, radiusInUnits);

    /// <summary>The IGI1 bullet-impact reaction radius: five metres, or 20480 engine units.</summary>
    public const double GroundImpactRadiusInUnits = 5.0 * 4096.0;

    /// <summary>Delivers the footstep-noise event a walking enemy raises — <c>0x463920</c>'s tail.</summary>
    /// <param name="stepPosition">Where the foot landed — the walker's cached ground point, in units.</param>
    /// <param name="walkerPosition">The walker's live position, used by source-distance action gates.</param>
    /// <param name="radiusInUnits">How far the step carries — the ground material's own figure × 4096.</param>
    /// <returns><see langword="true"/> when the step was close enough to queue.</returns>
    /// <remarks>
    /// The retail footstep handler posts <b>type 10 (<see cref="AiEvent.Walk"/>), filter 0x46</b> with the
    /// radius the ground material names, and <b>not at all while the walker crouches</b> — the caller holds
    /// that gate, because the crouch belongs to the walker, not the listener. <c>guard.qvm</c> answers WALK
    /// with <c>AIAction_SetCombat</c>, which is how a guard investigates footsteps it cannot see.
    /// </remarks>
    public bool ReceiveWalk(Vector3d stepPosition, Vector3d walkerPosition, double radiusInUnits) =>
        ReceiveHumanNoise(AiEvent.Walk, stepPosition, walkerPosition, radiusInUnits);

    /// <summary>The radius of the noise a door, a fence climb or a ladder step makes — 5 m (<c>0x46A00000</c>).</summary>
    public const double HumanNoiseRadiusInUnits = 5.0 * 4096.0;

    /// <summary>
    /// Delivers a noise an enemy human makes — footsteps, a landing or a round's impact, a door it opens or
    /// closes, a fence it climbs, a ladder step.
    /// </summary>
    /// <param name="type">
    /// <see cref="AiEvent.Walk"/>, <see cref="AiEvent.GroundImpact"/>, <see cref="AiEvent.Door"/>,
    /// <see cref="AiEvent.Fence"/> or <see cref="AiEvent.Ladder"/>.
    /// </param>
    /// <param name="position">Where the noise is — the human's own position for a door, fence or ladder.</param>
    /// <param name="source">The human's live position, for the actions that read the event's source.</param>
    /// <param name="radiusInUnits">How far it carries.</param>
    /// <returns><see langword="true"/> when the soldier heard it.</returns>
    /// <remarks>
    /// Each is broadcast to every AI with filter <c>0x46</c> — in range, from a live human, of the other
    /// side — and the range test passes at exactly the radius (<c>0x450110</c> rejects only
    /// <c>radius &lt; distance</c>). The posters are <c>0x463920</c> (walk), <c>0x408460</c> (landing),
    /// <c>0x40C270</c> (a door the player toggles, opened or picked), <c>0x411770</c> (every tick of a fence
    /// climb but its first and last) and the three ladder states' step (<c>0x40D6B0</c>, <c>0x40D920</c>,
    /// <c>0x40DA80</c>).
    /// </remarks>
    public bool ReceiveHumanNoise(AiEvent type, Vector3d position, Vector3d source, double radiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || !PlayerIsEnemy || radiusInUnits < 0.0)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, position);
        if (distance > radiusInUnits)
        {
            return false;
        }

        if (!Events.Post(
                new AiEventRecord(
                    type,
                    -1,
                    AiEventFilter.InRange | AiEventFilter.SubjectIsHuman | AiEventFilter.OtherSide,
                    (float)radiusInUnits,
                    position,
                    -1,
                    -1),
                owner: RuntimeId,
                worldTests: null))
        {
            return false;
        }

        LastEventDistance = distance;
        RefreshBelievedTarget();
        return true;
    }

    /// <summary>Receives the retail grenade-takeoff event (type 17).</summary>
    /// <param name="grenadePosition">The grenade task's position, in engine units.</param>
    /// <returns><see langword="true"/> when the in-view admission test passed.</returns>
    /// <remarks>
    /// IGI1's grenade task posts <c>AIEVENT_GRENADETHROWN</c> with filter <c>InView</c>, radius -1 and
    /// no human subject. It is not a gunshot and must not be routed through the hearing path: a guard
    /// that sees the grenade receives the event even when it is outside the gunshot radius.
    /// </remarks>
    public bool ReceiveGrenadeThrown(Vector3d grenadePosition)
    {
        if (Activity == AiSoldierActivity.Dead
            || !Vision.IsVisible(EyePosition(), EyeOrientation(), grenadePosition, Alerted))
        {
            return false;
        }

        if (!Events.Post(
                new AiEventRecord(
                    AiEvent.GrenadeThrown,
                    -1,
                    AiEventFilter.InView,
                    -1.0f,
                    grenadePosition,
                    0,
                    0),
                owner: RuntimeId,
                worldTests: null))
        {
            return false;
        }

        LastEventDistance = Vector3d.Distance(Position, grenadePosition);
        return true;
    }

    /// <summary>The retail grenade-land event radius: five metres, or 20480 engine units.</summary>
    public const double GrenadeLandRadiusInUnits = 5.0 * 4096.0;

    /// <summary>Receives the retail bounce event (type 18).</summary>
    /// <param name="grenadePosition">The grenade task's contact position.</param>
    /// <param name="radiusInUnits">The native event radius, five metres by default.</param>
    /// <returns><see langword="true"/> when the in-range admission test passed.</returns>
    public bool ReceiveGrenadeLand(
        Vector3d grenadePosition,
        double radiusInUnits = GrenadeLandRadiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || radiusInUnits < 0.0)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, grenadePosition);
        if (distance > radiusInUnits)
        {
            return false;
        }

        if (!Events.Post(
                new AiEventRecord(
                    AiEvent.GrenadeLand,
                    -1,
                    AiEventFilter.InRange,
                    (float)radiusInUnits,
                    grenadePosition,
                    0,
                    0),
                owner: RuntimeId,
                worldTests: null))
        {
            return false;
        }

        LastEventDistance = distance;
        RefreshBelievedTarget();
        return true;
    }

    /// <summary>The native flashbang AI event radius: ten metres, or 40960 engine units.</summary>
    public const double FlashbangEventRadiusInUnits = 10.0 * 4096.0;

    /// <summary>Receives the retail flashbang event (type 19).</summary>
    /// <param name="grenadePosition">The flash task's world position.</param>
    /// <param name="radiusInUnits">The native event radius, ten metres by default.</param>
    /// <returns><see langword="true"/> when both native view and range filters passed.</returns>
    public bool ReceiveFlashbang(
        Vector3d grenadePosition,
        double radiusInUnits = FlashbangEventRadiusInUnits)
    {
        if (Activity == AiSoldierActivity.Dead || radiusInUnits < 0.0)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, grenadePosition);
        if (distance > radiusInUnits
            || !Vision.IsVisible(EyePosition(), EyeOrientation(), grenadePosition, Alerted))
        {
            return false;
        }

        if (!Events.Post(
                new AiEventRecord(
                    AiEvent.Flashbang,
                    -1,
                    AiEventFilter.InView | AiEventFilter.InRange,
                    (float)radiusInUnits,
                    grenadePosition,
                    0,
                    0),
                owner: RuntimeId,
                worldTests: null))
        {
            return false;
        }

        LastEventDistance = distance;
        return true;
    }

    /// <summary>Posts the type-15 event a non-lethal hit delivers to a HumanAI.</summary>
    /// <param name="attackerPosition">The attacking human's position, used by distance/behind natives.</param>
    /// <returns><see langword="true"/> when the <see cref="AiEvent.TakingDamage"/> event was queued.</returns>
    /// <param name="attacker">The attacker's id — the record's source and subject (<c>0x4553E0</c>).</param>
    public bool ReceiveDamage(Vector3d attackerPosition, int attacker = AiDetectionCandidate.PlayerId)
    {
        if (Activity == AiSoldierActivity.Dead)
        {
            return false;
        }

        bool posted = Events.Post(
            new AiEventRecord(
                AiEvent.TakingDamage,
                -1,
                AiEventFilter.None,
                0.0f,
                attackerPosition,
                attacker,
                attacker),
            owner: RuntimeId,
            worldTests: null);

        if (!posted)
        {
            return false;
        }

        LastEventDistance = Vector3d.Distance(Position, attackerPosition);
        RefreshBelievedTarget(attacker);
        return true;
    }

    /// <summary>The alert ping's reach — 28672 units, 7 m (<c>sub_48AB10</c> pushes float bits <c>0x46E00000</c>).</summary>
    public const double AlertPingRadiusInUnits = 28672.0;

    /// <summary>How often an engaged guard pings — every 20 ticks (<c>sub_48A920</c>'s <c>% 20</c> counter).</summary>
    public const int AlertPingPeriodTicks = 20;

    /// <summary>Receives a fighting neighbour's alert ping — <c>AIEVENT_ALERT</c> out of <c>sub_48AB10</c>.</summary>
    /// <param name="alerterPosition">The alerting guard's own position — the record's position field.</param>
    /// <param name="source">The alerting guard's runtime task identity, used by <c>SendResponse</c>.</param>
    /// <returns><see langword="false"/> when out of the ping's reach or dead.</returns>
    /// <remarks>
    /// The record: type 5 (<c>ALERT</c>), filter mask <c>0x26</c> (in range, a human subject, the same side),
    /// radius 7 m, source = the alerting guard, subject = the target it is fighting. Admission has no other
    /// gate: an already-alert guard, or one with an alert queued, still takes the record. The side test is
    /// the caller's, which knows both teams; the distance gate is applied here.
    /// </remarks>
    /// <param name="target">
    /// The alerting soldier's own target — the record's subject (<c>0x48AB10</c> sends
    /// <c>{self, target}</c>).
    /// </param>
    public bool ReceiveAlert(Vector3d alerterPosition, int source = 0, int target = AiDetectionCandidate.PlayerId)
    {
        if (Activity == AiSoldierActivity.Dead)
        {
            return false;
        }

        double distance = Vector3d.Distance(Position, alerterPosition);
        if (distance > AlertPingRadiusInUnits)
        {
            return false;
        }

        bool posted = Events.Post(
            new AiEventRecord(
                AiEvent.Alert, -1, AiEventFilter.None, (float)AlertPingRadiusInUnits,
                alerterPosition, source, target),
            owner: 0,
            worldTests: null);
        if (!posted)
        {
            return false;
        }

        LastEventDistance = distance;

        // The same target refresh arm handles both hearing and squad alert records. A nearby guard that
        // has never seen Jones therefore inherits the fighting guard's target record and turns on the
        // squad's current player position instead of world origin.
        RefreshBelievedTarget(target);

        return true;
    }

    /// <summary>Posts the type-6 acknowledgement sent back by another guard.</summary>
    public void ReceiveAlertResponse() =>
        Events.Post(AiEventRecord.Simple(AiEvent.AlertResponse), owner: 0, worldTests: null);

    /// <summary>Implements <c>AIFunction_SendResponse</c> against the current event's source task.</summary>
    public void SendResponse()
    {
        if (_currentEventSource != 0)
        {
            _responseRouter?.Invoke(_currentEventSource);
        }
    }

    /// <summary>Gives the soldier an occlusion test, so it stops seeing through walls.</summary>
    /// <param name="sight">The test, or <see langword="null"/> to see through everything.</param>
    public void SetLineOfSight(LineOfSight? sight) => _sight = sight;

    /// <summary>
    /// Gets or sets what hears of every sight ray this soldier casts: from, to, and the task id of the human it looks
    /// at (<see cref="AiDetectionCandidate.PlayerId"/> for the player).
    /// </summary>
    /// <remarks>
    /// The sight cast <c>0x489B20</c> -> <c>0x4CE410</c> visits every dynamic object along the whole segment before it
    /// consults static geometry, and the human filter it passes (<c>0x42FC50</c>) admits every human but the looker
    /// and the target. A human the segment meets has its bone palette rebuilt (<c>0x45F800</c> -> <c>0x45F240</c>),
    /// which is where its own eye's matrix comes from -- so being looked past turns a bystander's eye.
    /// </remarks>
    public Action<Vector3d, Vector3d, int>? SightCast { get; set; }

    /// <summary>Connects detection and gunfire to body-rig bone 5's live world position.</summary>
    public void SetEyeTransform(Func<Vector3d>? eyePosition, Func<Matrix3f>? eyeOrientation)
    {
        _eyePosition = eyePosition;
        _eyeOrientation = eyeOrientation;
    }

    /// <summary>
    /// Reports every detection gate as it stands this instant for a target eye — for the debug overlay, so a
    /// guard that will not detect the player can be asked <em>which</em> gate is failing.
    /// </summary>
    /// <param name="enemyEye">The target eye in engine units, the same point <c>Tick</c> is fed.</param>
    /// <returns>The distance in units, and the cone / line-of-sight / alert / currently-visible flags.</returns>
    /// <remarks>
    /// This recomputes the cone against a fresh eye snapshot and runs the stored occlusion test directly, so
    /// it reflects the geometry now rather than whatever the rate-limited sweep last stored. The last flag is
    /// the sweep's own latched answer.
    /// </remarks>
    public (double DistanceUnits, bool InCone, bool LineOfSightClear, bool Alerted, bool IsCurrentlyVisible) ProbeDetection(Vector3d enemyEye)
    {
        AiEyeSnapshot snapshot = Vision.Capture(EyePosition(), EyeOrientation(), Alerted);
        bool inCone = snapshot.IsVisible(enemyEye);
        bool clear = _sight is null || _sight(EyePosition(), enemyEye);
        return (Vector3d.Distance(Position, enemyEye), inCone, clear, Alerted, TargetSlot.IsVisible);
    }

    /// <summary>Takes this tick's humans: their descriptions update, and anyone gone from the list is dropped.</summary>
    private void OfferCandidates(IReadOnlyList<AiDetectionCandidate> candidates)
    {
        HashSet<int> offered = [];
        foreach (AiDetectionCandidate candidate in candidates)
        {
            offered.Add(candidate.Id);
            if (_tracked.TryGetValue(candidate.Id, out Igi1AiTrackedHuman? human))
            {
                human.Candidate = candidate;
            }
            else
            {
                _tracked[candidate.Id] = new Igi1AiTrackedHuman(candidate);
            }
        }

        foreach (int gone in _tracked.Keys.Where(id => !offered.Contains(id)).ToArray())
        {
            Unregister(_tracked[gone]);
            _tracked.Remove(gone);
        }

        // With nobody yet in mind, the soldier's attention rests on the first human on offer.
        if (!_tracked.ContainsKey(_targetId) && candidates.Count > 0)
        {
            _targetId = candidates[0].Id;
        }
    }

    /// <summary>One pass of the detection scan — <c>0x489750</c> and <c>0x4898D0</c>.</summary>
    private void Scan(int tick)
    {
        Register(tick);

        Eye = Vision.Capture(EyePosition(), EyeOrientation(), Alerted);
        AiEyeSnapshot snapshot = Eye;
        Vector3d eye = EyePosition();
        foreach (Igi1AiTrackedHuman human in _enemies.Concat(_fallenFriends))
        {
            Vector3d mark = human.Candidate.Mark;
            int looked = human.Candidate.Id;
            _schedule.Sweep(
                human.Slot,
                () => snapshot.IsVisible(mark),
                () =>
                {
                    SightCast?.Invoke(eye, mark, looked);
                    return _sight is null || _sight(eye, mark);
                },
                Vector3d.Distance(eye, mark));
        }
    }

    // The scan's classification: other side and alive into the enemy list, own side and dead into the
    // fallen-friend list, anything else out.
    private void Register(int tick)
    {
        foreach (Igi1AiTrackedHuman human in _tracked.Values)
        {
            AiDetectionCandidate candidate = human.Candidate;
            List<Igi1AiTrackedHuman>? list = candidate.OtherSide && candidate.Alive ? _enemies
                : !candidate.OtherSide && !candidate.Alive ? _fallenFriends
                : null;
            if (list is null)
            {
                Unregister(human);
            }
            else if (!human.Slot.IsRegistered)
            {
                human.Slot.Register(tick);
                list.Add(human);
            }
        }
    }

    private void Unregister(Igi1AiTrackedHuman human)
    {
        human.Slot.IsVisible = false;
        human.Slot.IsInCone = false;
        human.Slot.Unregister();
        _enemies.Remove(human);
        _fallenFriends.Remove(human);
    }

    private void PostDetection(AiEvent type, Igi1AiTrackedHuman human) =>
        Events.Post(
            new AiEventRecord(
                type, -1, AiEventFilter.None, 0.0f, human.Candidate.Feet, human.Candidate.Id, human.Candidate.Id),
            owner: 0,
            worldTests: null);

    /// <summary>The eye the cones are measured from — body-rig bone 5 when a live rig is attached.</summary>
    /// <returns>The eye position.</returns>
    /// <remarks>
    /// IGI1 snapshots palette offsets 684/688/692, which resolve to bone 5's position. Headless tests and
    /// callers without a rig fall back to the separation cylinder's read half-height.
    /// </remarks>
    public Vector3d EyePosition() => _eyePosition?.Invoke()
        ?? new Vector3d(Position.X, Position.Y, Position.Z + HumanSeparationEyeHeight);

    private Matrix3f EyeOrientation() => _eyeOrientation?.Invoke() ?? Orientation;

    /// <summary>Diagnostics: the eye matrix a sweep run now would test against -- the bound eye, or the body's.</summary>
    public Matrix3f CurrentEyeOrientation => EyeOrientation();

    /// <summary>The eye height fallback for a soldier with no attached body rig.</summary>
    public const double HumanSeparationEyeHeight = 1.8 * 4096.0;
}
