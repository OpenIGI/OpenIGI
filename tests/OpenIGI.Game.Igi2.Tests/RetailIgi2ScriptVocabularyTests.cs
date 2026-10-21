using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Every name I.G.I. 2's shipped scripts call, and whether the title declares it.</summary>
/// <remarks>
/// <para>
/// A script does not fail on an unknown symbol the way a compiler does. An unresolved name is a call
/// that does nothing and a constant that reads as zero, so a level whose vocabulary is half declared
/// still loads, still builds a task tree, and is quietly wrong — a guard with the wrong
/// <c>AIType</c> fights differently three milestones later.
/// </para>
/// <para>
/// So the whole vocabulary is measured rather than sampled, and the union across the corpus is pinned.
/// The set shrinking means a module stopped registering something; the set growing means a script
/// reaches for a name nothing has been written for yet, and either way the assertion is what says so.
/// </para>
/// </remarks>
public sealed class RetailIgi2ScriptVocabularyTests
{
    /// <summary>Every symbol every shipped level script calls resolves through the title's session.</summary>
    /// <remarks>
    /// Built the way the host builds one — the title's structural task types and the title's content
    /// modules — so this fails if either stops being the title's.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_symbol_a_level_script_calls_is_declared()
    {
        (NativeRegistrySnapshot session, _) = HostShapedSession();

        SortedSet<string> vocabulary = [];
        SortedSet<string> missing = [];
        int scripts = 0;

        foreach (string path in Scripts("objects.qvm"))
        {
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            scripts++;

            foreach (string? name in program.SymbolNames)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                vocabulary.Add(name);
                if (!session.Contains(name))
                {
                    missing.Add($"{name} ({Path.GetFileName(Path.GetDirectoryName(path))})");
                }
            }
        }

        // 19 campaign levels and 5 multiplayer maps. One more map ships a mission script than ships a
        // level, which is why this count and the mission one differ by one.
        Assert.Equal(24, scripts);
        Assert.True(missing.Count == 0, $"undeclared: {string.Join(", ", missing)}");

        // The whole vocabulary of a shipped level, pinned. Two task commands and seven constants -- and
        // the constants are what make it worth pinning, because an undeclared one reads as zero rather
        // than failing.
        Assert.Equal(
            [
                "AIType_Defensive",
                "AIType_OnVehicle",
                "AIType_Offensive",
                "FALSE",
                "OVERLAY_BINOCULAR",
                "OVERLAY_SNIPER",
                "TRUE",
                "Task_DeclareParameters",
                "Task_New",
            ],
            vocabulary);
    }

    /// <summary>Every symbol every shipped mission script calls resolves too.</summary>
    /// <remarks>
    /// One name, and it is the one this title reads differently: <c>DefineMission</c> takes fourteen
    /// arguments here and eleven in the first game.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_symbol_a_mission_script_calls_is_declared()
    {
        (NativeRegistrySnapshot session, _) = HostShapedSession();

        SortedSet<string> vocabulary = [];
        int scripts = 0;

        foreach (string path in Scripts("mission.qvm"))
        {
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            scripts++;

            foreach (string? name in program.SymbolNames)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    vocabulary.Add(name);
                    Assert.True(session.Contains(name), $"'{name}' is undeclared.");
                }
            }
        }

        Assert.Equal(25, scripts);
        Assert.Equal(["DefineMission"], vocabulary);
    }

    /// <summary>The constants carry the values the engine registers them with.</summary>
    /// <remarks>
    /// <c>AISquad_RegisterScriptValues</c> (<c>igi2 0x49A880</c>) registers each through
    /// <c>Symbol_Register(name, Script_ConstInt, value, …)</c>, and the value is the argument pushed as
    /// <c>pxContext</c>: <b>1</b> for offensive, <b>0</b> for defensive, <b>2</b> for on-vehicle.
    /// <c>OverlayHolder_Open</c> (<c>0x533BC0</c>) does the same for the overlays.
    /// <para>
    /// Worth asserting because a wrong value does not fail. The level still loads, the task still gets a
    /// number, and the guard it configures simply behaves as a different kind of guard.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_constants_carry_the_values_the_engine_registers()
    {
        (_, NativeRegistry natives) = HostShapedSession();

        Assert.Equal(0, ConstantOf(natives, "AIType_Defensive"));
        Assert.Equal(1, ConstantOf(natives, "AIType_Offensive"));
        Assert.Equal(2, ConstantOf(natives, "AIType_OnVehicle"));
        Assert.Equal(0, ConstantOf(natives, "OVERLAY_BINOCULAR"));
        Assert.Equal(2, ConstantOf(natives, "OVERLAY_SNIPER"));
    }

    private static int ConstantOf(NativeRegistry natives, string name)
    {
        Assert.True(natives.TryResolve(name, out QvmSymbol? symbol), $"'{name}' is undeclared.");
        Assert.Equal(QvmSymbolKind.Constant, symbol!.Kind);
        return symbol.Read().Integer;
    }

    private static IEnumerable<string> Scripts(string name) =>
        Directory
            .EnumerateFiles(
                Path.Combine(GameDataSource.Igi2.RequireRoot(), "missions"),
                name,
                SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase);

    private static (NativeRegistrySnapshot Session, NativeRegistry Natives) HostShapedSession()
    {
        Igi2Runtime title = new Igi2Runtime();
        GameLoop loop = new(
            new ManualTimeSource(1_000),
            structuralTypes: title.Layout.StructuralTaskTypes);

        VirtualFileSystem files = new();
        ResourceRegistry resources = new(files);
        TitleSession.Start(title.Modules, loop, files, resources, loadTables: false);

        // The mission catalogue declares DefineMission, and the host registers it beside the modules.
        new World.Igi2MissionCatalogue(NullLogger.Instance).Register(loop.Natives);

        return (new NativeRegistrySnapshot(loop.Natives), loop.Natives);
    }

    /// <summary>What the session declares, as a set that can be asked about a name.</summary>
    private sealed class NativeRegistrySnapshot(NativeRegistry natives)
    {
        public bool Contains(string name) => natives.Contains(name);
    }
}
