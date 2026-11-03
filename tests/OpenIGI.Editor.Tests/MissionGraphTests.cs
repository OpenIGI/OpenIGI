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

    /// <summary>Values flow left to right: what a node reads stands left of it.</summary>
    [Fact]
    public void Auto_layout_puts_inputs_left()
    {
        MissionGraph graph = Build();
        GraphLayout layout = new();
        GraphAutoLayout.Place(graph, layout);

        GraphPinRef root = graph.ExpressionRoots[(new GraphNodeKey("t11"), "Condition")];
        Assert.True(layout.Positions[new GraphNodeKey("t10")].X < layout.Positions[root.Node].X);
        Assert.True(layout.Positions[root.Node].X < layout.Positions[new GraphNodeKey("t11")].X);
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

    private static MissionGraph Build()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript(Sample, QvmProgram.IgiVersion));
        ScriptVocabulary vocabulary = new ScriptVocabulary()
            .Add([new NativeSignature("Game_DisableMusic", [], null, NativeEffect.Action, ScriptContexts.Mission)])
            .AddFields("Terminal", [new NativeParameter("isHacked", ScriptType.Bool)])
            .AddFields("AnimTask", [new NativeParameter("isRun", ScriptType.Bool)])
            .AddFields("ConditionalContainer", [new NativeParameter("isRun", ScriptType.Bool)]);
        return MissionGraph.Build(document, TaskCatalogue.Build(document.Declarations.Values, vocabulary), vocabulary, LevelExpressionDialect.Igi1);
    }
}
