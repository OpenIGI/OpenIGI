using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Scripting.Qsc;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Every retail AI script, read into an event graph and written back from it, is the script it was: byte for byte.</summary>
public sealed class EventGraphRoundTripTests
{
    /// <summary>Project I.G.I.'s archetype and per-task AI scripts.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_ai_scripts_survive_the_graph() => AssertRoundTrip(GameDataSource.Igi1, minimum: 800);

    /// <summary>I.G.I. 2's soldier and squad scripts.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_ai_scripts_survive_the_graph() => AssertRoundTrip(GameDataSource.Igi2, minimum: 1000);

    /// <summary>Two exec wires into one statement cannot be a script, and the writer says so instead of copying.</summary>
    [Fact]
    public void A_merged_exec_wire_is_refused()
    {
        QscScript script = QscParser.ParseScript("""
            if (AIFunction_GetCurrentEventType() == AIEVENT_IDLE) { AIAction_Idle(1); } else { AIFunction_DefaultHandler(); }
            """, Scripting.Qvm.QvmProgram.IgiVersion);
        EventGraph graph = EventGraph.Read(script);
        EventStatementNode branch = Assert.Single(graph.Nodes.Values.OfType<EventStatementNode>(), static n => n.Kind == EventStatementKind.Branch);
        Assert.Equal("AIEVENT_IDLE", branch.Event);
        Assert.Equal(QscPrinter.Print(script), QscPrinter.Print(EventGraphWriter.Write(graph)));

        // Wire the false arm into the true arm's statement as well.
        GraphLink idle = graph.Links.Single(l => l.From == new GraphPinRef(branch.Key, EventStatementNode.True));
        GraphNodeKey handler = graph.Links.Single(l => l.From == new GraphPinRef(branch.Key, EventStatementNode.False)).To.Node;
        graph.AddLink(new GraphLink(new GraphPinRef(handler, EventStatementNode.Next), idle.To));
        Assert.Throws<EventGraphWriteException>(() => EventGraphWriter.Write(graph));
    }

    private static void AssertRoundTrip(GameDataSource source, int minimum)
    {
        List<string> failures = [];
        int scripts = 0;
        int events = 0;
        foreach (string path in source.EnumerateFiles("*.qvm").Where(IsAiScript))
        {
            byte[] original = File.ReadAllBytes(path);
            QscScript script = QscDecompiler.Decompile(original);
            EventGraph graph = EventGraph.Read(script);
            events += graph.Nodes.Values.OfType<EventStatementNode>().Count(static n => n.Event is not null);
            QscScript written = EventGraphWriter.Write(graph);
            scripts++;
            if (QscPrinter.Print(written) != QscPrinter.Print(script))
            {
                failures.Add($"{path}: prints differently");
            }
            else if (!QscCompiler.Compile(written).AsSpan().SequenceEqual(original))
            {
                failures.Add($"{path}: compiles to a different image");
            }
        }

        Assert.True(scripts >= minimum, $"only {scripts} AI scripts");
        Assert.True(events > scripts, $"only {events} event branches in {scripts} scripts");
        Assert.True(failures.Count == 0, $"{failures.Count} of {scripts} changed:\n" + string.Join('\n', failures.Take(20)));
    }

    private static bool IsAiScript(string path) =>
        string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "ai", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(path).Equals("settings.qvm", StringComparison.OrdinalIgnoreCase);
}
