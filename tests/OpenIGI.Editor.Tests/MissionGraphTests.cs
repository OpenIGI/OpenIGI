using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Vocabulary;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The mission graph: what reading a mission makes, its layout, and the layout's commands.</summary>
public sealed class MissionGraphTests
{
    private const string Sample = """
        Task_DeclareParameters("ConditionalContainer", "Condition", "VarString", "Run at start", "VarString", "Run at stop", "VarString");
        Task_DeclareParameters("Terminal", "Position", "ObjectPos");
        Task_DeclareParameters("AnimTask", "TargetID", "Int32", "Run", "VarString");
        Task_DeclareParameters("EditRigidObj", "Position", "ObjectPos", "Model", "String16");
        Task_New(-1, "Container", "Root",
            Task_New(10, "Terminal", "t", 0, 0, 0),
            Task_New(11, "ConditionalContainer", "gate", "!Terminal_10.isHacked && Terminal_99.isHacked", "Game_DisableMusic()", "",
                Task_New(12, "AnimTask", "anim", 20, "this.isRun"),
                Task_New(13, "EditRigidObj", "prop", 0, 0, 0, "crate")),
            Task_New(20, "EditRigidObj", "target", 0, 0, 0, "box"),
            Task_New(21, "ConditionalContainer", "broken", "Terminal_10.isHacked > * 3", "", ""));
        """;

    /// <summary>Field reads are wires from task pins, references wires from the target's self pin, and frames hold children.</summary>
    [Fact]
    public void Reading_a_mission_makes_its_wires()
    {
        MissionGraph graph = Build();

        GraphNodeKey terminal = new("t10");
        GraphNodeKey gate = new("t11");
        Assert.Contains(graph.Links, l => l.From == new GraphPinRef(terminal, "isHacked"));
        Assert.Contains(graph.Links, l => l.From == new GraphPinRef(new GraphNodeKey("t20"), MissionGraph.Self) && l.To == new GraphPinRef(new GraphNodeKey("t12"), "TargetID"));
        Assert.Equal(gate, graph.Nodes[new GraphNodeKey("t12")].Frame);

        // The condition's root is the && operator; Terminal_99 is a read of nothing; "this" is the owner.
        GraphPinRef root = graph.ExpressionRoots[(gate, "Condition")];
        Assert.Equal("&&", ((ExpressionGraphNode)graph.Nodes[root.Node]).Text);
        Assert.Contains(graph.Nodes.Values, static n => n is ExpressionGraphNode { Kind: ExpressionNodeKind.UnresolvedField, Text: "Terminal_99.isHacked" });
        Assert.Equal(new GraphPinRef(new GraphNodeKey("t12"), "isRun"), graph.ExpressionRoots[(new GraphNodeKey("t12"), "Run")]);
        Assert.Equal(ExpressionNodeKind.Call, ((ExpressionGraphNode)graph.Nodes[graph.ExpressionRoots[(gate, "Run at start")].Node]).Kind);

        // Unreadable text is one raw node holding it verbatim.
        ExpressionGraphNode raw = (ExpressionGraphNode)graph.Nodes[graph.ExpressionRoots[(new GraphNodeKey("t21"), "Condition")].Node];
        Assert.Equal((ExpressionNodeKind.Raw, "Terminal_10.isHacked > * 3"), (raw.Kind, raw.Text));
    }

    /// <summary>The writer keeps the brackets precedence needs and only those, and writes reads and calls as level data does.</summary>
    [Theory]
    [InlineData("!Terminal_10.isHacked && Terminal_10.isOn", "!Terminal_10.isHacked && Terminal_10.isOn")]
    [InlineData("(Terminal_10.isHacked || Terminal_10.isOn) && 1", "(Terminal_10.isHacked || Terminal_10.isOn) && 1")]
    [InlineData("Terminal_10.isHacked || (Terminal_10.isOn && 1)", "Terminal_10.isHacked || Terminal_10.isOn && 1")]
    [InlineData("1 - (2 - 3)", "1 - (2 - 3)")]
    [InlineData("(1 - 2) - 3", "1 - 2 - 3")]
    [InlineData("!(Terminal_10.nTick > 9 * GAME_FREQUENCY)", "!(Terminal_10.nTick > 9 * GAME_FREQUENCY)")]
    [InlineData("Game_DisableMusic() && Terminal_10.isOn", "Game_DisableMusic() && Terminal_10.isOn")]
    [InlineData("this.isRun", "ConditionalContainer_30.isRun")]
    public void Writer_brackets_only_what_precedence_needs(string text, string expected)
    {
        MissionGraph graph = BuildWith(text);
        Assert.Equal(expected, ExpressionGraphWriter.Write(graph, new GraphNodeKey("t30"), "Condition"));
    }

    /// <summary>A prop nothing wires stays hidden; its frame and wired tasks show.</summary>
    [Fact]
    public void Only_what_is_wired_is_shown()
    {
        MissionGraph graph = Build();
        bool Shown(string key) => ((TaskGraphNode)graph.Nodes[new GraphNodeKey(key)]).IsShown;

        Assert.True(Shown("t10"));
        Assert.True(Shown("t11"));
        Assert.True(Shown("t20"));
        Assert.False(Shown("t13"));
    }

    /// <summary>A layout survives its sidecar, and forgets expression positions whose text changed.</summary>
    [Fact]
    public void Layout_round_trips_and_forgets_stale_text()
    {
        MissionGraph graph = Build();
        GraphLayout layout = new();
        Assert.True(GraphAutoLayout.Place(graph, layout) > 5);
        layout.Comments.Add(new GraphComment("c1", "Alarm logic", new GraphRect(0, 0, 400, 200), 0x336699));
        layout.Collapsed.Add(new GraphNodeKey("t10"));
        layout.TextHashes["t11/Condition"] = GraphLayout.HashOf("!Terminal_10.isHacked && Terminal_99.isHacked");

        GraphLayout read = GraphLayout.Read(layout.Write());
        Assert.Equal(layout.Positions.Count, read.Positions.Count);
        Assert.Equal(layout.Comments, read.Comments);
        Assert.Contains(new GraphNodeKey("t10"), read.Collapsed);

        int before = read.Positions.Keys.Count(static k => k.Value.StartsWith("t11/Condition/", StringComparison.Ordinal));
        Assert.True(before > 0);
        Assert.Equal(1, read.Forget(graph, new Dictionary<string, string> { ["t11/Condition"] = "Terminal_10.isHacked" }));
        Assert.DoesNotContain(read.Positions.Keys, static k => k.Value.StartsWith("t11/Condition/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Values flow left to right within a graph: what a node reads stands left of it. The task read is drawn as a reference,
    /// placed under the graph's own key for it, so another graph drawing it keeps its own place.
    /// </summary>
    [Fact]
    public void Auto_layout_puts_inputs_left()
    {
        MissionGraph graph = Build();
        GraphLayout layout = new();
        GraphNodeKey reader = new("t11");
        GraphScope scope = GraphScopes.Build(graph).Single(s => s.Members.Contains(reader));
        GraphAutoLayout.Place(graph, layout, scope);

        GraphNodeKey read = new("t10");
        Assert.Contains(read, scope.References);
        Assert.DoesNotContain(read, layout.Positions.Keys);
        GraphPinRef root = graph.ExpressionRoots[(reader, "Condition")];
        Assert.True(layout.Positions[scope.LayoutKey(read)].X < layout.Positions[root.Node].X);
        Assert.True(layout.Positions[root.Node].X < layout.Positions[reader].X);
    }

    /// <summary>A drag merges into one undo step; comments and collapsing undo.</summary>
    [Fact]
    public void Layout_commands_undo_and_merge()
    {
        GraphLayout layout = new();
        GraphNodeKey key = new("t10");
        layout.Positions[key] = new GraphPoint(0, 0);
        MoveNodesCommand first = new(layout, new Dictionary<GraphNodeKey, (GraphPoint, GraphPoint)> { [key] = (new(0, 0), new(10, 0)) });
        MoveNodesCommand second = new(layout, new Dictionary<GraphNodeKey, (GraphPoint, GraphPoint)> { [key] = (new(10, 0), new(30, 5)) });
        first.Apply();
        second.Apply();
        Assert.True(first.TryMerge(second));
        first.Undo();
        Assert.Equal(new GraphPoint(0, 0), layout.Positions[key]);

        GraphComment note = new("c", "x", new GraphRect(0, 0, 1, 1), 0);
        SetCommentCommand add = new(layout, null, note);
        add.Apply();
        new SetCommentCommand(layout, note, note with { Text = "y" }).Apply();
        Assert.Equal("y", Assert.Single(layout.Comments).Text);
        add.Undo();

        CollapseNodesCommand fold = new(layout, [key], collapsed: true);
        fold.Apply();
        Assert.Contains(key, layout.Collapsed);
        fold.Undo();
        Assert.Empty(layout.Collapsed);
    }

    /// <summary>A wire's reroute points are kept with the layout, come back as saved, and undo; none straightens it again.</summary>
    [Fact]
    public void Reroutes_round_trip_and_undo()
    {
        GraphLayout layout = new();
        string wire = GraphLayout.WireKey("t10", "isRun", "t20", "On");
        SetRerouteCommand route = new(layout, wire, [new GraphPoint(100, 40), new GraphPoint(180, 40)]);
        route.Apply();

        GraphLayout read = GraphLayout.Read(layout.Write());
        Assert.Equal([new GraphPoint(100, 40), new GraphPoint(180, 40)], read.Reroutes[wire]);

        new SetRerouteCommand(layout, wire, []).Apply();
        Assert.False(layout.Reroutes.ContainsKey(wire));
        route.Undo();
        Assert.False(layout.Reroutes.ContainsKey(wire));
    }

    // The sample with one more container (30) whose condition is the text given; "this" in it is the container.
    private static MissionGraph BuildWith(string condition) => Build(Sample.Replace(
        "Task_New(21, \"ConditionalContainer\"",
        $"Task_New(30, \"ConditionalContainer\", \"probe\", \"{condition}\", \"\", \"\"), Task_New(21, \"ConditionalContainer\"",
        StringComparison.Ordinal));

    private static MissionGraph Build(string source = Sample)
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript(source, QvmProgram.IgiVersion));
        ScriptVocabulary vocabulary = new ScriptVocabulary()
            .Add([new NativeSignature("Game_DisableMusic", [], null, NativeEffect.Action, ScriptContexts.Mission)])
            .AddFields("Terminal", [new NativeParameter("isHacked", ScriptType.Bool)])
            .AddFields("AnimTask", [new NativeParameter("isRun", ScriptType.Bool)])
            .AddFields("ConditionalContainer", [new NativeParameter("isRun", ScriptType.Bool)]);
        return MissionGraph.Build(document, TaskCatalogue.Build(document.Declarations.Values, vocabulary), vocabulary, LevelExpressionDialect.Igi1);
    }
}
