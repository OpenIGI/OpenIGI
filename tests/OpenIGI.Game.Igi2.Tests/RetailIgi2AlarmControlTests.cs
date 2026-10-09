using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's alarm, <c>AlarmControl_100</c> (<c>AlarmControl_RunHandler</c>, <c>igi2 0x4D0320</c>), a terminal whose trigger and
/// alarm are the camera alarm's variable, and the player hacking a terminal (<c>HumanPlayer_InitStateActivateTerminal</c>,
/// <c>0x4ADF00</c>).
/// </summary>
public sealed class RetailIgi2AlarmControlTests
{
    /// <summary>The level's one alarm.</summary>
    private const int Alarm = 100;

    /// <summary>The main gate's camera, <c>SCamera_106</c>.</summary>
    private const int MainGate = 106;

    /// <summary>A code pad always active, <c>GenericTBA_160</c>, whose model and place a test terminal borrows.</summary>
    private const int Pad = 160;

    /// <summary>The alarm is typed with its parameters, the terminal's defaults where the level gives none.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_alarm_is_read()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2AlarmControlTask alarm = m.Tree.OfType<Igi2AlarmControlTask>().Single();
        Assert.Equal(Alarm, alarm.Id);
        Assert.Equal((string.Empty, "1", 4f, "tba_hackcomp"), (alarm.Model, alarm.ActiveScript, alarm.HackTime, alarm.ActivateSound));
        Assert.Equal(("EditVariable_103.nValue == 1", "EditVariable_103.nValue == 1"), (alarm.TriggerScript, alarm.AlarmScript));
        Assert.Equal(("explo_02_m", 5, 4), (alarm.ExplosionSound, alarm.ExplosionFragments, alarm.ExplosionFireballs));
        Assert.Equal((Igi2ActivateKind.Terminal, 0f), (alarm.Kind, alarm.Radius));
        Assert.Equal(-10000, alarm.TriggerLastTick);
    }

    /// <summary>
    /// The camera alarm's chain: the main gate's camera detects the player, the camera alarm's variable is raised, and the alarm
    /// is triggered and sounds on the next tick, counting once on the player; it stays sounding, not counting again.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_camera_detection_sounds_the_alarm()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2SecurityCameraTask gate = m.Tree.OfType<Igi2SecurityCameraTask>().Single(static c => c.Id == MainGate);
        gate.Models = m.Level.ModelFor;
        gate.Prepare();
        float[] f = gate.View.Orientation;
        Vector3d at = gate.View.Position + (new Vector3d(f[1], f[4], 0.0) * 16000.0);
        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(m.Collision!, m.Level, null, new Vector3d(at.X, at.Y, gate.View.Position.Z - 8000.0), 0f, m.Sounds);
        RetailIgi2SecurityCameraTests.Arm(m, player.Human);
        Igi2AlarmControlTask alarm = m.Tree.OfType<Igi2AlarmControlTask>().Single();
        alarm.AlarmRaised = () => player.Human.TimesAlarmTriggered++;

        int detected = -1, sounded = -1;
        for (int tick = 0; tick < 200 && sounded < 0; tick++)
        {
            m.Run.Tick();
            if (detected < 0 && gate.IsDetection)
            {
                detected = tick;
            }

            if (alarm.IsAlarm)
            {
                sounded = tick;
            }
        }

        Assert.True(detected >= 0, "the camera never detected the player");
        Assert.True(sounded > detected, $"the alarm sounded at {sounded}, the camera detected at {detected}");
        Assert.Equal(1.0, Read(m, "EditVariable_103.nValue"));
        Assert.Equal((true, false, 1), (alarm.IsTrigger, alarm.IsLastAlarm, player.Human.TimesAlarmTriggered));
        Assert.Equal(m.Run.Ticks - 1, alarm.TriggerLastTick);
        Assert.Equal(1.0, Read(m, $"AlarmControl_{Alarm}.isAlarm"));

        m.Run.Tick();
        Assert.Equal((true, true, 1), (alarm.IsAlarm, alarm.IsLastAlarm, player.Human.TimesAlarmTriggered));
    }

    /// <summary>An alarm that is not active is neither triggered nor sounding, whatever its scripts say.</summary>
    [RequiresTitleDataFact("igi2")]
    public void An_inactive_alarm_stays_quiet()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2AlarmControlTask alarm = m.Tree.OfType<Igi2AlarmControlTask>().Single();
        alarm.ActiveScript = "0";
        alarm.TriggerScript = "1";
        alarm.AlarmScript = "1";
        int raised = 0;
        alarm.AlarmRaised = () => raised++;
        m.Run.Tick();
        Assert.Equal((false, false, false, 0), (alarm.IsActive, alarm.IsTrigger, alarm.IsAlarm, raised));
        Assert.Equal(-10000, alarm.TriggerLastTick);
    }

    /// <summary>
    /// The player facing an active terminal hacks it while the key is held: it kneels 0.675 m out from the terminal's second
    /// vertex, the hack sounds, and once its four seconds are out the hack is done; the terminal is hacked
    /// on its next tick, and hacked this tick for that tick alone.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_hacks_a_terminal()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2AlarmControlTask terminal = Borrow(m);
        m.Run.Tick();
        Igi2TitlePlayer player = Before(m, terminal);
        Assert.Same(terminal, player.Human.EvaluateTarget);

        Hold(player, pressed: true);
        Igi2HumanPlayer human = player.Human;
        Assert.Equal(Igi2PlayerState.ActivateTerminal, human.Locomotion.CurrentState);
        Assert.Equal((4 * 30, true, true), (human.TimedActionTime, human.IsProgressBar, human.IsActivating));
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "tba_hackcomp" && s.IsAlive);
        Vector3d vertex = terminal.MagicVertex;
        double reach = Math.Sqrt(Math.Pow(human.Position.X - vertex.X, 2) + Math.Pow(human.Position.Y - vertex.Y, 2));
        Assert.Equal(Igi2HumanPlayer.HackReach, reach, 1.0);

        for (int tick = 1; tick < 4 * 30; tick++)
        {
            Hold(player);
        }

        Assert.Equal(Igi2PlayerState.ActivateTerminal, human.Locomotion.CurrentState);
        Hold(player);
        Assert.Equal((Igi2PlayerState.Stand, false), (human.Locomotion.CurrentState, human.IsActivating));

        // typecomp_end is asked for, but no sound script defines it, so nothing sounds.
        Assert.False(m.Sounds.Definitions.TryResolve(Igi2HumanPlayer.HackedSound, out _));
        Assert.DoesNotContain(m.Sounds.Playing, static s => s.Name == "tba_hackcomp" && s.IsAlive);

        Assert.False(terminal.IsHacked);
        m.Run.Tick();
        Assert.Equal((true, true, true), (terminal.IsHacked, terminal.IsHackedThisTick, terminal.IsInUse));
        // Stamped with the flow's tick as it was, before the flow ran this tick.
        Assert.Equal(m.Run.LevelFlow!.Tick - 1, terminal.HackedTick);
        Assert.Equal(1.0, Read(m, $"AlarmControl_{Alarm}.isHacked"));
        m.Run.Tick();
        Assert.Equal((true, false), (terminal.IsHacked, terminal.IsHackedThisTick));
    }

    // The level's alarm given the code pad's model and place, so that it has a second vertex to be hacked at.
    private static Igi2AlarmControlTask Borrow(RetailIgi2Mission1 m)
    {
        Igi2GenericTbaTask pad = m.Tree.OfType<Igi2GenericTbaTask>().Single(static a => a.Id == Pad);
        Igi2AlarmControlTask terminal = m.Tree.OfType<Igi2AlarmControlTask>().Single();
        terminal.Position = pad.Position;
        terminal.Orientation = pad.Orientation;
        terminal.Model = pad.Model;
        terminal.Explosive.Reset();
        terminal.Explosive.Models = m.Level.ModelFor;
        return terminal;
    }

    // The player 2400 units out in front of a terminal's vertex along its Y, facing it.
    private static Igi2TitlePlayer Before(RetailIgi2Mission1 m, Igi2AlarmControlTask terminal)
    {
        IReadOnlyList<float> f = terminal.CurrentOrientation;
        Vector3d ahead = new(f[1], f[4], f[7]);
        Vector3d at = terminal.MagicVertex + (ahead * 2400.0);
        float facing = MathF.Atan2((float)ahead.X, -(float)ahead.Y);
        return RetailIgi2Mission1.Stand(m.Collision!, m.Level, terminal, new Vector3d(at.X, at.Y, terminal.Position.Z), facing, m.Sounds);
    }

    private static void Hold(Igi2TitlePlayer player, bool pressed = false) =>
        player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate, Pressed = pressed ? PlayerAction.Activate : default });

    private static double Read(RetailIgi2Mission1 m, string expression) =>
        m.Run.Evaluate(new Igi2LevelExpression(expression, "test", m.Run.Natives));
}
