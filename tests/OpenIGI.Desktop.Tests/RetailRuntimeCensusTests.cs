using System.Text;
using OpenIGI.Game.Igi1;
using OpenIGI.Game.Igi2;
using OpenIGI.Scripting.Vocabulary;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Everything a title declares in its own script vocabulary, the original executable names: the editor reads all of it as
/// the original's (<see cref="ScriptVocabulary.RuntimeOf"/>), and only what mods add as OpenIGI's alone.
/// </summary>
public sealed class RetailRuntimeCensusTests
{
    /// <summary>Project I.G.I.'s natives and task types are all in <c>IGI.exe</c>.</summary>
    [RequiresTitleDataFact("igi1")]
    public void Igi1_vocabulary_is_named_by_the_original() =>
        AssertNamed(new Igi1Runtime().CreateScriptVocabulary(), GameDataSource.Igi1.Resolve("IGI.exe"));

    /// <summary>IGI 2's natives and task types are all in <c>igi2.exe</c>.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Igi2_vocabulary_is_named_by_the_original() =>
        AssertNamed(new Igi2Runtime().CreateScriptVocabulary(), GameDataSource.Igi2.Resolve("igi2.exe"));

    private static void AssertNamed(ScriptVocabulary vocabulary, string executable)
    {
        Assert.True(File.Exists(executable), $"no {executable}");
        HashSet<string> named = Strings(File.ReadAllBytes(executable));
        Assert.NotEmpty(vocabulary.Natives);
        Assert.Equal([], [.. vocabulary.Natives.Keys.Where(n => !named.Contains(n))]);
        Assert.Equal([], [.. vocabulary.TaskFields.Keys.Where(n => !named.Contains(n))]);
        Assert.All(vocabulary.Natives.Keys, n => Assert.Equal(ScriptRuntime.Retail, vocabulary.RuntimeOf(n)));
    }

    // Every run of three or more printable ASCII bytes: the names the executable registers its natives and types by.
    private static HashSet<string> Strings(byte[] bytes)
    {
        HashSet<string> found = new(StringComparer.Ordinal);
        StringBuilder run = new();
        foreach (byte b in bytes)
        {
            if (b is >= 0x20 and < 0x7F)
            {
                run.Append((char)b);
                continue;
            }

            if (run.Length >= 3)
            {
                found.Add(run.ToString());
            }

            run.Clear();
        }

        return found;
    }
}
