using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.Igi1;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Vocabulary;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>What a mod adds to a game's script vocabulary, and the check that keeps it out of a mission for the original.</summary>
public sealed class ModVocabularyTests
{
    /// <summary>A mod's new task type and constant are OpenIGI's alone; a type it replaces and the game's natives stay retail.</summary>
    [Fact]
    public void Mod_additions_are_marked_OpenIgi_only()
    {
        ScriptVocabulary vocabulary = Modded();

        Assert.Equal(ScriptRuntime.OpenIgi, vocabulary.RuntimeOf("ModDrone"));
        Assert.True(vocabulary.TaskFields.ContainsKey("ModDrone"));
        Assert.Equal(ScriptRuntime.OpenIgi, vocabulary.RuntimeOf("ModTest_Answer"));
        Assert.True(vocabulary.TryGetConstant("ModTest_Answer", out string? family, out int value));
        Assert.Equal((TitleScriptVocabulary.ModConstantFamily, 42), (family, value));

        Assert.Equal(ScriptRuntime.Retail, vocabulary.RuntimeOf("HumanSoldier"));
        Assert.Equal(ScriptRuntime.Retail, vocabulary.RuntimeOf("AIFunction_GetCurrentEventType"));
    }

    /// <summary>A mission for the original game that uses them is an error, which making it OpenIGI only clears.</summary>
    [Fact]
    public void A_retail_mission_using_them_is_told_to_become_OpenIgi_only()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript("""
            Task_DeclareParameters("LevelFlow", "Complete", "VarString");
            Task_New(-1, "Container", "Root",
                Task_New(1, "ModDrone", "drone"),
                Task_New(2, "LevelFlow", "flow", "ModTest_Answer == 42"));
            """, QvmProgram.IgiVersion));
        Facts facts = new(Modded());

        MissionIssue[] target = [.. MissionValidator.Validate(document, facts).Where(static i => i.Check == "Target")];
        Assert.Equal(2, target.Length);
        Assert.All(target, static i => Assert.Equal(IssueSeverity.Error, i.Severity));
        Assert.Contains(target, static i => i.Task?.Label == "drone");
        Assert.Contains(target, static i => i.Task?.Label == "flow" && i.Message.Contains("ModTest_Answer", StringComparison.Ordinal));

        target[0].Fixes[0].Make()!.Apply();
        Assert.Equal(MissionTarget.OpenIgi, facts.Target);
        Assert.DoesNotContain(MissionValidator.Validate(document, facts), static i => i.Check == "Target");
    }

    // Project I.G.I.'s vocabulary with a mod that adds a type and a constant, and replaces the soldier.
    private static ScriptVocabulary Modded()
    {
        Igi1Runtime runtime = new();
        return runtime.CreateScriptVocabulary().AddAdditions(runtime.Modules, [.. runtime.Modules, new Mod()]);
    }

    private sealed class Mod : IContentModule
    {
        public string Name => "Mod.Test";

        public void Register(ContentRegistrationContext context)
        {
            context.Types.Register("ModDrone", static () => new Drone());
            context.Types.Replace("HumanSoldier", static () => new Drone());
            context.Natives.RegisterConstant("ModTest_Answer", 42);
        }

        public void LoadTables(ContentLoadContext context)
        {
        }
    }

    private sealed class Drone : GameTask;

    private sealed class Facts(ScriptVocabulary vocabulary) : IMissionFacts
    {
        public MissionTarget Target { get; private set; } = MissionTarget.Retail;

        public ScriptVocabulary? Vocabulary => vocabulary;

        public bool? HasModel(string model) => null;

        public double? GroundUnder(Vector3d pointMetres) => null;

        public IEditorCommand? MakeOpenIgiOnly() => new DelegateCommand("OpenIGI", () => Target = MissionTarget.OpenIgi, () => Target = MissionTarget.Retail);
    }
}
