using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The noises the level's soldiers hear: gunshots, blasts, impacts, steps, grenades and rounds going by, each broadcast
/// to every human on the level's list and weighed by the listening brain.
/// </summary>
/// <remarks>
/// The engine's broadcast (<c>0x450CE0</c>) sends one record to every human, each taking it in its own handler. This
/// game's soldiers read the record's <c>Igi1Noise</c> context, the test the sender wrote for them; another game's
/// humans read its fields. Each delivery is counted and logged at most once a second, the receipt half of the
/// <c>[aiflow]</c> diff.
/// </remarks>
/// <param name="context">The soldiers, their brains and the player's side.</param>
internal sealed class SoldierNoiseBroadcaster(SoldierContext context)
{
    // A human's noise as its senders post it (0x463920, 0x479780): in range, of a living human, from the other side.
    private const AiEventFilter HumanNoiseFilter = AiEventFilter.InRange | AiEventFilter.SubjectIsHuman | AiEventFilter.OtherSide;

    private readonly SoldierContext _context = context;

    private int _lastNoiseLogTick = -100;

    /// <summary>Gets the level's humans in the order they joined the human list, every game's, which AI events are sent to.</summary>
    /// <remarks>Made when the soldiers are animated, from the level's tree; a guard spawned since joins at the end.</remarks>
    public List<IHumanAiEventTarget> Humans { get; } = [];

    /// <summary>Gets or sets the humans another game made for the level, which hear its AI events after its own.</summary>
    public LevelJoiners? Joined { get; set; }

    /// <summary>Delivers a gunshot to every soldier in earshot — the hearing side of <see cref="AiEvent.Gunshot"/>.</summary>
    /// <param name="origin">Where the shot was fired, in world units.</param>
    /// <param name="silenced">Whether the weapon is silenced (cuts the range to a thirtieth).</param>
    /// <param name="except">A soldier not to notify — the shooter, which does not hear its own shot as news.</param>
    /// <remarks>
    /// The range gate (30 m, or 1 m silenced) is applied per soldier in
    /// <see cref="Igi1HumanAi.HearGunshot(Vector3d, bool)"/>.
    /// The event is queued and pumped on the soldier's next tick; what it does about it is its archetype
    /// script's business.
    /// </remarks>
    public void NotifyGunshot(Vector3d origin, bool silenced, SoldierView? except = null)
        => NotifyGunshot(
            origin,
            silenced ? AiScriptApi.SilencedGunshotRange : AiScriptApi.GunshotRange,
            except);

    /// <summary>Delivers a gunshot using the firing weapon's exact AI detection range.</summary>
    /// <param name="origin">Where the shot was fired, in world units.</param>
    /// <param name="range">The weapon's AI detection range, in world units.</param>
    /// <param name="except">A soldier not to notify: the shooter.</param>
    /// <param name="line">
    /// The line of fire, as long as the gun reaches, which another game's humans read (<see cref="HumanAiEvent.Direction"/>);
    /// this game's brains hear only where the shot was.
    /// </param>
    public void NotifyGunshot(Vector3d origin, double range, SoldierView? except = null, Vector3f line = default)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }
            if (except is not null && _context.Brains.TryGetValue(except, out Igi1HumanAi? shooter) && ReferenceEquals(shooter, brain))
            {
                return false;
            }

            // The record carries the shooter's side (the player's, when nobody is excepted), and a
            // listener on that side does not admit it.
            int shooterTeam = except?.Soldier.Team ?? _context.PlayerTeam;
            int shooterId = except?.Soldier.Id ?? AiDetectionCandidate.PlayerId;
            return brain.HearGunshot(origin, range, fromSameSide: shooterTeam == view.Soldier.Team, shooterId);
        });
        Broadcast(noise, AiEvent.Gunshot, except?.Soldier.Team ?? _context.PlayerTeam, HumanNoiseFilter, range, origin, (GameTask?)except?.Soldier ?? _context.PlayerTask, line);
        ReportNoise("gunshot", noise.Heard, range);
    }

    /// <summary>Delivers IGI1's type-21 explosion alert to every active soldier in the blast envelope.</summary>
    /// <param name="origin">The explosion centre in engine units.</param>
    /// <param name="rangeUnits">The authored full-radius plus falloff envelope.</param>
    /// <remarks>
    /// This is intentionally separate from <see cref="NotifyGunshot(Vector3d, double, SoldierView?, Vector3f)"/>. Retail registers and queues
    /// <c>AIEVENT_EXPLOSION</c> (21), so an explosive object must not masquerade as a rifle shot.
    /// </remarks>
    public void NotifyExplosion(
        Vector3d origin,
        double rangeUnits = Igi1HumanAi.FlashbangEventRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }

            return brain.ReceiveExplosion(origin, rangeUnits);
        });
        Broadcast(noise, AiEvent.Explosion, -1, AiEventFilter.InRange, rangeUnits, origin, null);
        ReportNoise("explosion", noise.Heard, rangeUnits);
    }

    /// <summary>Delivers a player's world-impact event to every guard inside its authored radius.</summary>
    /// <param name="impact">The selected nearest world contact.</param>
    /// <param name="shooterPosition">The player's live position for source-based script actions.</param>
    /// <param name="radiusInUnits">Five metres for a round; five or ten metres for a landing.</param>
    /// <param name="flight">
    /// A round's flight to where it struck, which another game's humans tell a round's impact from a landing by
    /// (<see cref="HumanAiEvent.Direction"/>); nought for anything else.
    /// </param>
    public void NotifyGroundImpact(
        Vector3d impact,
        Vector3d shooterPosition,
        double radiusInUnits = Igi1HumanAi.GroundImpactRadiusInUnits,
        Vector3f flight = default)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }
            return brain.ReceiveGroundImpact(impact, shooterPosition, radiusInUnits);
        });
        Broadcast(noise, AiEvent.GroundImpact, _context.PlayerTeam, HumanNoiseFilter, radiusInUnits, impact, _context.PlayerTask, flight);
        ReportNoise("impact", noise.Heard, radiusInUnits);
    }

    /// <summary>Delivers the player's footstep noise to every guard the step's radius reaches.</summary>
    /// <param name="step">Where the foot landed, in units.</param>
    /// <param name="walkerPosition">The player's live position.</param>
    /// <param name="radiusUnits">The ground material's hearing radius × 4096 — <c>0x463920</c>.</param>
    public void NotifyWalk(Vector3d step, Vector3d walkerPosition, double radiusUnits) =>
        NotifyHumanNoise(AiEvent.Walk, step, walkerPosition, radiusUnits);

    /// <summary>Delivers a noise the player makes to every guard it reaches — see <see cref="Igi1HumanAi.ReceiveHumanNoise"/>.</summary>
    /// <param name="type">The event: a walk, a door, a fence climb or a ladder step.</param>
    /// <param name="position">Where the noise is.</param>
    /// <param name="source">The player's live position.</param>
    /// <param name="radiusUnits">How far it carries.</param>
    public void NotifyHumanNoise(
        AiEvent type,
        Vector3d position,
        Vector3d source,
        double radiusUnits = Igi1HumanAi.HumanNoiseRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }
            return brain.ReceiveHumanNoise(type, position, source, radiusUnits);
        });
        Broadcast(noise, type, _context.PlayerTeam, HumanNoiseFilter, radiusUnits, position, _context.PlayerTask);
        ReportNoise(type.ToString().ToLowerInvariant(), noise.Heard, radiusUnits);
    }

    /// <summary>Delivers IGI1's visible grenade-takeoff event (type 17).</summary>
    public void NotifyGrenadeThrown(Vector3d origin)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }

            return brain.ReceiveGrenadeThrown(origin);
        });
        Broadcast(noise, AiEvent.GrenadeThrown, -1, AiEventFilter.InView, -1.0, origin, null);
        ReportNoise("grenade-thrown", noise.Heard, Igi1HumanAi.GrenadeLandRadiusInUnits);
    }

    /// <summary>Delivers IGI1's five-metre grenade-land event (type 18).</summary>
    public void NotifyGrenadeLand(
        Vector3d origin,
        double radiusUnits = Igi1HumanAi.GrenadeLandRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }

            return brain.ReceiveGrenadeLand(origin, radiusUnits);
        });
        Broadcast(noise, AiEvent.GrenadeLand, -1, AiEventFilter.InRange, radiusUnits, origin, null);
        ReportNoise("grenade-land", noise.Heard, radiusUnits);
    }

    /// <summary>Delivers IGI1's ten-metre in-view flashbang event (type 19).</summary>
    public void NotifyFlashbang(
        Vector3d origin,
        double radiusUnits = Igi1HumanAi.FlashbangEventRadiusInUnits)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }

            return brain.ReceiveFlashbang(origin, radiusUnits);
        });
        Broadcast(noise, AiEvent.Flashbang, -1, AiEventFilter.InView | AiEventFilter.InRange, radiusUnits, origin, null);
        ReportNoise("flashbang", noise.Heard, radiusUnits);
    }

    /// <summary>
    /// Offers a soldier's round to every other soldier it passes — <c>0x4606F0</c> for rounds the player did
    /// not fire.
    /// </summary>
    /// <param name="origin">The muzzle.</param>
    /// <param name="liveEnd">Where the round stopped: the surface, the body it met, or the end of its reach.</param>
    /// <param name="shooter">The soldier that fired.</param>
    /// <param name="struck">The soldier the round struck, which takes TAKINGDAMAGE rather than a near miss.</param>
    public void NotifyRoundPassed(Vector3d origin, Vector3d liveEnd, SoldierView shooter, SoldierView? struck)
    {
        ArgumentNullException.ThrowIfNull(shooter);
        int source = shooter.Soldier.Id;
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier)
                || ReferenceEquals(view, shooter)
                || ReferenceEquals(view, struck))
            {
                return false;
            }

            bool otherSide = view.Soldier.Team != shooter.Soldier.Team;
            return brain.ReceiveGunshotMiss(origin, liveEnd, shooter.Position, otherSide, source);
        });
        Broadcast(noise);
        ReportNoise("near-miss", noise.Heard, Igi1HumanAi.GunshotMissRadiusInUnits);
    }

    /// <summary>
    /// Delivers the original's separate near-miss event to every guard whose live eye lies within two
    /// metres of this projectile segment.
    /// </summary>
    public void NotifyGunshotMiss(
        Vector3d origin,
        Vector3d end,
        SoldierView? struck,
        Vector3d shooterPosition)
    {
        Igi1Noise noise = new((view, brain) =>
        {
            if (!ConditionalContainerGate.IsTaskActive(view.Soldier))
            {
                return false;
            }
            // A body the round actually damaged receives TAKINGDAMAGE, not the avoidance event for a miss.
            if (struck is not null && ReferenceEquals(struck, view))
            {
                return false;
            }

            return brain.ReceiveGunshotMiss(origin, end, shooterPosition);
        });
        Broadcast(noise);
        ReportNoise("near-miss", noise.Heard, Igi1HumanAi.GunshotMissRadiusInUnits);
    }

    /// <summary>One human's delivery of a broadcast event: this game's test for a soldier, or the fields of another game's.</summary>
    /// <param name="human">The soldier's task the event was sent to.</param>
    /// <param name="aiEvent">The event.</param>
    public void Deliver(HumanTask human, in HumanAiEvent aiEvent)
    {
        if (!_context.ViewsByTask.TryGetValue(human, out SoldierView? view) || !_context.Brains.TryGetValue(view, out Igi1HumanAi? brain))
        {
            return;
        }

        if (aiEvent.Context is Igi1Noise noise)
        {
            if (noise.Hear(view, brain))
            {
                noise.Heard++;
            }
        }
        else
        {
            HearOtherGame(view, brain, aiEvent);
        }
    }

    /// <summary>Logs one delivery of a noise, at most once a second across every kind.</summary>
    /// <param name="kind">The noise.</param>
    /// <param name="heard">How many soldiers took it.</param>
    /// <param name="radiusUnits">How far it carried.</param>
    public void ReportNoise(string kind, int heard, double radiusUnits)
    {
        if (heard == 0 || _context.TickCount - _lastNoiseLogTick < 30)
        {
            return;
        }

        _lastNoiseLogTick = _context.TickCount;
        _context.Logger.Information(
            SoldierContext.LogCategory + ".Flow",
            FormattableString.Invariant(
                $"noise {kind} heard by {heard} soldier(s) (radius {radiusUnits / 4096.0:0.0} m)"));
    }

    // The engine's broadcast (0x450CE0): the event to every human on the list, each taking it in its own handler. A near
    // miss is weighed listener by listener, so it carries no record of its own: another game's humans hear a round go by
    // through the level's bodies instead.
    private void Broadcast(Igi1Noise noise) =>
        Broadcast(noise, new HumanAiEvent(Type: -1, Team: -1, Flags: 0, Radius: 0f, Position: null, Source: null, Subject: null, Context: noise));

    // The broadcast of a record the way its sender posts it, which another game's humans read; this game's read the noise.
    private void Broadcast(
        Igi1Noise noise, AiEvent type, int team, AiEventFilter filter, double radius, Vector3d position, GameTask? source, Vector3f direction = default) =>
        Broadcast(noise, new HumanAiEvent((int)type, team, (int)filter, (float)radius, position, source, source, noise, direction));

    private void Broadcast(Igi1Noise noise, in HumanAiEvent aiEvent) =>
        HumanAiEvents.Send(
            (Humans.Count > 0 ? Humans : _context.Brains.Keys.Select(static view => (IHumanAiEventTarget)view.Soldier))
                .Concat(Joined?.Humans ?? []),
            aiEvent);

    // Another game's event, read by its fields: the admission tests its sender set, then the receiver this game's own senders
    // hand the same event to. The engine's flag bits are both games'; the human ones are answered by the source, whichever
    // game's human it is.
    private static void HearOtherGame(SoldierView view, Igi1HumanAi brain, in HumanAiEvent aiEvent)
    {
        if (!ConditionalContainerGate.IsTaskActive(view.Soldier) || aiEvent.Position is not { } at)
        {
            return;
        }

        AiEventFilter filter = (AiEventFilter)aiEvent.Flags;
        int team = view.Soldier.Team;
        IAiHuman? subject = aiEvent.Subject as IAiHuman;
        if ((filter.HasFlag(AiEventFilter.OtherSide) && aiEvent.Team == team)
            || (filter.HasFlag(AiEventFilter.SameSide) && aiEvent.Team != team)
            || (filter.HasFlag(AiEventFilter.SubjectIsHuman) && subject is null)
            || (filter.HasFlag(AiEventFilter.SubjectAlive) && subject is { IsDead: true })
            || (filter.HasFlag(AiEventFilter.SubjectDead) && subject is { IsDead: false }))
        {
            return;
        }

        Vector3d source = (aiEvent.Source as IAiHuman)?.Position ?? at;
        switch ((AiEvent)aiEvent.Type)
        {
            case AiEvent.Gunshot:
                brain.HearGunshot(at, aiEvent.Radius, fromSameSide: aiEvent.Team == team, aiEvent.Source?.Id ?? -1);
                break;
            case AiEvent.Explosion:
                brain.ReceiveExplosion(at, aiEvent.Radius);
                break;
            case AiEvent.GrenadeThrown:
                brain.ReceiveGrenadeThrown(at);
                break;
            case AiEvent.GrenadeLand:
                brain.ReceiveGrenadeLand(at, aiEvent.Radius);
                break;
            case AiEvent.Flashbang:
                brain.ReceiveFlashbang(at, aiEvent.Radius);
                break;
            case AiEvent.Walk or AiEvent.GroundImpact or AiEvent.Door or AiEvent.Fence or AiEvent.Ladder:
                brain.ReceiveHumanNoise((AiEvent)aiEvent.Type, at, source, aiEvent.Radius);
                break;
        }
    }

    // Project I.G.I.'s own context for an AI event: the listening brain's test for it, and how many heard.
    private sealed class Igi1Noise(Func<SoldierView, Igi1HumanAi, bool> hear)
    {
        public int Heard { get; set; }

        public bool Hear(SoldierView view, Igi1HumanAi brain) => hear(view, brain);
    }
}
