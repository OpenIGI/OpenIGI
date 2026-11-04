using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>AI scripts split one graph per event, laid out as exec flow, with literals written onto their pins.</summary>
public sealed class EventGraphViewTests
{
    private const string Sample = """
        AIFunction_SetName("guard");
        if (AIFunction_GetCurrentEventType() == AIEVENT_IDLE)
        {
            AIAction_Patrol(5, 0, 1);
            if (AIFunction_GetDistance() > 10) { AIAction_Idle(1); } else { AIAction_Run(2); }
        }
        else if (AIFunction_GetCurrentEventType() == AIEVENT_SEE_ENEMY)
        {
            AIAction_Shoot(AIFunction_GetTarget(), 3);
        }
        """;

    /// <summary>Each event is a graph; what runs before the chain is the setup; the event test itself is not drawn.</summary>
    [Fact]
    public void Each_event_is_a_graph()
    {
        EventGraph graph = EventGraph.Read(QscParser.ParseScript(Sample, QvmProgram.IgiVersion));
        IReadOnlyList<EventScope> scopes = EventGraphView.Scopes(graph);

        Assert.Equal(["Setup", "On Idle", "On See Enemy"], scopes.Select(static s => s.Title));
        EventScope idle = scopes[1];
        EventNodeView patrol = EventGraphView.Describe(graph, idle, idle.Members.Single(k => graph.Nodes[k] is EventStatementNode { Text: "AIAction_Patrol" }));
        Assert.Equal("Patrol", patrol.Title);
        Assert.Equal(["5", "0", "1"], patrol.Inputs.Where(static p => !p.IsExec).Select(static p => p.Value));
        Assert.DoesNotContain(idle.Members, k => graph.Nodes[k] is EventValueNode { Text: EventGraph.EventTypeNative });

        EventScope enemy = scopes[2];
        Assert.Contains(enemy.Members, k => graph.Nodes[k] is EventValueNode { Text: "AIFunction_GetTarget" });
    }

    /// <summary>
    /// Editing an event: a native added and wired after the first statement, its value set, the first statement deleted with
    /// its chain closed; the script written says so, and undoing every step gives back the script it was, byte for byte.
    /// </summary>
    [Fact]
    public void Edits_write_back_and_undo_to_the_original()
    {
        QscScript script = QscParser.ParseScript(Sample, QvmProgram.IgiVersion);
        EventGraph graph = EventGraph.Read(script);
        EventScope idle = EventGraphView.Scopes(graph)[1];
        GraphNodeKey patrol = idle.Members.Single(k => graph.Nodes[k] is EventStatementNode { Text: "AIAction_Patrol" });
        NativeSignature wait = new("AIAction_Wait", [new NativeParameter("seconds", ScriptType.Int)], null, NativeEffect.Action, ScriptContexts.Ai);
        List<IEditorCommand> done = [];
        void Do(IEditorCommand? command)
        {
            Assert.NotNull(command);
            command.Apply();
            done.Add(command);
        }

        (EventGraphEditCommand add, GraphNodeKey added) = EventGraphEdits.AddCall(graph, wait, idle, static () => { });
        Do(add);
        GraphPinRef afterPatrol = graph.Links.Single(l => l.From == new GraphPinRef(patrol, EventStatementNode.Next)).To;
        Do(EventGraphEdits.Link(graph, new GraphPinRef(patrol, EventStatementNode.Next), new GraphPinRef(added, EventStatementNode.In), static () => { }));
        Do(EventGraphEdits.Link(graph, new GraphPinRef(added, EventStatementNode.Next), afterPatrol, static () => { }));
        Do(EventGraphEdits.SetValue(graph, new GraphPinRef(added, "0"), "7", static () => { }));
        Do(EventGraphEdits.Delete(graph, [patrol], static () => { }));
        Assert.Contains(EventGraphView.Scopes(graph)[1].Members, k => k == added);

        string written = QscPrinter.Print(EventGraphWriter.Write(graph));
        Assert.Contains("AIAction_Wait(7);", written, StringComparison.Ordinal);
        Assert.DoesNotContain("AIAction_Patrol", written, StringComparison.Ordinal);
        Assert.True(written.IndexOf("AIAction_Wait", StringComparison.Ordinal) < written.IndexOf("AIFunction_GetDistance", StringComparison.Ordinal));

        foreach (IEditorCommand command in Enumerable.Reverse(done))
        {
            command.Undo();
        }

        Assert.Equal(QscCompiler.Compile(script), QscCompiler.Compile(EventGraphWriter.Write(graph)));
    }

    /// <summary>
    /// Flow and values added in an event write as script: a Branch whose condition is a comparison wired in, a While that
    /// does not spin; wiring the False side of an if written without an else gives it one.
    /// </summary>
    [Fact]
    public void Branches_loops_and_values_write_back()
    {
        EventGraph graph = EventGraph.Read(QscParser.ParseScript(Sample, QvmProgram.IgiVersion));
        EventScope enemy = EventGraphView.Scopes(graph)[2];
        GraphNodeKey shoot = enemy.Members.Single(k => graph.Nodes[k] is EventStatementNode { Text: "AIAction_Shoot" });
        GraphPinRef shootNext = new(shoot, EventStatementNode.Next);

        (EventGraphEditCommand addBranch, GraphNodeKey branch) = EventGraphEdits.AddFlow(graph, EventStatementKind.Branch, enemy, static () => { });
        addBranch.Apply();
        EventGraphEdits.Link(graph, shootNext, new GraphPinRef(branch, EventStatementNode.In), static () => { })!.Apply();
        (EventGraphEditCommand addLoop, GraphNodeKey loop) = EventGraphEdits.AddFlow(graph, EventStatementKind.Loop, enemy, static () => { });
        addLoop.Apply();
        EventGraphEdits.Link(graph, new GraphPinRef(branch, EventStatementNode.False), new GraphPinRef(loop, EventStatementNode.In), static () => { })!.Apply();
        (EventGraphEditCommand addGreater, GraphNodeKey greater) = EventGraphEdits.AddValue(graph, new EventValueNode(default, EventValueKind.Binary, 2) { Binary = QscBinaryOperator.Greater }, enemy, static () => { });
        addGreater.Apply();
        EventGraphEdits.Link(graph, new GraphPinRef(greater, GraphNode.Output), new GraphPinRef(branch, EventStatementNode.Condition), static () => { })!.Apply();
        EventGraphEdits.SetValue(graph, new GraphPinRef(greater, "1"), "5", static () => { })!.Apply();
        Assert.Contains(greater, EventGraphView.Scopes(graph)[2].Members);

        string written = QscPrinter.Print(EventGraphWriter.Write(graph));
        Assert.Contains("if (0 > 5)", written, StringComparison.Ordinal);
        Assert.Contains("while (0)", written, StringComparison.Ordinal);

        // The idle event's inner if has an else already; take a fresh one without.
        EventGraph plain = EventGraph.Read(QscParser.ParseScript("if (AIFunction_GetDistance() > 10) { AIAction_Idle(1); } AIAction_Run(2);", QvmProgram.IgiVersion));
        EventScope setup = EventGraphView.Scopes(plain)[0];
        GraphNodeKey inner = plain.Nodes.Values.OfType<EventStatementNode>().Single(static n => n.Kind == EventStatementKind.Branch).Key;
        (EventGraphEditCommand addWait, GraphNodeKey wait) = EventGraphEdits.AddCall(plain, new NativeSignature("AIAction_Wait", [], null, NativeEffect.Action, ScriptContexts.Ai), setup, static () => { });
        addWait.Apply();
        EventGraphEdits.Link(plain, new GraphPinRef(inner, EventStatementNode.False), new GraphPinRef(wait, EventStatementNode.In), static () => { })!.Apply();
        Assert.Contains("else", QscPrinter.Print(EventGraphWriter.Write(plain)), StringComparison.Ordinal);
    }

    /// <summary>After a save, every node of the edited graph is found again in the graph the script reads back as.</summary>
    [Fact]
    public void Saved_layouts_follow_their_nodes()
    {
        EventGraph graph = EventGraph.Read(QscParser.ParseScript(Sample, QvmProgram.IgiVersion));
        EventScope idle = EventGraphView.Scopes(graph)[1];
        GraphNodeKey patrol = idle.Members.Single(k => graph.Nodes[k] is EventStatementNode { Text: "AIAction_Patrol" });
        (EventGraphEditCommand add, GraphNodeKey added) = EventGraphEdits.AddCall(graph, new NativeSignature("AIAction_Wait", [], null, NativeEffect.Action, ScriptContexts.Ai), idle, static () => { });
        add.Apply();
        GraphPinRef after = graph.Links.Single(l => l.From == new GraphPinRef(patrol, EventStatementNode.Next)).To;
        EventGraphEdits.Link(graph, new GraphPinRef(patrol, EventStatementNode.Next), new GraphPinRef(added, EventStatementNode.In), static () => { })!.Apply();
        EventGraphEdits.Link(graph, new GraphPinRef(added, EventStatementNode.Next), after, static () => { })!.Apply();

        EventGraph read = EventGraph.Read(QscDecompiler.Decompile(QscCompiler.Compile(EventGraphWriter.Write(graph))));
        IReadOnlyDictionary<GraphNodeKey, GraphNodeKey> map = EventGraphKeys.Map(graph, read);
        Assert.Equal(graph.Nodes.Count, map.Count);
        Assert.All(map, pair => Assert.Equal(graph.Nodes[pair.Key].Title, read.Nodes[pair.Value].Title));
        Assert.Equal("AIAction_Wait", ((EventStatementNode)read.Nodes[map[added]]).Text);
    }

    /// <summary>Every retail AI script of both titles: each drawn node in exactly one graph, and no two laid out on each other.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_ai_scripts_split_into_clean_graphs() => AssertClean(GameDataSource.Igi1);

    /// <summary>I.G.I. 2's soldier and squad scripts.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_ai_scripts_split_into_clean_graphs() => AssertClean(GameDataSource.Igi2);

    private static void AssertClean(GameDataSource source)
    {
        List<string> failures = [];
        int scripts = 0;
        foreach (string path in source.EnumerateFiles("*.qvm").Where(IsAiScript))
        {
            EventGraph graph = EventGraph.Read(QscDecompiler.Decompile(File.ReadAllBytes(path)));
            IReadOnlyList<EventScope> scopes = EventGraphView.Scopes(graph);
            scripts++;
            foreach (IGrouping<GraphNodeKey, EventScope> twice in scopes.SelectMany(s => s.Members.Select(k => (k, s))).GroupBy(static p => p.k, static p => p.s).Where(static g => g.Count() > 1))
            {
                failures.Add($"{path}: {twice.Key} in {twice.Count()} graphs");
            }

            foreach (EventScope scope in scopes)
            {
                GraphLayout layout = new();
                EventGraphView.Place(graph, scope, layout);
                List<(GraphNodeKey Key, GraphPoint At, double Height)> boxes = [.. scope.Members.Where(layout.Positions.ContainsKey)
                    .Select(k => (k, layout.Positions[k], EventGraphView.HeightOf(EventGraphView.Describe(graph, scope, k))))];
                for (int i = 0; i < boxes.Count && failures.Count < 20; i++)
                {
                    for (int j = i + 1; j < boxes.Count; j++)
                    {
                        (GraphNodeKey a, GraphPoint pa, double ha) = boxes[i];
                        (GraphNodeKey b, GraphPoint pb, double hb) = boxes[j];
                        if (pa.X < pb.X + 220 && pb.X < pa.X + 220 && pa.Y < pb.Y + hb && pb.Y < pa.Y + ha)
                        {
                            failures.Add($"{Path.GetFileName(path)} '{scope.Title}': {a} overlaps {b}");
                        }
                    }
                }

                if (boxes.Count != scope.Members.Count)
                {
                    failures.Add($"{Path.GetFileName(path)} '{scope.Title}': {scope.Members.Count - boxes.Count} nodes not placed");
                }
            }
        }

        Assert.True(scripts > 100, $"only {scripts} AI scripts");
        Assert.True(failures.Count == 0, $"{failures.Count} problems:\n" + string.Join('\n', failures.Take(20)));
    }

    private static bool IsAiScript(string path) =>
        string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "ai", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(path).Equals("settings.qvm", StringComparison.OrdinalIgnoreCase);
}
