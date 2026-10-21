using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Engine.Tasks;

/// <summary>
/// Every task type the engine knows about, by name.
/// </summary>
/// <remarks>
/// <para>
/// Models the original's 384-entry type table (<c>0xAF5EE0</c>) and its lookup <c>TaskType_FindByName</c>
/// (<c>0x401A70</c>). The 384 ceiling is not reproduced — nothing in retail data approaches it, and a
/// fatal overflow is a worse failure mode for mods than growing.
/// </para>
/// <para>
/// A type reaches the registry one of two ways: a class registers itself, or level data declares it
/// through <c>Task_DeclareParameters</c>. Both produce a parameter list, and when both happen for the
/// same name the declaration is checked against the class — which turns retail level data into a test
/// of our parameter attributes.
/// </para>
/// </remarks>
public sealed class TaskTypeRegistry
{
    private const string LogCategory = "Tasks";

    private readonly Dictionary<string, TaskType> _types = new(StringComparer.Ordinal);
    private readonly List<TaskType> _byId = [];
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="TaskTypeRegistry"/> class.</summary>
    /// <param name="logger">The logger for registration diagnostics; defaults to a sink that discards them.</param>
    public TaskTypeRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the number of registered types.</summary>
    public int Count => _types.Count;

    /// <summary>Gets every registered type, in registration order.</summary>
    public IReadOnlyList<TaskType> Types => _byId;

    /// <summary>Registers a task type implemented by a class.</summary>
    /// <typeparam name="T">The implementing class.</typeparam>
    /// <param name="name">The name scripts spawn it by.</param>
    /// <param name="parent">The parent type, or <see langword="null"/> for a root type.</param>
    /// <returns>The registered type.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">The name is already registered by another class.</exception>
    public TaskType Register<T>(string name, TaskType? parent = null)
        where T : GameTask, new() =>
        Register(name, static () => new T(), parent);

    /// <summary>Registers a task type with an explicit factory.</summary>
    /// <param name="name">The name scripts spawn it by.</param>
    /// <param name="factory">Creates an instance.</param>
    /// <param name="parent">The parent type, or <see langword="null"/> for a root type.</param>
    /// <returns>The registered type.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The name is already registered by another class.</exception>
    public TaskType Register(string name, Func<GameTask> factory, TaskType? parent = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (_types.TryGetValue(name, out TaskType? existing))
        {
            if (!existing.IsDeclarationOnly)
            {
                throw new InvalidOperationException($"The task type '{name}' is already registered.");
            }

            // Level data declared this type before its class existed. Replace the placeholder, keeping
            // the declared parameters so the check below still runs against them.
            _types.Remove(name);
            _byId.Remove(existing);
        }

        var type = new TaskType(_byId.Count, name, parent, factory, isDeclarationOnly: false);
        type.Parameters = ReadCompiledParameters(type);

        _types.Add(name, type);
        _byId.Add(type);
        return type;
    }

    /// <summary>Replaces the class that implements a registered task type.</summary>
    /// <typeparam name="T">The new implementing class.</typeparam>
    /// <param name="name">The type's name.</param>
    /// <returns>The type, now made as the new class.</returns>
    /// <exception cref="KeyNotFoundException">No class implements a type of that name.</exception>
    /// <exception cref="EngineFatalException">The level data declared the type, and the new class's parameters disagree.</exception>
    public TaskType Replace<T>(string name)
        where T : GameTask, new() =>
        Replace(name, static () => new T());

    /// <summary>Replaces the class that implements a registered task type, keeping its id, its parent and its subtypes.</summary>
    /// <param name="name">The type's name.</param>
    /// <param name="factory">Creates an instance of the new class.</param>
    /// <returns>The type, now made as the new class.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No class implements a type of that name.</exception>
    /// <exception cref="EngineFatalException">The level data declared the type, and the new class's parameters disagree.</exception>
    /// <remarks>
    /// What a mod calls to make a level's tasks of one type out of its own class. The new class reads the same parameters
    /// the old one did, since the level's data is written against them; what it does with them is its own.
    /// </remarks>
    public TaskType Replace(string name, Func<GameTask> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);
        if (!_types.TryGetValue(name, out TaskType? existing) || existing.IsDeclarationOnly)
        {
            throw new KeyNotFoundException($"No class implements the task type '{name}'.");
        }

        IReadOnlyList<TaskParameter> previous = existing.Parameters;
        existing.Reimplement(factory);
        existing.Parameters = ReadCompiledParameters(existing);
        if (existing.IsDeclared)
        {
            VerifyAgainstDeclaration(existing, previous);
        }

        _logger.Debug(LogCategory, $"Task type '{name}' is now made by another class.");
        return existing;
    }

    /// <summary>
    /// Replaces the class that implements a registered task type with one whose parameters are its own, an adapter setting it
    /// from the ones level data writes.
    /// </summary>
    /// <param name="name">The type's name.</param>
    /// <param name="factory">Creates an instance of the new class.</param>
    /// <param name="adapter">Sets the new class's members from the values the level gives the type.</param>
    /// <returns>The type, now made as the new class.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="adapter"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">No class implements a type of that name.</exception>
    /// <remarks>
    /// The type keeps the parameters it has, the replaced class's, until level data declares its own, which then stand: they
    /// are what the level writes, and the adapter reads them, so they are never checked against the new class.
    /// </remarks>
    public TaskType Replace(string name, Func<GameTask> factory, ITaskParameterAdapter adapter)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(adapter);
        if (!_types.TryGetValue(name, out TaskType? existing) || existing.IsDeclarationOnly)
        {
            throw new KeyNotFoundException($"No class implements the task type '{name}'.");
        }

        existing.Reimplement(factory, adapter);
        _logger.Debug(LogCategory, $"Task type '{name}' is now made by another class, its parameters adapted.");
        return existing;
    }

    /// <summary>
    /// Records the parameter list level data declares for a type, registering a placeholder when no class
    /// implements it yet.
    /// </summary>
    /// <param name="name">The type name.</param>
    /// <param name="parameters">The declared parameters, in wire order.</param>
    /// <returns>The type the declaration applies to.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">
    /// A class implements the type and its <c>[ScriptParam]</c> members disagree with the declaration.
    /// </exception>
    /// <remarks>
    /// This is the handler for the <c>Task_DeclareParameters</c> script command (<c>0x4F0740</c>), which
    /// retail <c>objects.qsc</c> emits for every type it uses.
    /// </remarks>
    public TaskType Declare(string name, IReadOnlyList<TaskParameter> parameters)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(parameters);

        if (_types.TryGetValue(name, out TaskType? existing))
        {
            if (existing.IsDeclarationOnly || existing.ParameterAdapter is not null)
            {
                existing.Parameters = [.. parameters];
                existing.IsDeclared = true;
                return existing;
            }

            VerifyAgainstDeclaration(existing, parameters);
            existing.IsDeclared = true;
            return existing;
        }

        var type = new TaskType(_byId.Count, name, parent: null, static () => new DeclaredTask(), isDeclarationOnly: true)
        {
            Parameters = [.. parameters],
            IsDeclared = true,
        };

        _types.Add(name, type);
        _byId.Add(type);
        _logger.Debug(LogCategory, $"Declared task type '{name}' with {parameters.Count} parameters (no class yet).");
        return type;
    }

    /// <summary>Looks up a type by name.</summary>
    /// <param name="name">The type name.</param>
    /// <param name="type">Receives the type when one is registered.</param>
    /// <returns><see langword="true"/> when the name resolves.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public bool TryGet(string name, [NotNullWhen(true)] out TaskType? type)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _types.TryGetValue(name, out type);
    }

    /// <summary>Looks up a type by name, failing fatally when it does not exist.</summary>
    /// <param name="name">The type name.</param>
    /// <returns>The type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">No such type is registered.</exception>
    /// <remarks>Missing is fatal in the original too: <c>"QTask type \"%s\" not defined"</c>.</remarks>
    public TaskType Get(string name)
    {
        if (!TryGet(name, out TaskType? type))
        {
            throw new EngineFatalException($"QTask type \"{name}\" not defined");
        }

        return type;
    }

    private static IReadOnlyList<TaskParameter> ReadCompiledParameters(TaskType type)
    {
        // Ask a throwaway instance what the source generator emitted for it. The alternative would be
        // reflecting over the attributes at run time, which the AOT targets rule out.
        GameTask probe = type.CreateInstance();
        return [.. probe.GetDeclaredScriptParameters()];
    }

    /// <summary>Checks a class against the parameter list a script declared for its type.</summary>
    /// <remarks>
    /// <para>
    /// <b>The declared list is the wire format and the class is whatever part of it has been
    /// transcribed.</b> A parameter list is per script, not global — <c>ParameterList_Derive</c>
    /// (<c>igi2 0x5686C0</c>) builds a type's list by extending its parent's, and each level's
    /// <c>Task_DeclareParameters</c> then states the list <em>that</em> script was compiled against. Two
    /// scripts in one game disagree routinely: I.G.I. 2's <c>location1/level1</c> declares ten
    /// parameters for <c>HumanPlayer</c> and every other level declares eleven, the extra one being
    /// <c>DisableSpottedIncrease</c> appended after that level was last built.
    /// </para>
    /// <para>
    /// So the two lists have to agree where they overlap and may differ in length either way. Trailing
    /// parameters the class does not carry are read and dropped; trailing members the data does not
    /// supply read as zero, which is what <see cref="ScriptArgumentReader"/> already returns past the
    /// end of a call's arguments. Neither can shift a field, because <c>Task_New</c> reads positionally
    /// and the shared part is a prefix.
    /// </para>
    /// <para>
    /// What is still fatal is disagreement <em>inside</em> the shared prefix about how many script
    /// arguments a parameter consumes, which does shift every field after it.
    /// </para>
    /// </remarks>
    private void VerifyAgainstDeclaration(TaskType type, IReadOnlyList<TaskParameter> declared)
    {
        IReadOnlyList<TaskParameter> compiled = type.Parameters;
        int shared = Math.Min(compiled.Count, declared.Count);

        if (compiled.Count != declared.Count)
        {
            IReadOnlyList<TaskParameter> longer = declared.Count > compiled.Count ? declared : compiled;
            string surplus = string.Join(
                ", ", longer.Skip(shared).Select(static parameter => $"'{parameter.Name}'"));

            _logger.Warning(
                LogCategory,
                declared.Count > compiled.Count
                    ? $"Task type '{type.Name}' declares {declared.Count} parameters in level data and its " +
                      $"class carries {compiled.Count}; {surplus} are read and dropped."
                    : $"Task type '{type.Name}' declares {declared.Count} parameters in level data and its " +
                      $"class carries {compiled.Count}; {surplus} read as zero.");
        }

        for (int i = 0; i < shared; i++)
        {
            ScriptParamType compiledType = ScriptParamTypeInfo.GetBaseType(compiled[i].Type);
            ScriptParamType declaredType = ScriptParamTypeInfo.GetBaseType(declared[i].Type);

            if (compiledType == declaredType)
            {
                if (!string.Equals(compiled[i].Name, declared[i].Name, StringComparison.Ordinal))
                {
                    _logger.Warning(
                        LogCategory,
                        $"Task type '{type.Name}' parameter {i} is named '{declared[i].Name}' in level data " +
                        $"but '{compiled[i].Name}' in its class.");
                }

                continue;
            }

            // What actually corrupts a parse is a change in how many script arguments the parameter
            // consumes, because Task_New reads them positionally out of one flat list and a different
            // count shifts every following field. A different type of the same arity does not.
            if (ScriptParamTypeInfo.GetArgumentCount(compiledType) != ScriptParamTypeInfo.GetArgumentCount(declaredType)
                || ScriptParamTypeInfo.IsVariableLength(compiledType)
                || ScriptParamTypeInfo.IsVariableLength(declaredType))
            {
                throw new EngineFatalException(
                    $"Task type '{type.Name}' parameter {i} ('{declared[i].Name}') is declared as " +
                    $"{ScriptParamTypeInfo.GetName(declared[i].Type)} in level data but as " +
                    $"{ScriptParamTypeInfo.GetName(compiled[i].Type)} in its class, and the two consume " +
                    "different numbers of script arguments.");
            }

            // Same arity, different type. This is real: the retail menu scripts declare SlideBar's
            // eighth parameter as "Set Num Options"/VarString in mainmenu.qvm and as
            // "Number of options"/Int32 in ingamemenu.qvm. A type's parameter list is per-script, not
            // global, so this is data variance rather than a bug to fail on.
            _logger.Warning(
                LogCategory,
                $"Task type '{type.Name}' parameter {i} is declared as " +
                $"{ScriptParamTypeInfo.GetName(declared[i].Type)} ('{declared[i].Name}') in level data " +
                $"but as {ScriptParamTypeInfo.GetName(compiled[i].Type)} ('{compiled[i].Name}') in its class.");
        }
    }
}
