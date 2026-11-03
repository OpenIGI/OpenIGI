using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Graphs.Exec;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Vocabulary;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Action nodes lowered into retail tasks, then run tick by tick as the game runs those tasks.</summary>
public sealed class ExecLayerTests
{
    private const string Level = """
        Task_DeclareParameters("EditVariable", "Position", "ObjectPos", "Initial value", "Int32", "Add", "VarString", "Sub", "VarString");
        Task_DeclareParameters("LevelTimer", "Position", "ObjectPos", "Orientation", "Real32x9", "On", "VarString", "Reset", "VarString", "Initial run", "bool8");
        Task_DeclareParameters("ConditionalContainer", "Condition", "VarString", "Run at start", "VarString", "Run at stop", "VarString");
        Task_DeclareParameters("StatusMessage", "Position", "ObjectPos", "Send", "VarString", "Text", "VarString");
        Task_DeclareParameters("Terminal", "Position", "ObjectPos");
        Task_New(-1, "Container", "Root",
            Task_New(10, "Terminal", "t", 0, 0, 0),
            Task_New(40, "StatusMessage", "msg", 0, 0, 0, "", "M1_MSG_1"));
        """;

    /// <summary>
    /// When the terminal is hacked: music off at once, then two seconds later the message sends and the menu is locked; each
    /// call runs exactly once.
    /// </summary>
    [Fact]
    public void An_event_a_delay_and_a_drive_run_when_and_as_often_as_wired()
    {
        ExecLayer layer = new();
        layer.Nodes.AddRange(
        [
            new ExecNode("hacked", ExecNodeKind.OnBecomesTrue) { Condition = "Terminal_10.isHacked" },
            new ExecNode("music", ExecNodeKind.Call) { Call = "Game_DisableMusic()" },
            new ExecNode("wait", ExecNodeKind.Delay) { Seconds = 2 },
            new ExecNode("say", ExecNodeKind.Drive) { Target = new GraphNodeKey("t40"), TargetPin = "Send" },
            new ExecNode("lock", ExecNodeKind.Call) { Call = "MenuManager_SetEnabled(FALSE)" },
        ]);
        layer.Wires.AddRange(
        [
            new ExecWire("hacked", ExecNode.Then, "music"),
            new ExecWire("hacked", ExecNode.Then, "wait"),
            new ExecWire("wait", ExecNode.Then, "say"),
            new ExecWire("wait", ExecNode.Then, "lock"),
        ]);

        Simulation run = Lower(layer, out MissionDocument document);
        run.Set("Terminal_10.isHacked", 1, fromTick: 10);
        run.Ticks(200);

        // The latch runs before the container in the tree, so the call comes the very tick the terminal is hacked.
        Assert.Equal([10], run.CallsOf("Game_DisableMusic"));
        Assert.Equal([70], run.CallsOf("MenuManager_SetEnabled"));
        // The message stands before the generated tasks in the tree, so it reads the timer before the timer counts that tick:
        // a tick later, as the game itself would run it.
        Assert.Equal(71, run.FirstTrue(document, "StatusMessage", "Send"));
        Assert.Equal(2, document.EnumerateTasks().Count(static t => t.TypeName == "ConditionalContainer"));
    }

    /// <summary>A branch reads its condition once: what it decided stands though the condition changes after.</summary>
    [Fact]
    public void A_branch_decides_once()
    {
        ExecLayer layer = new();
        layer.Nodes.AddRange(
        [
            new ExecNode("hacked", ExecNodeKind.OnBecomesTrue) { Condition = "Terminal_10.isHacked" },
            new ExecNode("which", ExecNodeKind.Branch) { Condition = "Terminal_10.isOn" },
            new ExecNode("yes", ExecNodeKind.Call) { Call = "Yes()" },
            new ExecNode("no", ExecNodeKind.Call) { Call = "No()" },
        ]);
        layer.Wires.AddRange(
        [
            new ExecWire("hacked", ExecNode.Then, "which"),
            new ExecWire("which", ExecNode.True, "yes"),
            new ExecWire("which", ExecNode.False, "no"),
        ]);

        Simulation run = Lower(layer, out _);
        run.Set("Terminal_10.isHacked", 1, fromTick: 5);
        run.Set("Terminal_10.isOn", 1, fromTick: 40);
        run.Ticks(100);

        Assert.Single(run.CallsOf("No"));
        Assert.Empty(run.CallsOf("Yes"));
    }

    /// <summary>Lowering again keeps every generated id, and undo puts the mission back as it was.</summary>
    [Fact]
    public void Lowering_again_keeps_ids_and_undoes()
    {
        ExecLayer layer = new();
        layer.Nodes.Add(new ExecNode("hacked", ExecNodeKind.OnBecomesTrue) { Condition = "Terminal_10.isHacked" });
        layer.Nodes.Add(new ExecNode("music", ExecNodeKind.Call) { Call = "Game_DisableMusic()" });
        layer.Wires.Add(new ExecWire("hacked", ExecNode.Then, "music"));
        MissionDocument document = Document();
        string before = QscPrinter.Print(document.ToScript());

        Core.Commands.IEditorCommand first = Compile(layer, document);
        first.Apply();
        int[] ids = [.. layer.Generated.Values.SelectMany(static i => i)];
        Compile(layer, document).Apply();
        Assert.Equal(ids, layer.Generated.Values.SelectMany(static i => i));
        Assert.Single(document.EnumerateTasks(), static t => t.Label == ExecLayerCompiler.ContainerLabel);

        MissionDocument fresh = Document();
        Core.Commands.IEditorCommand once = Compile(new ExecLayer { }, fresh);
        once.Apply();
        once.Undo();
        Assert.Equal(before, QscPrinter.Print(fresh.ToScript()));
    }

    /// <summary>The action layer is kept in the graph's sidecar, generated ids and all.</summary>
    [Fact]
    public void The_layer_survives_the_sidecar()
    {
        GraphLayout layout = new();
        layout.Actions.Nodes.Add(new ExecNode("e", ExecNodeKind.OnBecomesTrue) { Condition = "Terminal_10.isHacked" });
        layout.Actions.Nodes.Add(new ExecNode("d", ExecNodeKind.Delay) { Seconds = 2.5 });
        layout.Actions.Nodes.Add(new ExecNode("s", ExecNodeKind.Drive) { Target = new GraphNodeKey("t40"), TargetPin = "Send" });
        layout.Actions.Wires.Add(new ExecWire("e", ExecNode.Then, "d"));
        layout.Actions.Generated["e"] = [4095];

        GraphLayout read = GraphLayout.Read(layout.Write());
        Assert.Equal(layout.Actions.Nodes, read.Actions.Nodes);
        Assert.Equal(layout.Actions.Wires, read.Actions.Wires);
        Assert.Equal([4095], read.Actions.Generated["e"]);
    }

    /// <summary>
    /// A macro used twice with different arguments lowers into two sets of tasks, each running its own call once, the second
    /// two seconds after its event as the macro's delay says.
    /// </summary>
    [Fact]
    public void A_macro_expands_per_use()
    {
        ExecLayer body = new();
        body.Nodes.AddRange(
        [
            new ExecNode("in", ExecNodeKind.MacroEntry),
            new ExecNode("wait", ExecNodeKind.Delay) { Seconds = 2 },
            new ExecNode("say", ExecNodeKind.Call) { Call = "$What()" },
            new ExecNode("out", ExecNodeKind.MacroExit),
        ]);
        body.Wires.AddRange([new ExecWire("in", ExecNode.Then, "wait"), new ExecWire("wait", ExecNode.Then, "say"), new ExecWire("say", ExecNode.Then, "out")]);
        ExecMacro macro = ExecMacroJson.Read(ExecMacroJson.Write(new ExecMacro("WaitThen", ["What"], body)));

        ExecLayer layer = new();
        layer.Nodes.AddRange(
        [
            new ExecNode("hacked", ExecNodeKind.OnBecomesTrue) { Condition = "Terminal_10.isHacked" },
            new ExecNode("first", ExecNodeKind.Macro) { Macro = "WaitThen", Arguments = new Dictionary<string, string> { ["What"] = "Alpha" } },
            new ExecNode("second", ExecNodeKind.Macro) { Macro = "WaitThen", Arguments = new Dictionary<string, string> { ["What"] = "Beta" } },
        ]);
        layer.Wires.AddRange([new ExecWire("hacked", ExecNode.Then, "first"), new ExecWire("first", ExecNode.Then, "second")]);

        ExecLayer flat = ExecMacroExpander.Expand(layer, new Dictionary<string, ExecMacro> { [macro.Name] = macro });
        Assert.DoesNotContain(flat.Nodes, static n => n.Kind is ExecNodeKind.Macro or ExecNodeKind.MacroEntry or ExecNodeKind.MacroExit);
        Assert.Contains(flat.Nodes, static n => n.Id == "second/say" && n.Call == "Beta()");

        Simulation run = Lower(flat, out _);
        run.Set("Terminal_10.isHacked", 1, fromTick: 10);
        run.Ticks(300);
        Assert.Equal([70], run.CallsOf("Alpha"));
        Assert.Equal([130], run.CallsOf("Beta"));
    }

    /// <summary>A macro that runs itself, however deep, is refused rather than expanded for ever.</summary>
    [Fact]
    public void A_macro_using_itself_is_refused()
    {
        ExecLayer body = new();
        body.Nodes.AddRange([new ExecNode("in", ExecNodeKind.MacroEntry), new ExecNode("again", ExecNodeKind.Macro) { Macro = "Loop" }]);
        body.Wires.Add(new ExecWire("in", ExecNode.Then, "again"));
        ExecLayer layer = new();
        layer.Nodes.Add(new ExecNode("use", ExecNodeKind.Macro) { Macro = "Loop" });

        Assert.Throws<ExecLayerException>(() =>
            ExecMacroExpander.Expand(layer, new Dictionary<string, ExecMacro> { ["Loop"] = new ExecMacro("Loop", [], body) }));
    }

    private static MissionDocument Document() => MissionDocument.Read(QscParser.ParseScript(Level, QvmProgram.IgiVersion));

    private static Core.Commands.IEditorCommand Compile(ExecLayer layer, MissionDocument document)
    {
        MissionGraph graph = Graph(document);
        return ExecLayerCompiler.ToCommand(document, layer, ExecLayerCompiler.Lower(layer, document, graph));
    }

    private static MissionGraph Graph(MissionDocument document)
    {
        ScriptVocabulary vocabulary = new();
        return MissionGraph.Build(document, TaskCatalogue.Build(document.Declarations.Values, vocabulary), vocabulary, LevelExpressionDialect.Igi1);
    }

    private static Simulation Lower(ExecLayer layer, out MissionDocument document)
    {
        document = Document();
        Compile(layer, document).Apply();
        return new Simulation(document);
    }

    /// <summary>The game's tick for the four task types lowering uses, over the game's own expression evaluator.</summary>
    private sealed class Simulation(MissionDocument document) : IScriptExpressionContext
    {
        private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
        private readonly List<(string Name, int Value, int From)> _inputs = [];
        private readonly Dictionary<string, List<int>> _calls = new(StringComparer.Ordinal);
        private readonly Dictionary<MissionTask, bool> _wasRun = [];
        private readonly Dictionary<(MissionTask, string), int> _firstTrue = [];
        private int _tick;

        public void Set(string name, int value, int fromTick) => _inputs.Add((name, value, fromTick));

        public List<int> CallsOf(string native) => _calls.GetValueOrDefault(native) ?? [];

        public int? FirstTrue(MissionDocument mission, string type, string parameter)
        {
            MissionTask task = mission.EnumerateTasks().First(t => t.TypeName == type);
            return _firstTrue.TryGetValue((task, parameter), out int tick) ? tick : null;
        }

        public void Ticks(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _tick++;
                foreach ((string name, int value, int from) in _inputs.Where(input => input.From == _tick))
                {
                    _values[name] = value;
                }

                foreach (MissionTask task in document.EnumerateTasks())
                {
                    Step(task);
                }
            }
        }

        public bool TryGetValue(string name, out QvmValue value)
        {
            value = QvmValue.FromDouble(_values.GetValueOrDefault(name));
            return true;
        }

        public QvmValueKind GetValueKind(string name) => QvmValueKind.Float;

        public bool TrySetValue(string name, QvmValue value)
        {
            _values[name] = value.ToDouble();
            return true;
        }

        public bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result)
        {
            if (!_calls.TryGetValue(name, out List<int>? ticks))
            {
                _calls[name] = ticks = [];
            }

            ticks.Add(_tick);
            result = QvmValue.FromInt32(1);
            return true;
        }

        private void Step(MissionTask task)
        {
            string self = $"{task.TypeName}_{task.Id}";
            switch (task.TypeName)
            {
                case "EditVariable":
                    _values[self + ".nValue"] = _values.GetValueOrDefault(self + ".nValue") + Math.Truncate(Eval(task, "Add")) - Math.Truncate(Eval(task, "Sub"));
                    break;

                case "LevelTimer":
                    bool running = Eval(task, "On") != 0;
                    _values[self + ".isRun"] = running ? 1 : 0;
                    if (running)
                    {
                        _values[self + ".nTick"] = _values.GetValueOrDefault(self + ".nTick") + 1;
                    }

                    break;

                case "ConditionalContainer":
                    bool run = Eval(task, "Condition") != 0;
                    if (run && !_wasRun.GetValueOrDefault(task))
                    {
                        Eval(task, "Run at start");
                    }

                    _wasRun[task] = run;
                    break;

                case "StatusMessage":
                    if (Eval(task, "Send") != 0)
                    {
                        _firstTrue.TryAdd((task, "Send"), _tick);
                    }

                    break;
            }
        }

        private static int? Constant(string name) => name switch
        {
            "TRUE" => 1,
            "FALSE" => 0,
            "GAME_FREQUENCY" => 30,
            _ => null,
        };

        private double Eval(MissionTask task, string parameter)
        {
            string text = task.FindParameter(parameter)?.Arguments is [QscStringLiteral literal] ? literal.Value : string.Empty;
            string compiled = LevelExpression.Compile(text, $"{task.TypeName}_{task.Id}", LevelExpressionDialect.Igi1);
            return ScriptExpressionParser.IsBlank(compiled)
                ? 0
                : new ScriptExpressionEvaluator(this).EvaluateReal(ScriptExpressionParser.Parse(compiled, Constant));
        }
    }
}
