using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The AI natives <see cref="AiScriptHost"/> binds to a live soldier — that they store what the original
/// stores, at the polarity the original stores it, and that no native a shipped archetype script calls is
/// left as a silent stub.
/// </summary>
/// <remarks>
/// Each native's semantics were read from <c>IGI.exe</c> (<c>0x44Dxxx</c>/<c>0x44Exxx</c>) and the ctor
/// defaults from <c>0x44F060</c>; these pin the transcription.
/// </remarks>
public sealed class AiScriptHostNativeTests
{
    /// <summary>A fresh AI has the defaults the constructor <c>0x44F060</c> writes.</summary>
    [RequiresGameDataFact]
    public void AFreshSoldierHasTheConstructorsDefaults()
    {
        Igi1HumanAi soldier = Soldier();

        Assert.False(soldier.IsInvulnerable);          // +15036 = 0
        Assert.False(soldier.IsInstantDeathDisabled);  // +15037 = 0 -> head shot IS lethal by default
        Assert.Equal(-1, soldier.AlarmTriggerId);      // +15040
        Assert.Equal(-1, soldier.AlarmControlId);      // +15044
        Assert.Equal(-1, soldier.GunnerId);            // +15048
        Assert.Equal(0, soldier.AlarmAccess);          // +15052 = BeforeCombat
        Assert.Equal(-1, soldier.DeathAnimationOverride); // +15056
    }

    /// <summary>
    /// <c>SetInstantDeath</c> stores the <b>inverse</b> of its argument (<c>0x44E1B0</c> stores
    /// <c>arg == 0</c>): disabling instant death is what <c>SetInstantDeath(FALSE)</c> does.
    /// </summary>
    [RequiresGameDataFact]
    public void SetInstantDeathStoresTheInversePolarity()
    {
        (AiScriptHost host, _) = Host();
        Igi1HumanAi soldier = Soldier();

        Run(host, soldier, "AIFunction_SetInstantDeath", 0);   // FALSE
        Assert.True(soldier.IsInstantDeathDisabled);

        Run(host, soldier, "AIFunction_SetInstantDeath", 1);   // TRUE
        Assert.False(soldier.IsInstantDeathDisabled);
    }

    /// <summary><c>SetInvulnerability</c> stores its argument unchanged (<c>0x44E150</c>).</summary>
    [RequiresGameDataFact]
    public void SetInvulnerabilityStoresItsArgumentDirectly()
    {
        (AiScriptHost host, _) = Host();
        Igi1HumanAi soldier = Soldier();

        Run(host, soldier, "AIFunction_SetInvulnerability", 1);
        Assert.True(soldier.IsInvulnerable);

        Run(host, soldier, "AIFunction_SetInvulnerability", 0);
        Assert.False(soldier.IsInvulnerable);
    }

    /// <summary>The alarm, gunner and death-animation setters are plain stores.</summary>
    [RequiresGameDataFact]
    public void TheAlarmAndGunnerSettersStoreTheirIds()
    {
        (AiScriptHost host, _) = Host();
        Igi1HumanAi soldier = Soldier();

        Run(host, soldier, "AIFunction_SetAlarmTriggerID", 42);
        Run(host, soldier, "AIFunction_SetAlarmControlID", 7);
        Run(host, soldier, "AIFunction_SetGunnerID", 3);
        Run(host, soldier, "AIFunction_SetAlarmAccess", 1);
        Run(host, soldier, "AIFunction_SetDeathAnimation", 9);

        Assert.Equal(42, soldier.AlarmTriggerId);
        Assert.Equal(7, soldier.AlarmControlId);
        Assert.Equal(3, soldier.GunnerId);
        Assert.Equal(1, soldier.AlarmAccess);
        Assert.Equal(9, soldier.DeathAnimationOverride);
    }

    [RequiresGameDataFact]
    public void GetGunnerStatusReadsTheResolvedStationaryGunOccupiedFlag()
    {
        Igi1HumanAi soldier = Soldier();
        soldier.GunnerId = 17;
        soldier.SetGunnerStatusResolver(id => id == 17);
        Assert.True(soldier.GetGunnerStatus());

        soldier.SetGunnerStatusResolver(_ => false);
        Assert.False(soldier.GetGunnerStatus());
    }

    /// <summary>
    /// <c>SetEventPriority</c> builds the dispatch order in <b>call order</b> (<c>0x44E0C0</c> appends): the
    /// first type named leads, and naming a type twice does not move it.
    /// </summary>
    [RequiresGameDataFact]
    public void SetEventPriorityOrdersTheQueueInCallOrder()
    {
        (AiScriptHost host, _) = Host();
        Igi1HumanAi soldier = Soldier();
        soldier.SetScript(host, CallNative("AIAction_LookAtEvent", LookAtEventArgs));

        Run(host, soldier, "AIFunction_SetEventPriority", (int)AiEvent.Combat);
        Run(host, soldier, "AIFunction_SetEventPriority", (int)AiEvent.EnemyDetection);
        Run(host, soldier, "AIFunction_SetEventPriority", (int)AiEvent.Combat); // repeat: no-op

        Assert.Equal(AiEvent.Combat, soldier.Events.PriorityOrder[0]);
        Assert.Equal(AiEvent.EnemyDetection, soldier.Events.PriorityOrder[1]);
    }

    /// <summary>
    /// <c>IsEventBehind</c> answers <c>forward · (enemy − self) &lt; 0</c> against the enemy captured this
    /// tick, and <see langword="false"/> before any tick has run.
    /// </summary>
    [RequiresGameDataFact]
    public void IsEnemyBehindTestsTheFacingPlane()
    {
        Igi1HumanAi soldier = Soldier();
        Assert.False(soldier.IsEnemyBehind()); // no enemy known yet

        // Facing 0 points +Y. Put the enemy far behind (-Y) and far enough not to be detected in a tick.
        Core.Numerics.Vector3d p = soldier.Position;
        soldier.Tick(1, new Core.Numerics.Vector3d(p.X, p.Y - (2000 * 4096.0), p.Z), rootMotionThisTick: 0.0);
        Assert.True(soldier.IsEnemyBehind());

        soldier.Tick(2, new Core.Numerics.Vector3d(p.X, p.Y + (2000 * 4096.0), p.Z), rootMotionThisTick: 0.0);
        Assert.False(soldier.IsEnemyBehind());
    }

    /// <summary>
    /// <b>No native the two archetype scripts most soldiers run actually call is left a silent stub.</b>
    /// Runs <c>guard.qvm</c> and <c>patrol.qvm</c> for every event type and asserts the host never logs a
    /// "not modelled" native — the corpus check the blanket <c>default</c> used to hide.
    /// </summary>
    [RequiresGameDataFact]
    public void NoNativeGuardOrPatrolCallsIsAnUnmodelledStub()
    {
        foreach (string archetype in new[] { "guard", "patrol" })
        {
            (AiScriptHost host, MemorySink log) = Host();
            Igi1HumanAi soldier = Soldier();
            QvmProgram script = QvmProgram.Read(File.ReadAllBytes(ArchetypePath(archetype)));

            foreach (AiEvent type in Enum.GetValues<AiEvent>())
            {
                host.Run(script, soldier, type, slot: 0);
            }

            string[] unmodelled = [.. log.Snapshot().Select(static r => r.Message).Where(static m => m.Contains("not modelled", StringComparison.Ordinal))];
            Assert.True(unmodelled.Length == 0, $"{archetype}.qvm calls an unmodelled native: {string.Join("; ", unmodelled)}");
        }
    }

    /// <summary>
    /// A soldier whose script answers events with <c>AIAction_LookAtEvent</c> turns to face a gunshot
    /// it heard — the "guard notices your shot" reaction.
    /// </summary>
    /// <remarks>
    /// The behaviour is <c>0x44D870</c> → <c>0x48E0A0</c>: copy the current event's position, turn to
    /// it with the instant combat turn, hold for the duration. The port used to drop the action on the
    /// floor — a shot fired next to a guard produced no reaction at all.
    /// </remarks>
    [RequiresGameDataFact]
    public void AGuardTurnsTowardAGunshotItHeard()
    {
        (AiScriptHost host, _) = Host();
        Igi1HumanAi soldier = Soldier();

        // Only the gunshot branch acts: an action produced on CREATE would carry priority -1 (0x44FC90's
        // creation dispatch) and cap the pump shut for as long as it ran.
        soldier.SetScript(host, CallNativeOn(AiEvent.Gunshot, "AIAction_LookAtEvent", LookAtEventArgs));

        // A shot to the soldier's east, well inside the 30 m hearing range.
        Vector3d shot = new(
            soldier.Position.X + (10 * 4096.0),
            soldier.Position.Y,
            soldier.Position.Z);
        float wanted = MathF.Atan2((float)-(shot.X - soldier.Position.X), 0f);

        // Far away and never seen: the only stimulus is the sound.
        Vector3d enemyEye = new(soldier.Position.X, soldier.Position.Y + (500 * 4096.0), soldier.Position.Z);

        // CREATE dispatches; a real archetype's creation branch then declares what it hears.
        soldier.Tick(1, enemyEye, rootMotionThisTick: 0.0);
        soldier.DeclareEventPriority(AiEvent.Gunshot);
        Assert.True(soldier.HearGunshot(shot, silenced: false));

        for (int tick = 2; tick < 40; tick++)
        {
            soldier.Tick(tick, enemyEye, rootMotionThisTick: 0.0);
        }

        float error = MathF.Abs(soldier.Facing - wanted);
        while (error > MathF.PI)
        {
            error = MathF.Abs(error - MathF.Tau);
        }

        Assert.True(error <= 0.0349f, $"facing {soldier.Facing:F3}, wanted {wanted:F3} toward the shot");
    }

    /// <summary>The shipped guard and patrol QVMs both turn an admitted ordinary gunshot into SetCombat.</summary>
    [RequiresGameDataTheory]
    [InlineData("guard")]
    [InlineData("patrol")]
    public void RetailArchetypeReactsToAQueuedGunshot(string archetype)
    {
        (AiScriptHost host, _) = Host();
        Igi1HumanAi soldier = Soldier();
        QvmProgram script = QvmProgram.Read(File.ReadAllBytes(ArchetypePath(archetype)));
        soldier.SetScript(host, script);

        Vector3d position = soldier.Position;
        Vector3d enemyFarAway = new(position.X, position.Y + (500 * 4096.0), position.Z);
        Vector3d shot = new(position.X + (10 * 4096.0), position.Y, position.Z);

        soldier.Tick(1, enemyFarAway, rootMotionThisTick: 0.0); // CREATE
        Assert.True(soldier.HearGunshot(shot, silenced: false));
        soldier.Tick(2, enemyFarAway, rootMotionThisTick: 0.0);

        // SetCombat is NOT a combat install: 0x48E560 synthesizes an AIEVENT_COMBAT onto the AI's own
        // queue and ends. The engagement begins one tick later, when that COMBAT event dispatches and
        // the archetype's COMBAT branch runs AIAction_Combat.
        Assert.Contains(soldier.LastActions, action => action.Name == "AIAction_SetCombat");
        Assert.Equal(10 * 4096.0, soldier.LastEventDistance, precision: 3);
        Assert.NotEqual(AiSoldierActivity.Engaging, soldier.Activity);

        soldier.Tick(3, enemyFarAway, rootMotionThisTick: 0.0);
        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);
        Assert.Contains(soldier.LastActions, action => action.Name == "AIAction_Combat");
    }

    /// <summary>
    /// A <b>second</b> host on the same registry owns the natives — the mission-transition case. Each
    /// native's closure captures its host; skipping "already registered" names left the next mission's
    /// scripts running natives bound to the previous mission's host, so <c>GetCurrentEventType</c> read a
    /// stale event and every produced action landed in a list nobody read: guards in mission 2 were
    /// silently deaf to everything.
    /// </summary>
    [RequiresGameDataFact]
    public void ASecondHostOnTheSameRegistryOwnsTheNatives()
    {
        NativeRegistry shared = new();
        shared.RegisterConstant("TRUE", 1);
        shared.RegisterConstant("FALSE", 0);
        shared.RegisterConstant("GAME_FREQUENCY", 30);
        shared.RegisterRealConstant("WORLD_METER", 4096.0);
        shared.RegisterRealConstant("MATH_PI", Math.PI);
        _ = new AiScriptHost(shared, NullLogger.Instance);

        // The next mission's host registers against the SAME registry, as the transition does.
        AiScriptHost nextMission = new(shared, NullLogger.Instance);

        Igi1HumanAi soldier = Soldier();
        QvmProgram script = QvmProgram.Read(File.ReadAllBytes(ArchetypePath("guard")));
        soldier.SetScript(nextMission, script);

        Vector3d position = soldier.Position;
        Vector3d enemyFarAway = new(position.X, position.Y + (500 * 4096.0), position.Z);
        Vector3d shot = new(position.X + (10 * 4096.0), position.Y, position.Z);

        soldier.Tick(1, enemyFarAway, rootMotionThisTick: 0.0); // CREATE
        Assert.True(soldier.HearGunshot(shot, silenced: false));
        soldier.Tick(2, enemyFarAway, rootMotionThisTick: 0.0);
        Assert.Contains(soldier.LastActions, action => action.Name == "AIAction_SetCombat");

        soldier.Tick(3, enemyFarAway, rootMotionThisTick: 0.0);
        Assert.Equal(AiSoldierActivity.Engaging, soldier.Activity);
    }

    private static readonly int[] LookAtEventArgs = [2, 0];

    /// <summary>A host over the AI natives and the game constants, whose warnings are kept.</summary>
    /// <returns>The host, and the sink holding every warning it logs, so a "not modelled" one can be asserted against.</returns>
    private static (AiScriptHost Host, MemorySink Log) Host()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        // The Game module registers these beside its natives (sub_415530); guard.qvm's COMBAT branch
        // multiplies by WORLD_METER, so a host without it faults the branch that installs combat.
        natives.RegisterConstant("GAME_FREQUENCY", 30);
        natives.RegisterRealConstant("WORLD_METER", 4096.0);
        natives.RegisterRealConstant("MATH_PI", Math.PI);
        MemorySink log = new(minimumLevel: LogLevel.Warning);
        return (new AiScriptHost(natives, log), log);
    }

    private static Igi1HumanAi Soldier()
    {
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(GraphPath()));
        return new Igi1HumanAi(graph, graph.Nodes[0].Position, facing: 0f, phase: 0, seed: 1);
    }

    private static void Run(AiScriptHost host, Igi1HumanAi soldier, string native, params int[] args) =>
        host.Run(CallNative(native, args), soldier, AiEvent.Create, slot: 0);

    // A minimal program that calls one native with integer immediates and halts. The argument blocks are
    // the lazy-evaluated call operands the interpreter runs on demand (each pushes its value and breaks).
    private static QvmProgram CallNative(string native, int[] args)
    {
        QvmWriter writer = new();
        EmitCall(writer, native, args);
        writer.Code.Emit(QvmOpcode.Brk);
        return QvmProgram.Read(writer.Build());
    }

    // The same call, made only when the dispatched event is one type -- the shape of every branch in the
    // shipped archetypes: GetCurrentEventType() == AIEVENT_x.
    private static QvmProgram CallNativeOn(AiEvent type, string native, int[] args)
    {
        QvmWriter writer = new();
        QvmLabel compare = writer.Code.CreateLabel();
        QvmLabel skip = writer.Code.CreateLabel();

        writer.PushSymbol("AIFunction_GetCurrentEventType");
        writer.Code.Call();
        writer.Code.Branch(QvmOpcode.Bra, compare);
        writer.Code.MarkLabel(compare).Push((int)type).Emit(QvmOpcode.Eq);
        writer.Code.Branch(QvmOpcode.Bf, skip);
        EmitCall(writer, native, args);
        writer.Code.MarkLabel(skip).Emit(QvmOpcode.Brk);
        return QvmProgram.Read(writer.Build());
    }

    private static void EmitCall(QvmWriter writer, string native, int[] args)
    {
        QvmLabel[] labels = new QvmLabel[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            labels[i] = writer.Code.CreateLabel();
        }

        QvmLabel after = writer.Code.CreateLabel();

        writer.PushSymbol(native);
        writer.Code.Call(labels);
        writer.Code.Branch(QvmOpcode.Bra, after);
        for (int i = 0; i < args.Length; i++)
        {
            writer.Code.MarkLabel(labels[i]).Push(args[i]).Emit(QvmOpcode.Brk);
        }

        writer.Code.MarkLabel(after).Emit(QvmOpcode.Pop);
    }

    private static string GraphPath() => Path.Combine(
        GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");

    private static string ArchetypePath(string name) => Path.Combine(
        GameData.RequireRoot(), "common", "ai", name + ".qvm");
}
