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
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        ContentRegistrationContext context = new(types, natives, new ScriptVarRegistry(natives), new ServiceCatalog(), NullLogger.Instance);
        foreach (IContentModule module in modules)
        {
            module.Register(context);
        }

        return TaskFieldCatalog.Record(types);
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
