using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>
/// Pins the retail <c>objects.qvm</c> weather data that the port relies on: which levels ship a
/// <c>RainEffect</c> task, and which of them actually enable it through the <c>Is Rain</c> /
/// <c>Is Active</c> flags the reference editor gates on (<c>c40f092e</c>).
/// </summary>
/// <remarks>
/// This is a contract test against shipped data rather than a guess — without it nothing here is verified
/// against what the retail levels really author. It is skipped when <c>IGI_GAME_PATH</c> is absent, so the
/// open-source build still runs clean.
/// </remarks>
public sealed class RainEffectRetailTests
{
    private readonly ITestOutputHelper _output;

    public RainEffectRetailTests(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void RetailRainEffectFlagsMatchExpectedWeather()
    {
        // The levels the reference editor's Is Rain / Is Active gates actually fire on — rain that is
        // both authored and enabled. Authoring alone is not enough: 7/12 author a RainEffect with
        // Is Rain false, and 4/8/11 author one that is inactive.
        var expected = new Dictionary<string, (bool IsRain, bool IsActive)>
        {
            ["level3"] = (true, true),
            ["level9"] = (true, true),
            ["level10"] = (true, true),
            ["level4"] = (true, false),
            ["level7"] = (false, true),
            ["level8"] = (true, false),
            ["level11"] = (true, false),
            ["level12"] = (false, true),
            ["level13"] = (false, false),
            ["level14"] = (false, false),
            ["level1"] = (false, false),
            ["level2"] = (false, false),
            ["level5"] = (false, false),
            ["level6"] = (false, false),
        };

        string missionsRoot = Path.Combine(GameData.RequireRoot(), "MISSIONS");
        foreach (string script in Directory.GetFiles(missionsRoot, "objects.qvm", SearchOption.AllDirectories))
        {
            string level = Path.GetFileName(Path.GetDirectoryName(script)!);
            (TaskTree _, GameTask root) = Load(script);

            DeclaredTask? rain = FindRainEffect(root);
            bool isRain = rain is not null && ReadFlag(rain, "Is Rain");
            bool isActive = rain is not null && ReadFlag(rain, "Is Active");

            _output.WriteLine(
                $"{level}: present={rain is not null}, Is Rain={isRain}, Is Active={isActive}, " +
                $"start={Raw(rain, "Traceline start")}, end={Raw(rain, "Traceline end")}, alpha={Raw(rain, "Rain Alpha")}");

            Assert.True(expected.TryGetValue(level, out (bool IsRain, bool IsActive) e),
                $"Unrecognised level {level} in retail probe.");

            // A level with no RainEffect task has neither flag on; a level with one must match the
            // authored flags exactly so the editor's gate agrees with the data.
            if (rain is null)
            {
                Assert.False(e.IsRain || e.IsActive, $"{level} unexpectedly has no RainEffect task.");
                Assert.False(isRain || isActive, $"{level} flagged rain but has no task.");
            }
            else
            {
                Assert.Equal(e.IsRain, isRain);
                Assert.Equal(e.IsActive, isActive);
            }
        }
    }

    private static DeclaredTask? FindRainEffect(GameTask task)
    {
        if (task is DeclaredTask declared && string.Equals(declared.Type.Name, "RainEffect", StringComparison.Ordinal))
        {
            return declared;
        }

        foreach (GameTask child in task.Children)
        {
            if (FindRainEffect(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string Raw(DeclaredTask? task, string name)
    {
        if (task is null)
        {
            return "<none>";
        }

        return task.TryGetValue(name, out object? value) ? value?.ToString() ?? "<null>" : "<missing>";
    }

    // Local mirror of the flag decoder the Desktop port uses, so the golden test is independent of the
    // rendering code it pins against. Retail ships the flags as booleans, integers or quoted strings like
    // "TRUE"/"FALSE" — every spelling that reaches here must read as intended.
    private static bool ReadFlag(DeclaredTask? task, string name)
    {
        if (task is null || !task.TryGetValue(name, out object? raw))
        {
            return true;
        }

        return raw switch
        {
            bool flag => flag,
            int value => value != 0,
            string text => IsTrueText(text),
            _ => true,
        };
    }

    private static bool IsTrueText(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
        }

        return string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase) || trimmed == "1";
    }

    private static (TaskTree, GameTask) Load(string path)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        EngineTaskTypes.Register(types);

        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);

        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
        QvmInterpreter interpreter = new(program, natives);
        interpreter.Run();

        return (tree, tree.Root);
    }
}
