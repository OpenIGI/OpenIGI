using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Engine.Titles;

/// <summary>Fills a title's script vocabulary with what its content modules register: the fields every task type exports.</summary>
public static class TitleScriptVocabulary
{
    /// <summary>Registers a title's content modules against scratch registries, as a level load would, and records each task type's fields.</summary>
    /// <param name="modules">The title's content modules.</param>
    /// <returns>What exporting every registered type binds.</returns>
    /// <remarks>Nothing is loaded: modules register types and natives only, so this is cheap and needs no installation.</remarks>
    public static TaskFieldRecording RecordTaskFields(IReadOnlyList<IContentModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        return TaskFieldCatalog.Record(Register(modules).Types);
    }

    /// <summary>
    /// Adds what modules beyond a title's own register (a mod's task types and constants) to its vocabulary, each marked as
    /// only OpenIGI having it, so a mission made for the original game is told when it uses one.
    /// </summary>
    /// <param name="vocabulary">The title's vocabulary.</param>
    /// <param name="shipped">The title's own content modules.</param>
    /// <param name="modules">Every module the game runs: the title's, then the mods'.</param>
    /// <returns>The vocabulary.</returns>
    /// <remarks>
    /// A task type a mod replaces keeps its name, which the original has, and stays the original's. A mod's constants go
    /// in a family of their own, <see cref="ModConstantFamily"/>.
    /// </remarks>
    public static ScriptVocabulary AddAdditions(this ScriptVocabulary vocabulary, IReadOnlyList<IContentModule> shipped, IReadOnlyList<IContentModule> modules)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(modules);
        (NativeRegistry baseNatives, TaskTypeRegistry baseTypes) = Register(shipped);
        (NativeRegistry natives, TaskTypeRegistry types) = Register(modules);
        TaskFieldRecording fields = TaskFieldCatalog.Record(types);
        foreach (TaskType type in types.Types)
        {
            if (baseTypes.TryGet(type.Name, out _))
            {
                continue;
            }

            vocabulary.AddFields(type.Name, fields.Fields.TryGetValue(type.Name, out IReadOnlyList<NativeParameter>? exported) ? exported : []);
            vocabulary.MarkOpenIgiOnly(type.Name);
        }

        List<KeyValuePair<string, int>> constants = [];
        foreach (string name in natives.Names)
        {
            if (!baseNatives.Contains(name) && natives.TryResolve(name, out QvmSymbol? symbol)
                && symbol is { Kind: QvmSymbolKind.Constant, ValueType: QvmValueKind.Integer } && !vocabulary.TryGetConstant(name, out _, out _))
            {
                constants.Add(new(name, symbol.Read().Integer));
                vocabulary.MarkOpenIgiOnly(name);
            }
        }

        if (constants.Count > 0)
        {
            vocabulary.Add(new ConstantFamily(ModConstantFamily, [.. constants.OrderBy(static c => c.Key, StringComparer.Ordinal)]));
        }

        return vocabulary;
    }

    /// <summary>The constant family a mod's constants are listed in.</summary>
    public const string ModConstantFamily = "Mods";

    // Registers modules against scratch registries, as a level load would.
    private static (NativeRegistry Natives, TaskTypeRegistry Types) Register(IReadOnlyList<IContentModule> modules)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        ContentRegistrationContext context = new(types, natives, new ScriptVarRegistry(natives), new ServiceCatalog(), NullLogger.Instance);
        foreach (IContentModule module in modules)
        {
            module.Register(context);
        }

        return (natives, types);
    }

    /// <summary>Adds a recording's fields and globals to a vocabulary.</summary>
    /// <param name="vocabulary">The vocabulary.</param>
    /// <param name="recording">The recording.</param>
    /// <returns>The vocabulary.</returns>
    public static ScriptVocabulary Add(this ScriptVocabulary vocabulary, TaskFieldRecording recording)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(recording);
        foreach ((string type, IReadOnlyList<NativeParameter> fields) in recording.Fields)
        {
            vocabulary.AddFields(type, fields);
        }

        foreach (NativeParameter global in recording.Globals.Values)
        {
            vocabulary.AddGlobal(global);
        }

        return vocabulary;
    }
}
