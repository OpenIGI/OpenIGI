using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Vocabulary;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A mission split into small graphs, and how their nodes are named.</summary>
public sealed class GraphScopeTests
{
    private const string Sample = """
        Task_DeclareParameters("ConditionalContainer", "Condition", "VarString", "Run at start", "VarString", "Run at stop", "VarString");
        Task_DeclareParameters("Terminal", "Position", "ObjectPos");
        Task_DeclareParameters("AnimTask", "TargetID", "Int32", "Run", "VarString");
        Task_New(-1, "Container", "Root",
            Task_New(10, "Terminal", "t", 0, 0, 0),
            Task_New(11, "ConditionalContainer", "gate", "Terminal_10.isHacked && ComputerHilight_5.nValue > 3", "", "",
                Task_New(12, "AnimTask", "anim", 20, "this.isRun * GAME_FREQUENCY")),
            Task_New(40, "ConditionalContainer", "any", "Terminal_10.isHacked || Terminal_41.isHacked || Terminal_42.isHacked", "", ""),
            Task_New(41, "Terminal", "t41", 0, 0, 0),
            Task_New(42, "Terminal", "t42", 0, 0, 0),
            Task_New(30, "Container", "other",
                Task_New(31, "AnimTask", "anim2", 20, "Terminal_10.isHacked")));
        """;

    /// <summary>Each gate is a graph of its own; what a graph reads from elsewhere is drawn there as a reference.</summary>
    [Fact]
    public void Each_container_of_logic_is_a_graph_and_reads_are_references()
    {
        MissionGraph graph = Build();
        IReadOnlyList<GraphScope> scopes = GraphScopes.Build(graph);

        GraphScope gate = scopes.Single(s => s.Key == "t11");
        Assert.True(gate.IsGated);
        Assert.Contains(new GraphNodeKey("t11"), gate.Members);
        Assert.Contains(new GraphNodeKey("t12"), gate.Members);
        Assert.Contains(new GraphNodeKey("t10"), gate.References);

        GraphScope other = scopes.Single(s => s.Members.Contains(new GraphNodeKey("t31")));
        Assert.Equal("other", other.Title);
        Assert.Contains(new GraphNodeKey("t10"), other.References);
        Assert.Null(GraphScopes.HomeScope(scopes, new GraphNodeKey("t10")));
    }

    /// <summary>A literal or constant feeding one pin is that pin's inline value, not a node of its own.</summary>
    [Fact]
    public void Literals_become_inline_values()
    {
        MissionGraph graph = Build();
        GraphScope gate = GraphScopes.Build(graph).Single(s => s.Key == "t11");

        Assert.Contains(gate.Inline.Values, static v => v.Text == "3");
        Assert.Contains(gate.Inline.Values, static v => v.Text == "GAME_FREQUENCY");
        Assert.DoesNotContain(gate.Members, k => graph.Nodes[k] is ExpressionGraphNode { Kind: ExpressionNodeKind.Number or ExpressionNodeKind.Constant });
    }

    /// <summary>Operators and natives are named by what they do, with every pin named.</summary>
    [Fact]
    public void Nodes_are_named_by_what_they_do()
    {
        MissionGraph graph = Build();
        ExpressionGraphNode[] nodes = [.. graph.Nodes.Values.OfType<ExpressionGraphNode>()];
        ExpressionNodeLabel greater = ExpressionNodeText.Describe(nodes.First(static n => n.Operator == ScriptOperator.Greater));
        Assert.Equal("Greater", greater.Title);
        Assert.Equal(["A", "B"], greater.Inputs);
        Assert.Equal("A > B", greater.Subtitle);

        Assert.Equal(("Get Current Event Type", "AIFunction"), ExpressionNodeText.Humanise("AIFunction_GetCurrentEventType"));
        Assert.Equal(("Set Enabled", "MenuManager"), ExpressionNodeText.Humanise("MenuManager_SetEnabled"));
        Assert.Equal(("Disable Music", "Game"), ExpressionNodeText.Humanise("Game_DisableMusic"));
    }

    /// <summary>A run of one operator is one node with an input an operand; what it reads is still drawn, as references.</summary>
    [Fact]
    public void Operator_runs_fold_into_one_node()
    {
        MissionGraph graph = Build();
        GraphScope any = GraphScopes.Build(graph).Single(s => s.Key == "t40");

        (GraphNodeKey root, List<GraphPinRef> operands) = Assert.Single(any.Chains);
        Assert.Equal(3, operands.Count);
        Assert.Single(any.Folded);
        Assert.Contains(root, any.Members);
        Assert.DoesNotContain(any.Folded.Keys.Single(), any.Members);
        Assert.Contains(new GraphNodeKey("t41"), any.References);
        Assert.Contains(new GraphNodeKey("t10"), any.References);

        // Each operand pin resolves back to the real input it stands for.
        foreach (GraphPinRef operand in operands)
        {
            (GraphNodeKey node, string pin) = any.DrawnPin(operand);
            Assert.Equal(root, node);
            Assert.Equal(operand, GraphScope.Resolve(node.Value, pin));
        }
    }

    private static MissionGraph Build()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript(Sample, QvmProgram.IgiVersion));
        ScriptVocabulary vocabulary = new ScriptVocabulary()
            .Add([new ConstantFamily("Time", [new KeyValuePair<string, int>("GAME_FREQUENCY", 30)])])
            .AddFields("Terminal", [new NativeParameter("isHacked", ScriptType.Bool)])
            .AddFields("AnimTask", [new NativeParameter("isRun", ScriptType.Bool)])
            .AddFields("ConditionalContainer", [new NativeParameter("isRun", ScriptType.Bool)]);
        return MissionGraph.Build(document, TaskCatalogue.Build(document.Declarations.Values, vocabulary), vocabulary, LevelExpressionDialect.Igi1);
    }
}
