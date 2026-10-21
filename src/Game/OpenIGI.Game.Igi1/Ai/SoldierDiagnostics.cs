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
/// The soldiers' running commentary: the heartbeat, the fight, the channels a fight asks for, the events a brain drained
/// and the walk step, each shaped like the retail probe's line it is diffed against.
/// </summary>
/// <remarks>
/// Nothing here draws from the level's stream or changes a soldier: every line only reads, and each is rate-limited or
/// logged once per change, so a run is commentary rather than a per-tick flood.
/// </remarks>
/// <param name="context">The soldiers, their brains and the tick count.</param>
internal sealed class SoldierDiagnostics(SoldierContext context)
{
    private const string TraceLogCategory = SoldierContext.LogCategory + ".Trace";

    private readonly SoldierContext _context = context;

    private readonly HashSet<string> _reportedChannels = new(StringComparer.Ordinal);

    private readonly Dictionary<int, string> _lastGunnerRuntimeReports = [];

    private string _lastCombatReport = string.Empty;

    private int _lastCombatDetailTick;

    private int _lastDrainLogTick = -100;

    /// <summary>Gets how many walk steps have been sampled.</summary>
    public int TravelSamples { get; private set; }

    /// <summary>Gets the sampled steps' total, in units.</summary>
    public double TravelTotal { get; private set; }

    /// <summary>Gets the longest sampled step, in units.</summary>
    public double TravelMax { get; private set; }

    /// <summary>Records one patrolling soldier's step this tick.</summary>
    /// <param name="travel">The clip's forward delta, in units.</param>
    public void RecordTravel(double travel)
    {
        TravelSamples++;
        TravelTotal += travel;
        TravelMax = Math.Max(TravelMax, travel);
    }

    /// <summary>The liveness half of the [aiflow] diff, every ten seconds: the brains' activities, and the friendlies' detail.</summary>
    public void Heartbeat()
    {
        // The liveness half of the [aiflow] diff: proof the AI tick is running at all, ten seconds apart.
        // The patrol/activity counts ride the same line so a capture can show WHEN patrols engage after
        // the intro cutscene, without a separate per-tick flood.
        if (_context.TickCount % 300 == 1)
        {
            int onPatrol = _context.Brains.Values.Count(b => b.Route is not null);
            Dictionary<string, int> activity = [];
            foreach (Igi1HumanAi brain in _context.Brains.Values)
            {
                string key = brain.Activity.ToString();
                activity[key] = activity.GetValueOrDefault(key) + 1;
            }

            string activityText = string.Join(", ", activity.OrderByDescending(p => p.Value).Select(p => $"{p.Key}={p.Value}"));
            _context.Logger.Information(
                SoldierContext.LogCategory + ".Flow", $"heartbeat tick={_context.TickCount} brains={_context.Brains.Count} onPatrol={onPatrol} [{activityText}]");

            // Diagnostic-only: the Soldier.Trace category at Debug adds per-soldier detail for the friendlies
            // (ids 400-405, plus the heli crews by their mind ids), so a long lockstep run shows what they
            // actually do. At Trace it extends the same record to every active human, which is useful for
            // checking first-base guard grounding without changing the normal diagnostic volume.
            if (_context.Logger.IsEnabled(LogLevel.Debug, TraceLogCategory))
            {
                bool traceAll = _context.Logger.IsEnabled(LogLevel.Trace, TraceLogCategory);
                foreach (KeyValuePair<SoldierView, Igi1HumanAi> pair in _context.Brains)
                {
                    HumanSoldierTask soldier = pair.Key.Soldier;
                    HumanAiTask? mind = soldier.Children.OfType<HumanAiTask>().FirstOrDefault();
                    int mindId = mind?.Id ?? -1;
                    bool friendly = soldier.Id is >= 400 and <= 405
                        || soldier.Id == 428
                        || mindId is >= 540 and <= 544
                        || mindId is >= 1234 and <= 1238
                        || soldier.Id == 666;
                    if (!friendly && !traceAll)
                    {
                        continue;
                    }

                    Igi1HumanAi brain = pair.Value;
                    string traceTag = friendly ? "friendly" : "guard";
                    string route = brain.Route is null ? "-" : $"path {brain.Route.Path.Id} cmd={brain.Route.Current?.Kind}";
                    string last = brain.LastActions.Count == 0
                        ? "none"
                        : string.Join(",", brain.LastActions.Select(a => $"{a.Name.Split('_')[^1]}({string.Join(",", a.Arguments)}) "));
                    string pending = string.Join(",", Enumerable.Range(0, 24).Select(i => (AiEvent)i).Where(t => brain.Events.PendingCount(t) > 0).Select(t => $"{t}x{brain.Events.PendingCount(t)}"));
                    string dispatched = brain.LastDispatchedEvent?.ToString() ?? "-";
                    double groundZ = _context.SnapToGround(pair.Key.Position.X, pair.Key.Position.Y, pair.Key.Position.Z);
                    double groundGap = (pair.Key.Position.Z - groundZ) / WorldScene.UnitsPerMetre;
                    _context.Logger.Debug(
                        TraceLogCategory,
                        $"{traceTag} soldier {soldier.Id} mind {mindId} {brain.Activity} {route} " +
                        $"evt={dispatched} last={last} pending=[{pending}] lastTick={brain.LastTick} " +
                        $"anim={pair.Key.CurrentAnimation} rm={Math.Abs(pair.Key.RootMotion().Y):0.0} " +
                        $"pos=({pair.Key.Position.X / 4096.0:0.0},{pair.Key.Position.Y / 4096.0:0.0}) " +
                        $"bpos=({brain.Position.X / 4096.0:0.0},{brain.Position.Y / 4096.0:0.0}) cur={brain.CurrentNode} dest={brain.DestinationNode} " +
                        $"target={brain.Target?.Target ?? "-"} " +
                        $"ground={groundZ / WorldScene.UnitsPerMetre:0.00} gap={groundGap:0.000}m " +
                        $"fwd={brain.LastSteeringForward:0.0} rmT={brain.LastAdvanceRm:0.0} st={brain.LastSteeringOutcome} " +
                        $"moved={(brain.LastMovedTo is { } mv ? $"({mv.X / 4096.0:0.0},{mv.Y / 4096.0:0.0})" : "-")} facing={brain.LastFacingDeg:0.0} " +
                        $"wp={(brain.DebugWaypoint is { } wpp ? $"{wpp.X / 4096.0:0.000},{wpp.Y / 4096.0:0.000}" : "-")}");

                    foreach ((SoldierView otherView, Igi1HumanAi other) in _context.Brains)
                    {
                        if (ReferenceEquals(other, brain))
                        {
                            continue;
                        }

                        double dx = other.Position.X - brain.Position.X;
                        double dy = other.Position.Y - brain.Position.Y;
                        double h = Math.Sqrt((dx * dx) + (dy * dy)) / 4096.0;
                        if (h < 3.0)
                        {
                            _context.Logger.Debug(
                                TraceLogCategory,
                                $"{traceTag}   near s{soldier.Id}: s{otherView.Soldier.Id} m{otherView.Soldier.Children.OfType<HumanAiTask>().FirstOrDefault()?.Id} {h:0.000} m {other.Activity}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>A gunner's state once per change, and every ten seconds the timer its mission's script waits on.</summary>
    /// <param name="brain">The gunner.</param>
    /// <param name="scriptVariables">The level's variables, which hold the timer.</param>
    public void ReportGunner(Igi1HumanAi brain, ScriptVarRegistry? scriptVariables)
    {
        string report = $"runtime={brain.RuntimeId} gunnerId={brain.GunnerId} "
            + $"mounted={(brain.IsMounted ? "true" : "false")} "
            + $"task={(brain.MountedGun is null ? "-" : brain.MountedGun.TaskId.ToString(System.Globalization.CultureInfo.InvariantCulture))} "
            + $"activity={brain.Activity}";
        if (_lastGunnerRuntimeReports.GetValueOrDefault(brain.RuntimeId) != report)
        {
            _lastGunnerRuntimeReports[brain.RuntimeId] = report;
            _context.Logger.Information(SoldierContext.LogCategory + ".Gunner", $"{report}");
        }

        if (_context.TickCount % 300 == 1)
        {
            string timer = scriptVariables?.TryRead("LevelTimer_1608.nTick", out QvmValue timerValue) == true
                ? timerValue.ToString()
                : "missing";
            string dispatched = brain.LastDispatchedEvent?.ToString() ?? "-";
            _context.Logger.Information(
                SoldierContext.LogCategory + ".Gunner",
                $"diagnostic runtime={brain.RuntimeId} lastEvent={dispatched} "
                + $"timer1608={timer}");
        }
    }

    /// <summary>The pump half of the [aiflow] diff: the events a brain drained, at most once a second.</summary>
    /// <param name="brain">The brain that has just ticked.</param>
    public void ReportDrained(Igi1HumanAi brain)
    {
        // The pump half of the [aiflow] diff: an event that was QUEUED shows up here the tick it is
        // drained, whether or not any script acted on it. Noise delivered but never drained points at
        // the pump; drained but never acted on points at the script/actions.
        if (_context.TickCount - _lastDrainLogTick >= 30)
        {
            int gunshot = brain.Events.DrainedCount(AiEvent.Gunshot);
            int miss = brain.Events.DrainedCount(AiEvent.GunshotMiss);
            int impact = brain.Events.DrainedCount(AiEvent.GroundImpact);
            int explosion = brain.Events.DrainedCount(AiEvent.Explosion);
            int grenadeThrown = brain.Events.DrainedCount(AiEvent.GrenadeThrown);
            int grenadeLand = brain.Events.DrainedCount(AiEvent.GrenadeLand);
            int flashbang = brain.Events.DrainedCount(AiEvent.Flashbang);
            int walk = brain.Events.DrainedCount(AiEvent.Walk);
            int damage = brain.Events.DrainedCount(AiEvent.TakingDamage);
            if (gunshot + miss + impact + explosion + grenadeThrown + grenadeLand + flashbang + walk + damage > 0)
            {
                _lastDrainLogTick = _context.TickCount;
                _context.Logger.Information(
                    SoldierContext.LogCategory + ".Flow",
                    $"#{brain.RuntimeId} drained gunshot={gunshot} miss={miss} "
                        + $"explosion={explosion} grenadeThrown={grenadeThrown} "
                        + $"grenadeLand={grenadeLand} flashbang={flashbang} "
                        + $"impact={impact} walk={walk} dmg={damage} act={brain.Activity}");
            }
        }
    }

    /// <summary>Reports what every soldier's AI is actually doing, for diagnosing a level that looks wrong.</summary>
    /// <remarks>
    /// Written for the "why is nobody patrolling?" question, which needs to distinguish four different
    /// failures that look identical on screen: no brain, no script, a script that never asked to patrol, and
    /// a patrol that started but cannot move.
    /// </remarks>
    public void LogAiSummary()
    {
        Dictionary<string, int> activities = [];
        Dictionary<int, int> paths = [];
        int withRoute = 0;
        int moving = 0;

        foreach ((SoldierView view, Igi1HumanAi brain) in _context.Brains)
        {
            string key = brain.Activity.ToString();
            activities[key] = activities.GetValueOrDefault(key) + 1;

            if (brain.Route is { } route)
            {
                withRoute++;
                paths[route.Path.Id] = paths.GetValueOrDefault(route.Path.Id) + 1;
                if (route.Current is { } command && command.Kind.IsMovement())
                {
                    moving++;
                }
            }

            _ = view;
        }

        _context.Logger.Information(
            SoldierContext.LogCategory,
            $"AI: {_context.Brains.Count} brain(s); {withRoute} on a patrol path; {moving} on a movement command.");

        foreach ((string activity, int count) in activities.OrderByDescending(p => p.Value))
        {
            _context.Logger.Information(SoldierContext.LogCategory, $"AI:   activity {activity}: {count}");
        }

        _context.Logger.Information(
            SoldierContext.LogCategory,
            $"AI: patrol paths in use: {string.Join(", ", paths.Keys.Order())}");

        if (TravelSamples > 0)
        {
            double mean = TravelTotal / TravelSamples;
            _context.Logger.Information(
                SoldierContext.LogCategory,
                $"AI: walk step over {TravelSamples} sample(s): mean {mean:F1} u/tick " +
                $"({mean * 30.0 / WorldScene.UnitsPerMetre:F2} m/s), max {TravelMax:F1} u/tick. " +
                $"Retail measures 160-185 u/tick (about 1.28 m/s).");
        }
    }

    /// <summary>Logs how the level's fight is going, once per change.</summary>
    /// <param name="roundsFired">How many rounds the level's soldiers have fired.</param>
    /// <remarks>
    /// One line, and only when the numbers move, so it is a running commentary rather than a per-tick
    /// flood. It is what tells a "the guards do nothing" report apart into its three possible causes:
    /// nobody sees the player, somebody sees him and never engages, or somebody engages and never
    /// fires.
    /// </remarks>
    public void ReportCombat(int roundsFired)
    {
        int seeing = 0;
        int engaging = 0;
        int chasing = 0;
        foreach (Igi1HumanAi brain in _context.Brains.Values)
        {
            if (brain.SeesEnemy)
            {
                seeing++;
            }

            if (brain.Activity == AiSoldierActivity.Engaging)
            {
                engaging++;
            }

            if (brain.IsMovingInCombat)
            {
                chasing++;
            }
        }

        if (seeing == 0 && engaging == 0 && roundsFired == 0)
        {
            return;
        }

        // The port-side [aiflow] line, for diffing against the retail probe's capture: every two
        // seconds, one decision-state line per engaged soldier.
        if (engaging > 0 && _context.TickCount - _lastCombatDetailTick >= 60)
        {
            _lastCombatDetailTick = _context.TickCount;
            foreach (Igi1HumanAi brain in _context.Brains.Values)
            {
                if (brain.Activity == AiSoldierActivity.Engaging)
                {
                    _context.Logger.Information(SoldierContext.LogCategory + ".Flow", $"#{brain.RuntimeId} {brain.DescribeCombat()}");
                }
            }
        }

        string report =
            $"{seeing} soldier(s) see the player, {engaging} engaging, {chasing} chasing, {roundsFired} round(s) fired.";
        if (report == _lastCombatReport)
        {
            return;
        }

        _lastCombatReport = report;
        _context.Logger.Information(SoldierContext.LogCategory, $"AI: {report}");
    }

    /// <summary>Logs each input channel a fight asks for, once per type and channel.</summary>
    /// <remarks>
    /// Deliberately the same shape as the probe's <c>[combat] channel</c> line from the retail game (logged
    /// here under the <c>Soldier.Combat</c> category rather than a tag), so
    /// a run of the port and a capture of the original can be put side by side. The slot is resolved
    /// through the port's transcription of <c>off_54151C</c>; if it disagrees with the probe's, the
    /// transcription is wrong.
    /// </remarks>
    public void ReportChannels(Igi1HumanAi brain)
    {
        foreach (AiChannelWrite write in brain.ChannelWrites)
        {
            string line = $"channel type={(int)brain.Type} channel={(int)write.Channel} "
                + $"-> slot {write.Slot} value={write.Value:0.00}";

            if (_reportedChannels.Add(line))
            {
                _context.Logger.Information(SoldierContext.LogCategory + ".Combat", line);
            }
        }
    }
}
