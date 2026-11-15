using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Scripting.Vocabulary;

/// <summary>A family of named integer constants, such as every <c>AIEVENT_*</c>.</summary>
/// <param name="Name">The family's name.</param>
/// <param name="Values">Each constant's name and value, in the order they are offered.</param>
public sealed record ConstantFamily(string Name, IReadOnlyList<KeyValuePair<string, int>> Values)
{
    /// <summary>Gets what each constant means, by its name, for the ones worth a word; an editor shows it beside the name.</summary>
    public IReadOnlyDictionary<string, string> Notes { get; init; } = NoNotes;

    private static IReadOnlyDictionary<string, string> NoNotes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Everything a title's scripts can name: natives with their signatures, and constants grouped into families.</summary>
/// <remarks>
/// One vocabulary per title. Each title's assemblies declare their natives beside the implementations and the host merges
/// them, so the editor gets the whole vocabulary without referencing a title. A test per title builds the runtime namespace
/// and fails when a native is registered without a signature here.
/// </remarks>
public sealed class ScriptVocabulary
{
    private readonly Dictionary<string, NativeSignature> _natives = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ConstantFamily> _families = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Family, int Value)> _constants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<NativeParameter>> _taskFields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativeParameter> _globals = new(StringComparer.Ordinal);
    private readonly HashSet<string> _openIgiOnly = new(StringComparer.Ordinal);
    private readonly List<TaskListLimit> _taskLists = [];

    /// <summary>Gets the fixed-size lists the game keeps tasks in, each with the types that fill it.</summary>
    public IReadOnlyList<TaskListLimit> TaskLists => _taskLists;

    /// <summary>Adds fixed-size task lists.</summary>
    /// <param name="lists">The lists.</param>
    /// <returns>This vocabulary.</returns>
    public ScriptVocabulary Add(IEnumerable<TaskListLimit> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        _taskLists.AddRange(lists);
        return this;
    }

    /// <summary>Gets the natives, by name.</summary>
    public IReadOnlyDictionary<string, NativeSignature> Natives => _natives;

    /// <summary>Gets the constant families, by name.</summary>
    public IReadOnlyDictionary<string, ConstantFamily> Families => _families;

    /// <summary>Gets the fields each task type exports to level expressions (<c>Door_12.isOpen</c>), by task type.</summary>
    /// <remarks>Each field is a <see cref="NativeParameter"/>: its name, its type, and what it means.</remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<NativeParameter>> TaskFields => _taskFields;

    /// <summary>Gets the global variables level expressions can read, such as I.G.I. 2's <c>NetMission_eCurrentObjective</c>.</summary>
    public IReadOnlyDictionary<string, NativeParameter> Globals => _globals;

    private readonly Dictionary<(string TaskType, string Pin), TaskAction> _actions = [];
    private readonly Dictionary<string, string> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Type, string Parameter), string> _help = [];

    /// <summary>Gets what making a task's condition hold does, in plain words, for every condition the title has words for.</summary>
    public IReadOnlyCollection<TaskAction> Actions => _actions.Values;

    /// <summary>Adds what making tasks' conditions hold does.</summary>
    /// <param name="actions">The actions; a later one for the same type and parameter replaces an earlier.</param>
    /// <returns>This vocabulary.</returns>
    public ScriptVocabulary Add(IEnumerable<TaskAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        foreach (TaskAction action in actions)
        {
            _actions[(action.TaskType, action.Parameter)] = action;
        }

        return this;
    }

    /// <summary>What making a task's condition hold does, in plain words.</summary>
    /// <param name="taskType">The task type.</param>
    /// <param name="parameter">The condition parameter.</param>
    /// <returns>The words (<c>Show the message</c>), or <see langword="null"/> when the title has none for it.</returns>
    public string? ActionOf(string taskType, string parameter) =>
        _actions.TryGetValue((taskType, parameter), out TaskAction? action) ? action.Verb : null;

    /// <summary>Adds what task types and their parameters are, in a line each, from the game's own task classes.</summary>
    /// <param name="rows">The type, the parameter as the levels declare it (empty for the type itself), and the text; a later
    /// row for the same type and parameter replaces an earlier, so a title's own class speaks over a shared one.</param>
    /// <returns>This vocabulary.</returns>
    public ScriptVocabulary AddHelp(IEnumerable<(string Type, string Parameter, string Text)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach ((string type, string parameter, string text) in rows)
        {
            _help[(type, parameter)] = text;
        }

        return this;
    }

    /// <summary>What a task type, or one of its parameters, is.</summary>
    /// <param name="taskType">The task type.</param>
    /// <param name="parameter">The parameter as the levels declare it, or empty for the type itself.</param>
    /// <returns>The line, or <see langword="null"/> when the game's code says nothing about it.</returns>
    public string? HelpOf(string taskType, string parameter = "") => _help.GetValueOrDefault((taskType, parameter));

    /// <summary>What making a task's condition hold does, with how it ends.</summary>
    /// <param name="taskType">The task type.</param>
    /// <param name="parameter">The condition parameter.</param>
    /// <returns>The action, or <see langword="null"/> when the title has none for it.</returns>
    public TaskAction? Action(string taskType, string parameter) => _actions.GetValueOrDefault((taskType, parameter));

    /// <summary>Adds what exported fields say about their task, in plain words, by field name.</summary>
    /// <param name="states">Each field's name and its words (<c>isHacked</c>, <c>is hacked</c>).</param>
    /// <returns>This vocabulary.</returns>
    public ScriptVocabulary AddStates(IEnumerable<KeyValuePair<string, string>> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        foreach ((string field, string words) in states)
        {
            _states[field] = words;
        }

        return this;
    }

    /// <summary>What an exported field says about its task, in plain words.</summary>
    /// <param name="field">The field's name.</param>
    /// <returns>The words (<c>is hacked</c>), or <see langword="null"/> when the title has none for it.</returns>
    public string? StateOf(string field) => _states.GetValueOrDefault(field);

    /// <summary>Adds the fields a task type exports.</summary>
    /// <param name="taskType">The task type's name.</param>
    /// <param name="fields">Its fields.</param>
    /// <returns>This vocabulary.</returns>
    public ScriptVocabulary AddFields(string taskType, IEnumerable<NativeParameter> fields)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskType);
        ArgumentNullException.ThrowIfNull(fields);
        _taskFields[taskType] = [.. fields];
        return this;
    }

    /// <summary>Adds global variables.</summary>
    /// <param name="globals">The variables.</param>
    /// <returns>This vocabulary.</returns>
    public ScriptVocabulary AddGlobals(IEnumerable<NativeParameter> globals)
    {
        ArgumentNullException.ThrowIfNull(globals);
        foreach (NativeParameter global in globals)
        {
            AddGlobal(global);
        }

        return this;
    }

    /// <summary>Adds a global variable.</summary>
    /// <param name="global">The variable: its name and type.</param>
    /// <returns>This vocabulary.</returns>
    public ScriptVocabulary AddGlobal(NativeParameter global)
    {
        ArgumentNullException.ThrowIfNull(global);
        _globals[global.Name] = global;
        return this;
    }

    /// <summary>The type of a field a level expression reads.</summary>
    /// <param name="taskType">The task type.</param>
    /// <param name="field">The field's name.</param>
    /// <param name="type">Receives its type.</param>
    /// <returns><see langword="true"/> when the type exports the field.</returns>
    public bool TryGetField(string taskType, string field, out ScriptType type)
    {
        if (_taskFields.TryGetValue(taskType, out IReadOnlyList<NativeParameter>? fields)
            && fields.FirstOrDefault(f => string.Equals(f.Name, field, StringComparison.Ordinal)) is { } found)
        {
            type = found.Type;
            return true;
        }

        type = ScriptType.Any;
        return false;
    }

    /// <summary>Adds natives.</summary>
    /// <param name="signatures">The signatures.</param>
    /// <returns>This vocabulary.</returns>
    /// <exception cref="InvalidOperationException">A name is added twice with different shapes.</exception>
    /// <remarks>
    /// A name both titles' shared code declares arrives twice with the same shape and is kept once; a name declared twice
    /// with different pins is a mistake in one of the tables.
    /// </remarks>
    public ScriptVocabulary Add(IEnumerable<NativeSignature> signatures)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        foreach (NativeSignature signature in signatures)
        {
            if (_natives.TryGetValue(signature.Name, out NativeSignature? existing))
            {
                if (!SameShape(existing, signature))
                {
                    throw new InvalidOperationException($"'{signature.Name}' is declared twice with different signatures.");
                }

                continue;
            }

            _natives.Add(signature.Name, signature);
        }

        return this;
    }

    /// <summary>Adds a constant family.</summary>
    /// <param name="family">The family.</param>
    /// <returns>This vocabulary.</returns>
    /// <exception cref="InvalidOperationException">The family or one of its constants is already present.</exception>
    public ScriptVocabulary Add(ConstantFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (!_families.TryAdd(family.Name, family))
        {
            throw new InvalidOperationException($"The constant family '{family.Name}' is added twice.");
        }

        foreach ((string name, int value) in family.Values)
        {
            if (!_constants.TryAdd(name, (family.Name, value)))
            {
                throw new InvalidOperationException($"The constant '{name}' is in two families.");
            }
        }

        return this;
    }

    /// <summary>Adds constant families.</summary>
    /// <param name="families">The families.</param>
    /// <returns>This vocabulary.</returns>
    /// <exception cref="InvalidOperationException">A family or a constant is already present.</exception>
    public ScriptVocabulary Add(IEnumerable<ConstantFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        foreach (ConstantFamily family in families)
        {
            Add(family);
        }

        return this;
    }

    /// <summary>Looks a native up.</summary>
    /// <param name="name">The name a script calls it by.</param>
    /// <param name="signature">Receives the signature.</param>
    /// <returns><see langword="true"/> when the vocabulary has it.</returns>
    public bool TryGetNative(string name, [NotNullWhen(true)] out NativeSignature? signature) =>
        _natives.TryGetValue(name, out signature);

    /// <summary>Looks a constant up.</summary>
    /// <param name="name">The constant's name.</param>
    /// <param name="family">Receives its family's name.</param>
    /// <param name="value">Receives its value.</param>
    /// <returns><see langword="true"/> when the vocabulary has it.</returns>
    public bool TryGetConstant(string name, [NotNullWhen(true)] out string? family, out int value)
    {
        if (_constants.TryGetValue(name, out (string Family, int Value) found))
        {
            family = found.Family;
            value = found.Value;
            return true;
        }

        family = null;
        value = 0;
        return false;
    }

    /// <summary>Records that only OpenIGI has a task type or a constant: an engine or mod addition the original lacks.</summary>
    /// <param name="name">The task type's or the constant's name.</param>
    /// <returns>This vocabulary.</returns>
    public ScriptVocabulary MarkOpenIgiOnly(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _openIgiOnly.Add(name);
        return this;
    }

    /// <summary>Which runtime has a task type, a constant or a native: the original game, or only OpenIGI.</summary>
    /// <param name="name">The name scripts and task trees use.</param>
    /// <returns><see cref="ScriptRuntime.OpenIgi"/> for an addition the original lacks; otherwise the original's.</returns>
    /// <remarks>
    /// Everything a title declares itself is the original's: its natives and task types are checked against the retail
    /// executables. What a mod adds is marked as it is added.
    /// </remarks>
    public ScriptRuntime RuntimeOf(string name) =>
        _openIgiOnly.Contains(name) || (_natives.TryGetValue(name, out NativeSignature? native) && native.Runtime == ScriptRuntime.OpenIgi)
            ? ScriptRuntime.OpenIgi
            : ScriptRuntime.Retail;

    /// <summary>The natives callable from a kind of script.</summary>
    /// <param name="context">The kind of script.</param>
    /// <returns>The natives, ordered by name.</returns>
    public ReadOnlyCollection<NativeSignature> CallableFrom(ScriptContexts context) =>
        new([.. _natives.Values.Where(n => (n.Contexts & context) != 0).OrderBy(static n => n.Name, StringComparer.Ordinal)]);

    private static bool SameShape(NativeSignature a, NativeSignature b) =>
        a.Returns == b.Returns && a.Effect == b.Effect && a.IsVariadic == b.IsVariadic && a.OptionalCount == b.OptionalCount
        && a.Parameters.Select(static p => p.Type).SequenceEqual(b.Parameters.Select(static p => p.Type));
}
