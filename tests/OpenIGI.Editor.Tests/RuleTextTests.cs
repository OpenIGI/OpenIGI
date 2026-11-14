using OpenIGI.Editor.Core.Graphs;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Rules;
using OpenIGI.Game.Igi1;
using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A rule's lines said in the mission's own names and the title's words, and its steps moved about.</summary>
public sealed class RuleTextTests
{
    private const string Level = """
        Task_DeclareParameters("StatusMessage", "Position", "ObjectPos", "Send", "VarString", "Text", "VarString");
        Task_DeclareParameters("Door", "Position", "ObjectPos", "Open door expression", "VarString", "Odd one", "VarString");
        Task_DeclareParameters("Terminal", "Position", "ObjectPos");
        Task_DeclareParameters("AreaActivate", "Position", "ObjectPos");
        Task_New(-1, "Container", "Root",
            Task_New(10, "Terminal", "Main terminal", 0, 0, 0),
            Task_New(11, "AreaActivate", "", 0, 0, 0),
            Task_New(20, "Door", "Gate", 0, 0, 0, "", ""),
            Task_New(40, "StatusMessage", "Welcome", 0, 0, 0, "", "M1_MSG_1"));
        """;

    /// <summary>
    /// One field read is said as the task's name and the title's words for the field, negated and compared too; anything
    /// longer, and a read of a task the mission lacks, is shown as written.
    /// </summary>
    [Fact]
    public void A_condition_is_said_in_the_missions_names()
    {
        RuleText text = Text();

        Assert.Equal("Main terminal is hacked", text.Condition("Terminal_10.isHacked"));
        Assert.Equal("not: Main terminal is hacked", text.Condition("!Terminal_10.isHacked"));
        Assert.Equal("AreaActivate 11 count inside > 0", text.Condition(" AreaActivate_11.nActive > 0 "));
        Assert.Equal("Main terminal someField", text.Condition("Terminal_10.someField"));
        Assert.Equal("Terminal_10.isHacked && Door_20.isOpen", text.Condition("Terminal_10.isHacked && Door_20.isOpen"));
        Assert.Equal("Terminal_99.isHacked", text.Condition("Terminal_99.isHacked"));
        Assert.Equal("(nothing yet)", text.Condition(string.Empty));
    }

    /// <summary>A step says what it does: the title's words for a condition made to hold, the parameter's name where it has none.</summary>
    [Fact]
    public void A_step_says_what_it_does()
    {
        RuleText text = Text();

        Assert.Equal("Show the message: Welcome", text.Step(new DriveStep("a", new GraphNodeKey("t40"), "Send")));
        Assert.Equal("Open the door: Gate", text.Step(new DriveStep("b", new GraphNodeKey("t20"), "Open door expression")));
        Assert.Equal("Make 'Odd one' hold: Gate", text.Step(new DriveStep("c", new GraphNodeKey("t20"), "Odd one")));
        Assert.Equal("Make a condition hold (its task is gone)", text.Step(new DriveStep("d", new GraphNodeKey("t77"), "Send")));
        Assert.Equal("Wait 2.5 s", text.Step(new WaitStep("e", 2.5)));
        Assert.Equal("Call Game_DisableMusic()", text.Step(new CallStep("f", "Game_DisableMusic()")));
        Assert.Equal("If Main terminal is on", text.Step(new IfStep("g", "Terminal_10.isOn", [], [])));
        Assert.Equal("When Main terminal is hacked", text.Title(new Rule("r", "Terminal_10.isHacked", [])));
        Assert.Equal("Alarm", text.Title(new Rule("r", "Terminal_10.isHacked", []) { Name = "Alarm" }));
    }

    /// <summary>Steps go in after a step, into a block's either half, or at the end; move among their own list; and come out with what is inside them.</summary>
    [Fact]
    public void Steps_are_put_in_moved_and_taken_out()
    {
        Rule rule = new("r", "X()", [new CallStep("a", "A()"), new IfStep("if", "Y()", [new CallStep("t", "T()")], [])]);

        rule = RuleEdits.Insert(rule, new CallStep("b", "B()"), new StepPlace("a"));
        rule = RuleEdits.Insert(rule, new CallStep("t2", "T2()"), new StepPlace("if", Inside: true));
        rule = RuleEdits.Insert(rule, new CallStep("e", "E()"), new StepPlace("if", Inside: true, Otherwise: true));
        rule = RuleEdits.Insert(rule, new CallStep("t0", "T0()"), new StepPlace("t"));
        rule = RuleEdits.Insert(rule, new CallStep("z", "Z()"), new StepPlace(null));
        rule = RuleEdits.Insert(rule, new CallStep("y", "Y()"), new StepPlace("no such step"));
        Assert.Equal(["a", "b", "if", "t", "t0", "t2", "e", "z", "y"], rule.AllSteps().Select(static s => s.Id));

        rule = RuleEdits.Move(rule, "t2", -5);
        rule = RuleEdits.Move(rule, "a", 1);
        Assert.Equal(["b", "a", "if", "t2", "t", "t0", "e", "z", "y"], rule.AllSteps().Select(static s => s.Id));

        rule = RuleEdits.Replace(rule, "e", static s => ((CallStep)s) with { Call = "Changed()" });
        Assert.Equal("Changed()", ((CallStep)RuleEdits.Find(rule, "e")!).Call);

        rule = RuleEdits.Remove(RuleEdits.Remove(rule, "if"), "z");
        Assert.Equal(["b", "a", "y"], rule.AllSteps().Select(static s => s.Id));
        Assert.Null(RuleEdits.Find(rule, "t"));
    }

    /// <summary>Every condition the first game's words name is one its levels declare, under that very name.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Every_named_action_is_a_parameter_the_levels_declare()
    {
        ScriptVocabulary vocabulary = new Igi1Runtime().CreateScriptVocabulary();
        Dictionary<string, HashSet<string>> declared = new(StringComparer.Ordinal);
        foreach (string path in OpenIGI.TestSupport.GameDataSource.Igi1.EnumerateFiles("objects.qvm"))
        {
            foreach (TaskDeclaration declaration in MissionDocument.Read(File.ReadAllBytes(path)).Declarations.Values)
            {
                if (!declared.TryGetValue(declaration.TypeName, out HashSet<string>? names))
                {
                    declared[declaration.TypeName] = names = new(StringComparer.Ordinal);
                }

                names.UnionWith(declaration.Parameters.Where(static p => p.Type == OpenIGI.Engine.Tasks.ScriptParamType.VarString).Select(static p => p.Name));
            }
        }

        string[] unknown = [.. vocabulary.Actions
            .Where(a => !declared.TryGetValue(a.TaskType, out HashSet<string>? names) || !names.Contains(a.Parameter))
            .Select(static a => $"{a.TaskType}.{a.Parameter}")];
        Assert.True(unknown.Length == 0, "Not declared by any level: " + string.Join(", ", unknown));
    }

    private static RuleText Text()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript(Level, QvmProgram.IgiVersion));
        ScriptVocabulary vocabulary = new Igi1Runtime().CreateScriptVocabulary();
        MissionGraph graph = MissionGraph.Build(document, TaskCatalogue.Build(document.Declarations.Values, vocabulary), vocabulary, LevelExpressionDialect.Igi1);
        return new RuleText(document, graph, vocabulary);
    }
}
