using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Vocabulary;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The task catalogue: declared parameters and exported fields become typed pins.</summary>
public sealed class TaskCatalogueTests
{
    private const string Declarations = """
        Task_DeclareParameters("ConditionalContainer", "Condition", "VarString", "Run at start", "VarString", "Run at stop", "VarString");
        Task_DeclareParameters("HumanAI", "AI Type", "String32", "Graph ID", "Int32");
        Task_DeclareParameters("StatusMessage", "Position", "ObjectPos", "Send", "VarString", "Text", "VarString", "Duration", "Real32");
        Task_DeclareParameters("EditRigidObj", "Position", "ObjectPos", "Orientation", "Real32x9", "Model", "String16");
        """;

    /// <summary>Conditions, actions, references and values each get the pin their role calls for.</summary>
    [Fact]
    public void Parameters_become_typed_pins()
    {
        TaskCatalogue catalogue = Build();

        TaskNodeDefinition container = catalogue.Definitions["ConditionalContainer"];
        Assert.True(container.IsContainer);
        Assert.Equal([ScriptType.Bool, ScriptType.Exec, ScriptType.Exec], container.Inputs.Select(static p => p.Type));
        Assert.All(container.Inputs, static p => Assert.True(p.IsLinkable));
        Assert.Equal("isRun", Assert.Single(container.Outputs).Name);

        TaskNodeDefinition ai = catalogue.Definitions["HumanAI"];
        Assert.Equal(ScriptType.TaskOf("AIGraph"), ai.Inputs[1].Type);
        Assert.Equal(ScriptType.Text, ai.Inputs[0].Type);
        Assert.False(ai.Inputs[0].IsLinkable);

        TaskNodeDefinition message = catalogue.Definitions["StatusMessage"];
        Assert.Equal(
            [ScriptType.Vector3, ScriptType.Bool, ScriptType.Text, ScriptType.Real],
            message.Inputs.Select(static p => p.Type));
        Assert.Equal(TextRole.TextResource, message.Inputs[2].Role);
        Assert.True(message.IsLogic);
    }

    /// <summary>A prop is not logic, and the undeclared plain container is always there.</summary>
    [Fact]
    public void Props_are_not_logic_and_container_is_built_in()
    {
        TaskCatalogue catalogue = Build();

        Assert.False(catalogue.Definitions["EditRigidObj"].IsLogic);
        Assert.Equal(ScriptType.Orientation, catalogue.Definitions["EditRigidObj"].Inputs[1].Type);
        Assert.True(catalogue.TryGet(TaskCatalogue.ContainerType, out TaskNodeDefinition? plain));
        Assert.True(plain.IsContainer);
    }

    private static TaskCatalogue Build()
    {
        MissionDocument document = MissionDocument.Read(QscParser.ParseScript(Declarations, QvmProgram.IgiVersion));
        ScriptVocabulary vocabulary = new ScriptVocabulary()
            .AddFields("ConditionalContainer", [new NativeParameter("isRun", ScriptType.Bool)])
            .AddFields("StatusMessage", [new NativeParameter("isSendt", ScriptType.Bool)]);
        return TaskCatalogue.Build(document.Declarations.Values, vocabulary);
    }
}
