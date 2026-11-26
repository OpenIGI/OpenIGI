using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Prefabs;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A piece of a mission kept and placed again: new ids that still find each other, moved positions, minds kept.</summary>
public sealed class PrefabTests
{
    private const string Level = """
        Task_DeclareParameters("HumanSoldier", "Position", "ObjectPos", "Gamma", "Real32");
        Task_DeclareParameters("HumanAI", "Graph", "Int32");
        Task_DeclareParameters("Building", "Position", "ObjectPos", "Model", "VarString");
        Task_DeclareParameters("StatusMessage", "Send", "VarString", "Text", "VarString");
        Task_New(-1, "Container", "Root",
            Task_New(10, "HumanSoldier", "guard", 1000, 2000, 300, 0,
                Task_New(11, "HumanAI", "mind", 5)),
            Task_New(20, "Building", "hut", 3000, 2000, 100, "hut01"),
            Task_New(30, "StatusMessage", "seen", "HumanSoldier_10.isDead", "M1_MSG"));
        """;

    /// <summary>
    /// Kept, written and read back, then placed: every task new with a free id, the message still reading the copied guard,
    /// the piece moved so its anchor (middle across, lowest up) stands at the point, and the mind under its new id.
    /// </summary>
    [Fact]
    public void A_prefab_places_as_new_tasks_where_it_is_put()
    {
        MissionDocument document = Document();
        MissionTask[] tasks = [.. document.EnumerateTasks().Where(static t => t.Id is 10 or 11 or 20 or 30)];
        Prefab kept = Prefab.Capture(document, tasks, "Hut and guard", "igi1", id => id == 11 ? [1, 2, 3] : null)!;
        Assert.Equal("Soldiers", kept.Category);
        Assert.Equal([2000.0, 2000.0, 100.0], kept.Anchor);

        Prefab read = Prefab.Read(kept.Write());
        PlacedPrefab placed = read.Place(document, (10000, 20000, 500));

        MissionTask[] all = [.. placed.Tasks.SelectMany(static t => t.SelfAndSubtree())];
        MissionTask guard = all.Single(static t => t.TypeName == "HumanSoldier");
        MissionTask mind = all.Single(static t => t.TypeName == "HumanAI");
        MissionTask hut = all.Single(static t => t.TypeName == "Building");
        MissionTask message = all.Single(static t => t.TypeName == "StatusMessage");
        Assert.DoesNotContain(guard.Id, (int[])[10, 11, 20, 30, -1]);
        Assert.True(guard.FindParameter("Position")!.TryGetNumbers(out double[] at));
        Assert.Equal([9000.0, 20000.0, 700.0], at);
        Assert.True(hut.FindParameter("Position")!.TryGetNumbers(out double[] hutAt));
        Assert.Equal([11000.0, 20000.0, 500.0], hutAt);
        Assert.Equal($"HumanSoldier_{guard.Id}.isDead", ((QscStringLiteral)message.FindParameter("Send")!.Arguments[0]).Value);
        Assert.Equal([1, 2, 3], placed.Minds[mind.Id]);
        Assert.Single(placed.Minds);
    }

    /// <summary>The library keeps a game's prefabs a file each, lists them by shelf and name, and passes over a damaged file.</summary>
    [Fact]
    public void The_library_keeps_prefabs_by_game()
    {
        string folder = Path.Combine(Path.GetTempPath(), "openigi-prefabs-" + Guid.NewGuid().ToString("N"));
        try
        {
            PrefabLibrary library = new(folder);
            MissionDocument document = Document();
            MissionTask hut = document.EnumerateTasks().Single(static t => t.Id == 20);
            library.Save(Prefab.Capture(document, [hut], "Hut: small", "igi1", static _ => null)!);
            library.Save(Prefab.Capture(document, [hut], "A hut", "igi1", static _ => null)!);
            File.WriteAllText(Path.Combine(folder, "igi1", "broken" + PrefabLibrary.Extension), "{ not json");

            Assert.Equal(["A hut", "Hut: small"], library.List("igi1").Select(static p => p.Name));
            Assert.Empty(library.List("igi2"));
            library.Delete(library.List("igi1")[0]);
            Assert.Equal(["Hut: small"], library.List("igi1").Select(static p => p.Name));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static MissionDocument Document() => MissionDocument.Read(QscParser.ParseScript(Level, QvmProgram.IgiVersion));
}
