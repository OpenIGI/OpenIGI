using System.Globalization;
using System.Text;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>
/// Reports what the retail levels' <c>Dirlight</c> and <c>GlobalLight</c> tasks actually contain.
/// </summary>
/// <remarks>
/// <para>
/// A measurement, not an assertion. The port has never read the directional-light tasks, <b>no
/// record of their parameter names exists</b>, and the one unit claim available — that a script
/// <c>Angle</c> is radians — exists in a doc comment rather than in anything derived from the binary.
/// Interpreting nine numbers per keyframe on that basis would be guessing.
/// </para>
/// <para>
/// So this prints the declared names alongside the values across every mission that ships one, and
/// the reading is written afterwards against what it shows.
/// </para>
/// </remarks>
public sealed class RetailDirlightProbe(ITestOutputHelper output)
{
    private static readonly string[] LightTypeNames = ["Dirlight", "DirlightKeyframe", "GlobalLight", "GlobalLightKeyframe"];

    [RequiresGameDataFact]
    public void Report()
    {
        StringBuilder report = new();

        for (int mission = 1; mission <= 14; mission++)
        {
            string? path = FindObjects(mission);
            if (path is null)
            {
                continue;
            }

            TaskTree tree = Load(path);
            report.AppendLine(CultureInfo.InvariantCulture, $"=== mission {mission} ===");

            foreach (GameTask task in Walk(tree.Root))
            {
                if (task is not DeclaredTask declared
                    || Array.IndexOf(LightTypeNames, declared.Type.Name) < 0)
                {
                    continue;
                }

                report.Append(CultureInfo.InvariantCulture, $"  {declared.Type.Name} label='{declared.Label}'");

                for (int i = 0; i < declared.Type.Parameters.Count; i++)
                {
                    TaskParameter parameter = declared.Type.Parameters[i];
                    report.Append(CultureInfo.InvariantCulture, $"  [{parameter.Name}:{parameter.Type}]=");
                    report.Append(Describe(declared.GetValue(i)));
                }

                report.AppendLine();
            }
        }

        output.WriteLine(report.ToString());
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        float f => f.ToString("0.#####", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        Vector3f v => FormattableString.Invariant($"({v.X:0.####},{v.Y:0.####},{v.Z:0.####})"),
        _ => value.ToString() ?? "?",
    };

    private static string? FindObjects(int mission)
    {
        // The mission table is published by scripts at run time, so the probe walks for the file
        // instead of resolving a descriptor it would have to boot the whole game to build.
        string root = GameData.Resolve("missions");
        if (!Directory.Exists(root))
        {
            return null;
        }

        foreach (string location in Directory.EnumerateDirectories(root))
        {
            string candidate = Path.Combine(location, $"level{mission}", "objects.qvm");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<GameTask> Walk(GameTask root)
    {
        foreach (GameTask child in root.Children)
        {
            yield return child;

            foreach (GameTask nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static TaskTree Load(string path)
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
        return tree;
    }
}
