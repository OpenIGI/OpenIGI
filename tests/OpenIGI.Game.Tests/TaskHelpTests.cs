using OpenIGI.Game.Igi1;
using OpenIGI.Scripting.Vocabulary;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>What a task type is, as the editor's help says it, comes from the game's own task classes.</summary>
public sealed class TaskHelpTests
{
    /// <summary>The types a level is built from and the parameters a level's logic hangs on have a line each.</summary>
    [Fact]
    public void The_task_classes_say_what_they_are()
    {
        ScriptVocabulary vocabulary = new Igi1Runtime().CreateScriptVocabulary();
        foreach (string type in (string[])["CutScene", "StatusMessage", "AreaActivate", "LevelFlow", "DefineComputerObjective", "ConditionalContainer", "LevelTimer", "EditVariable"])
        {
            Assert.False(string.IsNullOrWhiteSpace(vocabulary.HelpOf(type)), $"{type} has no help.");
        }

        Assert.False(string.IsNullOrWhiteSpace(vocabulary.HelpOf("CutScene", "Run")));
        Assert.DoesNotContain('<', vocabulary.HelpOf("CutScene")!);
    }
}
