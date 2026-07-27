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
/// fatal overflow is a worse failure mode for mods than growing. See the re-note, §11.
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
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="TaskTypeRegistry"/> class.</summary>
    /// <param name="logger">The logger for registration diagnostics; defaults to a sink that discards them.</param>
    public TaskTypeRegistry(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

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
            if (existing.IsDeclarationOnly)
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

    private void VerifyAgainstDeclaration(TaskType type, IReadOnlyList<TaskParameter> declared)
    {
        IReadOnlyList<TaskParameter> compiled = type.Parameters;

        if (compiled.Count != declared.Count)
        {
            throw new EngineFatalException(
                $"Task type '{type.Name}' declares {declared.Count} parameters in level data but its class " +
                $"has {compiled.Count} [ScriptParam] members.");
        }

        for (int i = 0; i < compiled.Count; i++)
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
