using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Audio;

/// <summary>
/// The sound script's definitions: what every name game code says actually resolves to.
/// </summary>
/// <remarks>
/// <para>
/// Filled by running <c>sounds.qvm</c>, whose whole body is calls to four natives —
/// <c>DefineSound</c>, <c>DefineGroup</c>, <c>DefineGraph</c> and <c>DefineTriggerOnce</c>
/// (<c>0x4E6070</c> registers all four). Retail <c>common/sounds/sounds.qvm</c> is 361 sounds in 26
/// groups, plus 10 graphs and 7 triggers; each mission adds a handful more.
/// </para>
/// <para>
/// This is the layer that makes a name mean anything. <c>SoundBank</c> holds <c>AI_DEATH_01</c>;
/// game code asks for <c>ai_death_1</c>; only the definition joins them, and it carries the distances
/// and the loop flag with it.
/// </para>
/// </remarks>
public sealed class SoundDefinitionRegistry
{
    private const string LogCategory = "Audio";

    /// <summary>The shared definitions, loaded for every mission.</summary>
    public const string CommonScriptResource = "LOCAL:common/sounds/sounds.qvm";

    /// <summary>The mission's own definitions, loaded after the shared ones.</summary>
    public const string MissionScriptResource = "MISSION:sounds/sounds.qvm";

    /// <summary>The native that publishes a plain sound.</summary>
    public const string DefineSoundNative = "DefineSound";

    /// <summary>The native that opens a named container of definitions.</summary>
    public const string DefineGroupNative = "DefineGroup";

    /// <summary>The native that publishes a parameter-driven sound.</summary>
    public const string DefineGraphNative = "DefineGraph";

    /// <summary>The native that publishes a sound firing once on a condition.</summary>
    public const string DefineTriggerOnceNative = "DefineTriggerOnce";

    private readonly List<SoundDefinition> _roots = [];
    private readonly Dictionary<string, SoundDefinition> _byName = new(StringComparer.Ordinal);
    private readonly Stack<List<SoundDefinition>> _open = new();
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="SoundDefinitionRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public SoundDefinitionRegistry(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the top-level definitions, in the order the scripts declared them.</summary>
    public IReadOnlyList<SoundDefinition> Roots => _roots;

    /// <summary>Gets the number of definitions of every kind, at every depth.</summary>
    public int Count => _byName.Count;

    /// <summary>Registers the four <c>Define*</c> natives.</summary>
    /// <param name="natives">The script namespace the sound scripts resolve against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        natives.RegisterFunction(DefineSoundNative, call => Define(call, SoundDefinitionKind.Sound));
        natives.RegisterFunction(DefineGroupNative, DefineGroup);
        natives.RegisterFunction(DefineGraphNative, call => Define(call, SoundDefinitionKind.Graph));
        natives.RegisterFunction(DefineTriggerOnceNative, call => Define(call, SoundDefinitionKind.TriggerOnce));
    }

    /// <summary>Forgets every definition, for a mission teardown.</summary>
    public void Clear()
    {
        _roots.Clear();
        _byName.Clear();
        _open.Clear();
    }

    /// <summary>Reads and runs one sound script, adding its definitions to those already registered.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <param name="resource">The script to run.</param>
    /// <returns>The number of definitions this script added.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script is present but malformed or faults.</exception>
    /// <remarks>
    /// A missing script costs only its sounds, so it is a warning. Load the shared script before a
    /// mission's: the first definition of a name is the one that answers to it, which is the same rule
    /// the bank mounts under.
    /// </remarks>
    public int Load(ResourceRegistry resources, NativeRegistry natives, string resource)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentException.ThrowIfNullOrEmpty(resource);

        if (!resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Warning(LogCategory, $"Sound_ParseConfigFile(): Couldn't load script: {resource}");
            return 0;
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {resource}", ex);
        }

        int before = _byName.Count;
        _open.Clear();

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException($"{resource} faulted with {error}: {interpreter.ErrorMessage}");
        }

        return _byName.Count - before;
    }

    /// <summary>Loads the shared script and then the current mission's.</summary>
    /// <param name="resources">Where the scripts are read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <returns>The total number of definitions registered.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public int LoadForMission(ResourceRegistry resources, NativeRegistry natives)
    {
        Clear();
        Load(resources, natives, CommonScriptResource);
        Load(resources, natives, MissionScriptResource);

        _logger.Information(LogCategory, $"{Count} sound definition(s) registered.");
        return Count;
    }

    /// <summary>Looks a definition up by the name game code says.</summary>
    /// <param name="name">The definition name, such as <c>ai_death_1</c>.</param>
    /// <param name="definition">Receives the definition when one answers to the name.</param>
    /// <returns><see langword="true"/> when the name is defined.</returns>
    /// <remarks>
    /// The original broadcasts a lookup message down the whole definition tree and stops at the first
    /// match (<c>0x4E8AA0</c> for a sound, <c>0x4EB670</c> for a group, which recurses over its
    /// children). Comparison is <c>strcmp</c>, so it is case-sensitive. This keeps a flat index built
    /// in that same first-match order, which answers identically and does not walk the tree per shot.
    /// </remarks>
    public bool TryResolve(string name, out SoundDefinition? definition)
    {
        if (string.IsNullOrEmpty(name))
        {
            definition = null;
            return false;
        }

        return _byName.TryGetValue(name, out definition);
    }

    private QvmValue DefineGroup(QvmCallContext call)
    {
        // Argument 0 is the group's name; the rest are nested Define* calls. The original creates its
        // task, rebinds all four natives to it as parent (0x4B8930), *then* forces the remaining
        // arguments -- QVM arguments are unevaluated code offsets, so a native can change what its own
        // arguments will do before asking for them. Pushing a child list here is that same trick.
        List<SoundDefinition> children = [];

        // Added before its children are forced, because the original creates the group's task first
        // and the lookup then meets it before anything nested inside it. The list is filled in place
        // afterwards, so the definition keeps the group's own declaration order.
        Add(new SoundDefinition
        {
            Name = call.GetString(0),
            BankKey = string.Empty,
            Kind = SoundDefinitionKind.Group,
            Children = children,
        });

        _open.Push(children);
        try
        {
            for (int index = 1; index < call.ArgumentCount; index++)
            {
                call.GetInt32(index);
            }
        }
        finally
        {
            _open.Pop();
        }

        return QvmValue.FromInt32(1);
    }

    private QvmValue Define(QvmCallContext call, SoundDefinitionKind kind)
    {
        // Arguments 0 to 7 are identical across DefineSound (0x4E8C60), DefineGraph (0x4EAD20) and
        // DefineTriggerOnce (0x4E9EC0) -- same order, same types -- so they are read for all three.
        // What each adds after that differs and is not modelled: DefineGraph carries two eleven-point
        // curves, DefineTriggerOnce four more integers and two flags.
        SoundDefinition definition = new()
        {
            Name = call.GetString(0),
            BankKey = call.GetString(1),
            Kind = kind,
            Offset = new Vector3d(call.GetSingle(2), call.GetSingle(3), call.GetSingle(4)),
            Volume = call.GetSingle(5),
            MinimumDistanceMetres = call.GetSingle(6),
            MaximumDistanceMetres = call.GetSingle(7),

            // Only DefineSound has these in these slots, and only it has a loop flag at all.
            VolumeParameterIndex = kind == SoundDefinitionKind.Sound ? call.GetInt32(8) : -1,
            PitchParameterIndex = kind == SoundDefinitionKind.Sound ? call.GetInt32(9) : -1,
            BankKeyParameterIndex = kind == SoundDefinitionKind.Sound ? call.GetInt32(10) : -1,
            Loop = kind == SoundDefinitionKind.Sound && call.ArgumentCount > 12 && call.GetInt32(12) != 0,
        };

        Add(definition);
        return QvmValue.FromInt32(1);
    }

    private void Add(SoundDefinition definition)
    {
        if (_open.Count > 0)
        {
            _open.Peek().Add(definition);
        }
        else
        {
            _roots.Add(definition);
        }

        // First definition of a name wins, matching the original's stop-at-first-match search.
        _byName.TryAdd(definition.Name, definition);
    }
}
