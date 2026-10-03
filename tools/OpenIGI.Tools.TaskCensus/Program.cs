using System.Globalization;
using System.Text;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Level;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Tools;

return ToolHost.Run("TaskCensus", args, Run);

static int Run(ToolContext ctx)
{
    IReadOnlyList<string> args = ctx.Arguments;
    if (args.Count == 0 || args[0] is "-h" or "--help")
    {
        ctx.Out.WriteLine(UsageText);
        return ToolExitCode.Ok;
    }

    string root = args[0];
    string? outPath = null;
    for (int i = 1; i < args.Count - 1; i++)
    {
        if (args[i] is "--out" or "-o")
        {
            outPath = args[i + 1];
        }
    }

    if (!Directory.Exists(root))
    {
        return ctx.Usage(UsageText, $"'{root}' does not exist.");
    }

    string missionsRoot = Path.Combine(root, "missions");
    if (!Directory.Exists(missionsRoot))
    {
        return ctx.Usage(UsageText, $"'{missionsRoot}' does not exist; is this a game root?");
    }

    // name -> the declaration, plus which levels declared it. A type declared identically by every level is
    // the common case; a disagreement is the interesting one, so both are recorded.
    SortedDictionary<string, Declaration> census = new(StringComparer.Ordinal);
    List<string> failures = [];
    int levels = 0;

    foreach (string script in Directory
        .EnumerateFiles(missionsRoot, LevelLoader.ObjectsScriptName, SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
    {
        string levelDirectory = Path.GetDirectoryName(script)!;
        string label = Path.GetRelativePath(missionsRoot, levelDirectory).Replace('\\', '/');

        try
        {
            foreach (DeclaredType type in DeclaredTypesOf(root, levelDirectory))
            {
                string signature = Signature(type);

                if (!census.TryGetValue(type.Name, out Declaration? existing))
                {
                    census[type.Name] = new Declaration(type.Name, signature, [label]);
                    continue;
                }

                existing!.Levels.Add(label);
                if (!string.Equals(existing.Signature, signature, StringComparison.Ordinal))
                {
                    existing.Disagreements.Add($"{label}: {signature}");
                }
            }

            levels++;
        }
        catch (Exception ex) when (ex is EngineFatalException or InvalidOperationException or IOException)
        {
            failures.Add($"{label}: {ex.Message}");
        }
    }

    StringBuilder report = new();
    report.AppendLine(CultureInfo.InvariantCulture, $"# Task-type census — `{root}`");
    report.AppendLine();
    report.AppendLine(CultureInfo.InvariantCulture,
        $"{census.Count} task types declared across {levels} level(s), recovered by running each level's ");
    report.AppendLine("`objects.qvm` with an empty type registry. Parameters are in wire order: the order the");
    report.AppendLine("`Task_New` calls supply them.");
    report.AppendLine();

    foreach (Declaration declaration in census.Values)
    {
        report.AppendLine(CultureInfo.InvariantCulture, $"## {declaration.Name}");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"Declared by {declaration.Levels.Count} level(s).");
        report.AppendLine();
        report.AppendLine("```");
        report.AppendLine(declaration.Signature);
        report.AppendLine("```");

        if (declaration.Disagreements.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("**Levels disagree on this type:**");
            report.AppendLine();
            foreach (string disagreement in declaration.Disagreements.Distinct(StringComparer.Ordinal))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {disagreement}");
            }
        }

        report.AppendLine();
    }

    if (failures.Count > 0)
    {
        report.AppendLine("## Levels that did not load");
        report.AppendLine();
        foreach (string failure in failures)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- {failure}");
        }
    }

    if (outPath is null)
    {
        ctx.Out.Write(report.ToString());
    }
    else
    {
        File.WriteAllText(outPath, report.ToString());
        ctx.Info($"{census.Count} type(s) from {levels} level(s) -> {outPath}");
    }

    foreach (string failure in failures)
    {
        ctx.Warn(failure);
    }

    return ToolExitCode.Ok;
}

static IReadOnlyList<DeclaredType> DeclaredTypesOf(string gameRoot, string levelDirectory)
{
    // The census reads the declarations rather than applying them: it binds its own
    // Task_DeclareParameters and lets Task_New do nothing. That keeps it independent of which task
    // classes this port happens to implement, which matters in both directions -- a title whose classes
    // are not written yet still surveys, and a title whose classes disagree with its data reports the
    // disagreement instead of refusing to load. Running a level through the real LevelLoader is a
    // different question, and TaskCensusTests asks it.
    List<DeclaredType> declared = [];
    var natives = new NativeRegistry();

    natives.RegisterFunction("Task_DeclareParameters", call =>
    {
        if (call.ArgumentCount < 1)
        {
            return QvmValue.Zero;
        }

        string typeName = call.GetString(0);
        List<string> parameters = [];

        // Name/type string pairs follow the type name, exactly as TaskScriptCommands reads them.
        for (int i = 1; i + 1 < call.ArgumentCount; i += 2)
        {
            parameters.Add($"{call.GetString(i)}: {call.GetString(i + 1)}");
        }

        declared.Add(new DeclaredType(typeName, parameters));
        return QvmValue.Zero;
    });

    // Task_New builds the tree; the census only wants the declarations that precede it. Bound to a
    // no-op rather than left unbound so the script runs to completion and declares everything, including
    // any type only declared late.
    natives.RegisterFunction("Task_New", static _ => QvmValue.Zero);

    string script = Path.Combine(levelDirectory, LevelLoader.ObjectsScriptName);
    QvmProgram program = QvmProgram.Read(File.ReadAllBytes(script));

    var interpreter = new QvmInterpreter(program, natives);
    QvmError error = interpreter.Run();
    if (error != QvmError.None)
    {
        throw new InvalidOperationException($"{error}: {interpreter.ErrorMessage}");
    }

    return declared;
}

static string Signature(DeclaredType type)
{
    if (type.Parameters.Count == 0)
    {
        return $"{type.Name}()";
    }

    IEnumerable<string> lines = type.Parameters.Select(static p => $"    {p}");
    return $"{type.Name}(\n{string.Join(",\n", lines)})";
}

/// <summary>One task type as a level script declared it.</summary>
/// <param name="Name">The type name scripts spawn it by.</param>
/// <param name="Parameters">Its parameters, in the order Task_New supplies them.</param>
internal sealed record DeclaredType(string Name, IReadOnlyList<string> Parameters);

/// <summary>The tool's usage text.</summary>
internal static partial class Program
{
    private const string UsageText =
        """
        OpenIGI TaskCensus - report the task types a game's level scripts declare.

        usage:
          TaskCensus <game-root> [--out <file.md>]

        Runs every objects.qvm below <game-root>/missions with an empty type registry and
        reports what each level declared: the type name, its parameter count and its
        parameters in wire order. See README.md.
        """;
}

internal sealed record Declaration(string Name, string Signature, List<string> Levels)
{
    public List<string> Disagreements { get; } = [];
}
