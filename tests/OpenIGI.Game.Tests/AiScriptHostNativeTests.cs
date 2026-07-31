using OpenIGI.Core.Diagnostics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

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
    // A logger that keeps every message, so a "not modelled" warning can be asserted against.
    private sealed class Recorder : IIgiLogger
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogLevel level) => true;

        public void Log(LogLevel level, string category, string message, Exception? exception = null) =>
            Messages.Add(message);
    }

    /// <summary>A fresh AI has the defaults the constructor <c>0x44F060</c> writes.</summary>
    [RequiresGameDataFact]
    public void AFreshSoldierHasTheConstructorsDefaults()
    {
        AiSoldier soldier = Soldier();

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
        AiSoldier soldier = Soldier();

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
        AiSoldier soldier = Soldier();

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
        AiSoldier soldier = Soldier();

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

    /// <summary>
    /// <c>SetEventPriority</c> builds the dispatch order in <b>call order</b> (<c>0x44E0C0</c> appends): the
    /// first type named leads, and naming a type twice does not move it.
    /// </summary>
    [RequiresGameDataFact]
    public void SetEventPriorityOrdersTheQueueInCallOrder()
    {
        (AiScriptHost host, _) = Host();
        AiSoldier soldier = Soldier();

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
        AiSoldier soldier = Soldier();
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
            (AiScriptHost host, Recorder log) = Host();
            AiSoldier soldier = Soldier();
            QvmProgram script = QvmProgram.Read(File.ReadAllBytes(ArchetypePath(archetype)));

            foreach (AiEvent type in Enum.GetValues<AiEvent>())
            {
                host.Run(script, soldier, type, slot: 0);
            }

            string[] unmodelled = [.. log.Messages.Where(m => m.Contains("not modelled", StringComparison.Ordinal))];
            Assert.True(unmodelled.Length == 0, $"{archetype}.qvm calls an unmodelled native: {string.Join("; ", unmodelled)}");
        }
    }

    private static (AiScriptHost Host, Recorder Log) Host()
    {
        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        Recorder log = new();
        return (new AiScriptHost(natives, log), log);
    }

    private static AiSoldier Soldier()
    {
        GraphFile graph = GraphFile.Read(File.ReadAllBytes(GraphPath()));
        return new AiSoldier(graph, graph.Nodes[0].Position, facing: 0f, phase: 0, seed: 1);
    }

    private static void Run(AiScriptHost host, AiSoldier soldier, string native, params int[] args) =>
        host.Run(CallNative(native, args), soldier, AiEvent.Create, slot: 0);

    // A minimal program that calls one native with integer immediates and halts. The argument blocks are
    // the lazy-evaluated call operands the interpreter runs on demand (each pushes its value and breaks).
    private static QvmProgram CallNative(string native, int[] args)
    {
        QvmWriter writer = new();
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

        writer.Code.MarkLabel(after).Emit(QvmOpcode.Brk);
        return QvmProgram.Read(writer.Build());
    }

    private static string GraphPath() => Path.Combine(
        GameData.RequireRoot(), "missions", "location0", "level1", "graphs", "graph4019.dat");

    private static string ArchetypePath(string name) => Path.Combine(
        GameData.RequireRoot(), "common", "ai", name + ".qvm");
}
