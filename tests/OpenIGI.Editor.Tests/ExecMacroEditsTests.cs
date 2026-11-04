using OpenIGI.Editor.Core.Graphs.Exec;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Collapsing action nodes into a project macro, and expanding a macro node back.</summary>
public sealed class ExecMacroEditsTests
{
    // On alarm: wait $Time, then show a message, then end.
    private static ExecLayer Chain()
    {
        ExecLayer layer = new();
        layer.Nodes.Add(new ExecNode("on", ExecNodeKind.OnBecomesTrue) { Condition = "AlarmControl_98.isAlarm" });
        layer.Nodes.Add(new ExecNode("wait", ExecNodeKind.Delay) { Seconds = 2 });
        layer.Nodes.Add(new ExecNode("say", ExecNodeKind.Call) { Call = "StatusMessage_Send($Line)" });
        layer.Nodes.Add(new ExecNode("end", ExecNodeKind.Call) { Call = "Game_End()" });
        layer.Wires.Add(new ExecWire("on", ExecNode.Then, "wait"));
        layer.Wires.Add(new ExecWire("wait", ExecNode.Then, "say"));
        layer.Wires.Add(new ExecWire("say", ExecNode.Then, "end"));
        return layer;
    }

    /// <summary>The middle of a chain collapses into one macro node wired where the nodes were; its $ names are parameters.</summary>
    [Fact]
    public void Collapse_wires_the_macro_node_in_place()
    {
        ExecLayer layer = Chain();
        (ExecMacro macro, ExecLayerState state) = ExecMacroEdits.Collapse(layer, ["wait", "say"], "WaitThenSay", "use1")!.Value;

        Assert.Equal(["Line"], macro.Parameters);
        Assert.Equal(["entry", "wait", "say", "exit"], macro.Body.Nodes.Select(static n => n.Id));
        Assert.Contains(new ExecWire("entry", ExecNode.Then, "wait"), macro.Body.Wires);
        Assert.Contains(new ExecWire("say", ExecNode.Then, "exit"), macro.Body.Wires);

        Assert.Equal(["on", "end", "use1"], state.Nodes.Select(static n => n.Id));
        Assert.Equal([new ExecWire("on", ExecNode.Then, "use1"), new ExecWire("use1", ExecNode.Then, "end")], state.Wires);
    }

    /// <summary>Collapsed and expanded again, the chain runs the same nodes in the same order, under the use's prefix.</summary>
    [Fact]
    public void Expand_undoes_collapse()
    {
        ExecLayer layer = Chain();
        (ExecMacro macro, ExecLayerState collapsed) = ExecMacroEdits.Collapse(layer, ["wait", "say"], "WaitThenSay", "use1")!.Value;
        ExecLayer used = new();
        collapsed.Into(used);
        used.Nodes[^1] = used.Nodes[^1] with { Arguments = new Dictionary<string, string> { ["Line"] = "\"M1_MSG_3\"" } };

        (ExecLayerState expanded, IReadOnlyList<string> added) = ExecMacroEdits.Expand(used, "use1", macro)!.Value;

        Assert.Equal(["use1/wait", "use1/say"], added);
        Assert.Equal(
            [new ExecWire("on", ExecNode.Then, "use1/wait"), new ExecWire("use1/wait", ExecNode.Then, "use1/say"), new ExecWire("use1/say", ExecNode.Then, "end")],
            expanded.Wires.OrderBy(static w => w.From == "on" ? 0 : w.From == "use1/wait" ? 1 : 2));
        Assert.Equal("StatusMessage_Send(\"M1_MSG_3\")", expanded.Nodes.Single(static n => n.Id == "use1/say").Call);

        // The compiler's own expansion of the macro node runs the same chain.
        ExecLayer flat = ExecMacroExpander.Expand(used, new Dictionary<string, ExecMacro> { [macro.Name] = macro });
        Assert.Equal(expanded.Wires.Count, flat.Wires.Count);
    }

    /// <summary>An event starts a chain of its own, so it never goes into a macro.</summary>
    [Fact]
    public void An_event_stays_out_of_a_macro() =>
        Assert.Null(ExecMacroEdits.Collapse(Chain(), ["on"], "Nothing", "use1"));
}
