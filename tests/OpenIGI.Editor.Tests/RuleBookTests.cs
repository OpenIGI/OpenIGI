using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Rules;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Rules as sentences, kept as the action layer's chains and run by the game as the tasks those compile to.</summary>
public sealed class RuleBookTests
{
    /// <summary>
    /// A rule written as a list (music off, wait two seconds, send the message, lock the menu) runs on the very ticks the
    /// same chain wired by hand does.
    /// </summary>
    [Fact]
    public void A_rule_runs_as_the_chain_it_is_kept_as()
    {
        Rule rule = new("hacked", "Terminal_10.isHacked",
        [
            new CallStep("music", "Game_DisableMusic()"),
            new WaitStep("wait", 2),
            new DriveStep("say", new GraphNodeKey("t40"), "Send"),
            new CallStep("lock", "MenuManager_SetEnabled(FALSE)"),
        ]);
        ExecLayer layer = new();
        RuleBook.With(layer, rule).Into(layer);

        ExecLayerTests.Simulation run = ExecLayerTests.Lower(layer, out MissionDocument document);
        run.Set("Terminal_10.isHacked", 1, fromTick: 10);
        run.Ticks(200);

        Assert.Equal([10], run.CallsOf("Game_DisableMusic"));
        Assert.Equal([70], run.CallsOf("MenuManager_SetEnabled"));
        Assert.Equal(71, run.FirstTrue(document, "StatusMessage", "Send"));
    }

    /// <summary>A rule's conditions are read with what starts it: it runs the first tick all of them hold, and once.</summary>
    [Fact]
    public void A_rule_waits_for_every_condition()
    {
        Rule rule = new("hacked", "Terminal_10.isHacked", [new CallStep("go", "Go()")]) { Conditions = ["Terminal_10.isOn"] };
        ExecLayer layer = new();
        RuleBook.With(layer, rule).Into(layer);

        ExecLayerTests.Simulation run = ExecLayerTests.Lower(layer, out _);
        run.Set("Terminal_10.isHacked", 1, fromTick: 5);
        run.Set("Terminal_10.isOn", 1, fromTick: 30);
        run.Ticks(100);

        Assert.Equal([30], run.CallsOf("Go"));
    }

    /// <summary>A rule switched off is kept in the layer and read back, and compiles to nothing.</summary>
    [Fact]
    public void A_rule_switched_off_does_nothing()
    {
        Rule rule = new("hacked", "Terminal_10.isHacked", [new CallStep("go", "Go()")]) { IsEnabled = false, Name = "Off for now" };
        ExecLayer layer = new();
        RuleBook.With(layer, rule).Into(layer);

        ExecLayerTests.Simulation run = ExecLayerTests.Lower(layer, out MissionDocument document);
        run.Set("Terminal_10.isHacked", 1, fromTick: 5);
        run.Ticks(50);

        Assert.Empty(run.CallsOf("Go"));
        Assert.DoesNotContain(document.EnumerateTasks(), static t => t.TypeName == "EditVariable");
        Rule read = Assert.Single(RuleBook.Read(layer));
        Assert.False(read.IsEnabled);
        Assert.Equal("Off for now", read.Name);
    }

    /// <summary>Every kind of step, blocks and waits inside blocks included, is read back as it was written, through the sidecar too.</summary>
    [Fact]
    public void A_rule_reads_back_as_written()
    {
        Rule rule = new("r1", "AreaActivate_7.nActive > 0",
        [
            new CallStep("a", "A()"),
            new WaitStep("w1", 1.5),
            new IfStep("if", "EditVariable_3.nValue",
                [new DriveStep("d", new GraphNodeKey("t40"), "Send"), new WaitStep("w2", 3), new CallStep("b", "B()")],
                [new CallStep("c", "C()")]),
            new MacroStep("m", "WaitThen", new Dictionary<string, string> { ["What"] = "Alpha" }),
            new WaitStep("w3", 4),
            new CallStep("e", "E()"),
        ])
        {
            Name = "Reach the exit",
            Group = "Objectives",
            Conditions = ["LevelFlow_1.nTick > 30"],
        };

        GraphLayout layout = new();
        RuleBook.With(layout.Actions, rule).Into(layout.Actions);
        Rule read = Assert.Single(RuleBook.Read(GraphLayout.Read(layout.Write()).Actions));

        Assert.False(read.IsFreeform);
        Assert.Equal(Describe(rule), Describe(read));
    }

    /// <summary>A rule put in again takes its old place and its old nodes; one removed takes only its own; a move reorders the rules.</summary>
    [Fact]
    public void Rules_are_replaced_removed_and_moved_among_the_others()
    {
        ExecLayer layer = new();
        foreach (string id in (string[])["one", "two", "three"])
        {
            RuleBook.With(layer, new Rule(id, $"{id}()", [new CallStep(id + "-a", "A()"), new WaitStep(id + "-w", 1), new CallStep(id + "-b", "B()")])).Into(layer);
        }

        RuleBook.With(layer, new Rule("two", "changed()", [new CallStep("two-b", "B()")])).Into(layer);
        Assert.Equal(["one", "two", "three"], RuleBook.Read(layer).Select(static r => r.Id));
        Assert.Equal("changed()", RuleBook.Read(layer)[1].When);
        Assert.DoesNotContain(layer.Nodes, static n => n.Id is "two-a" or "two-w");
        Assert.DoesNotContain(layer.Wires, static w => w.To is "two-a" or "two-w" || w.From == "two-w");

        RuleBook.Moved(layer, "three", 0).Into(layer);
        Assert.Equal(["three", "one", "two"], RuleBook.Read(layer).Select(static r => r.Id));

        RuleBook.Without(layer, "one").Into(layer);
        Assert.Equal(["three", "two"], RuleBook.Read(layer).Select(static r => r.Id));
        Assert.Equal(["three", "three-a", "three-w", "three-b", "two", "two-b"], layer.Nodes.Select(static n => n.Id));
        Assert.Equal(2, RuleBook.Read(layer)[0].Steps.Count(static s => s is CallStep));

        string fresh = RuleBook.NewId(layer, ["r7"]);
        Assert.DoesNotContain(layer.Nodes, n => n.Id == fresh);
        Assert.NotEqual("r7", fresh);
    }

    /// <summary>A chain wired in the graph as no list can say it (two waits side by side) is listed, and left to the graph.</summary>
    [Fact]
    public void A_chain_no_list_can_say_is_left_to_the_graph()
    {
        ExecLayer layer = new();
        layer.Nodes.AddRange(
        [
            new ExecNode("e", ExecNodeKind.OnBecomesTrue) { Condition = "X()" },
            new ExecNode("w1", ExecNodeKind.Delay) { Seconds = 1 },
            new ExecNode("w2", ExecNodeKind.Delay) { Seconds = 2 },
        ]);
        layer.Wires.AddRange([new ExecWire("e", ExecNode.Then, "w1"), new ExecWire("e", ExecNode.Then, "w2")]);

        Rule rule = Assert.Single(RuleBook.Read(layer));
        Assert.True(rule.IsFreeform);
        Assert.Empty(rule.Steps);
        Assert.Throws<ArgumentException>(() => RuleBook.With(layer, rule));
    }

    /// <summary>Two steps with one id, or a step with the id of another rule's, are refused.</summary>
    [Fact]
    public void A_step_id_used_twice_is_refused()
    {
        ExecLayer layer = new();
        RuleBook.With(layer, new Rule("one", "X()", [new CallStep("a", "A()")])).Into(layer);

        Assert.Throws<ArgumentException>(() => RuleBook.With(layer, new Rule("two", "Y()", [new CallStep("a", "A()")])));
        Assert.Throws<ArgumentException>(() => RuleBook.With(layer, new Rule("two", "Y()", [new CallStep("b", "A()"), new CallStep("b", "B()")])));
    }

    // A rule as text, for comparing two: records hold their lists by reference.
    private static string Describe(Rule rule) =>
        $"{rule.Id}|{rule.Name}|{rule.Group}|{rule.IsEnabled}|{rule.When}|{string.Join('&', rule.Conditions)}|{Describe(rule.Steps)}";

    private static string Describe(IReadOnlyList<RuleStep> steps) => string.Join(';', steps.Select(static step => step switch
    {
        CallStep call => $"call {call.Id} {call.Call}",
        DriveStep drive => $"drive {drive.Id} {drive.Target.Value}.{drive.Pin}",
        WaitStep wait => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"wait {wait.Id} {wait.Seconds}"),
        MacroStep macro => $"macro {macro.Id} {macro.Macro}({string.Join(',', macro.Arguments.OrderBy(static a => a.Key, StringComparer.Ordinal).Select(static a => a.Key + "=" + a.Value))})",
        IfStep block => $"if {block.Id} {block.Condition} [{Describe(block.Then)}] else [{Describe(block.Otherwise)}]",
        _ => step.ToString() ?? string.Empty,
    }));
}
