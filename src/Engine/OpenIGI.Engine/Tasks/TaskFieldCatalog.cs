using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Engine.Tasks;

/// <summary>What exporting every registered task type binds: each type's fields, the globals, and the types that could not be asked.</summary>
/// <param name="Fields">Each type's exported fields, by type name, in the order it binds them.</param>
/// <param name="Globals">The global variables the types bind, by name.</param>
/// <param name="Failures">The types whose export threw without a level around them, with the reason.</param>
public sealed record TaskFieldRecording(
    IReadOnlyDictionary<string, IReadOnlyList<NativeParameter>> Fields,
    IReadOnlyDictionary<string, NativeParameter> Globals,
    IReadOnlyDictionary<string, string> Failures);

/// <summary>Learns the fields every task type exports by asking it: a scratch instance of each exports into a scratch registry.</summary>
/// <remarks>
/// Task types bind their variables in code (<see cref="ScriptVarRegistry.BindInt"/> and the rest, in each type's export
/// handler), so there is no table to read. Recording is the one source that cannot drift from what the game binds: a field a
/// type starts exporting appears here without anyone writing it down twice.
/// </remarks>
public static class TaskFieldCatalog
{
    private const int ScratchId = 1;

    /// <summary>Exports a scratch instance of every type and records what each binds.</summary>
    /// <param name="types">The registered task types.</param>
    /// <returns>The fields, globals and failures.</returns>
    public static TaskFieldRecording Record(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        Dictionary<string, IReadOnlyList<NativeParameter>> fields = new(StringComparer.Ordinal);
        Dictionary<string, NativeParameter> globals = new(StringComparer.Ordinal);
        Dictionary<string, string> failures = new(StringComparer.Ordinal);

        foreach (TaskType type in types.Types)
        {
            NativeRegistry symbols = new();
            ScriptVarRegistry variables = new(symbols);
            GameTask task;
            try
            {
                task = type.CreateInstance();
                task.Id = ScratchId;
                variables.Export(task);
            }
            catch (Exception error) when (error is NullReferenceException or InvalidOperationException or ArgumentException)
            {
                failures[type.Name] = error.Message;
                continue;
            }

            string prefix = task.Name + ".";
            List<NativeParameter> own = [];
            foreach (string name in variables.BoundNames)
            {
                ScriptType kind = KindOf(symbols, name);
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    own.Add(new NativeParameter(name[prefix.Length..], kind));
                }
                else
                {
                    globals[name] = new NativeParameter(name, kind);
                }
            }

            if (own.Count > 0)
            {
                fields[type.Name] = own;
            }
        }

        return new TaskFieldRecording(fields, globals, failures);
    }

    // An integer one byte wide is a flag the game stores as a byte: a truth value.
    private static ScriptType KindOf(NativeRegistry symbols, string name) =>
        symbols.TryResolve(name, out QvmSymbol? symbol) && symbol.Variable is { } variable
            ? variable switch
            {
                QvmIntegerVariable { Size: 1 } => ScriptType.Bool,
                _ when variable.ValueType == QvmValueKind.Integer => ScriptType.Int,
                _ when variable.ValueType == QvmValueKind.Float => ScriptType.Real,
                _ when variable.ValueType == QvmValueKind.String => ScriptType.Text,
                _ => ScriptType.Any,
            }
            : ScriptType.Any;
}
