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
public sealed class SoundDefinitionRegistry : ISoundDefinitionLookup
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

    private readonly SoundDefinitionTable _table = new();
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="SoundDefinitionRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public SoundDefinitionRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the top-level definitions, in the order the scripts declared them.</summary>
    public IReadOnlyList<SoundDefinition> Roots => _table.Roots;

    /// <summary>Gets the number of definitions of every kind, at every depth.</summary>
    public int Count => _table.Count;

    /// <summary>Registers the four <c>Define*</c> natives.</summary>
    /// <param name="natives">The script namespace the sound scripts resolve against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        natives.RegisterFunction(DefineSoundNative, call => Define(call, SoundDefinitionKind.Sound));
        natives.RegisterFunction(DefineGroupNative, call =>
        {
            // The original rebinds all four natives to the group as parent (0x4B8930) before forcing its arguments.
            _table.DefineGroup(call);
            return QvmValue.FromInt32(1);
        });
        natives.RegisterFunction(DefineGraphNative, call => Define(call, SoundDefinitionKind.Graph));
        natives.RegisterFunction(DefineTriggerOnceNative, call => Define(call, SoundDefinitionKind.TriggerOnce));
    }

    /// <summary>Forgets every definition, for a mission teardown.</summary>
    public void Clear() => _table.Clear();

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
        int before = _table.Count;
        if (!_table.Run(resources, natives, resource))
        {
            _logger.Warning(LogCategory, $"Sound_ParseConfigFile(): Couldn't load script: {resource}");
            return 0;
        }

        return _table.Count - before;
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

    /// <inheritdoc/>
    /// <remarks>
    /// The original broadcasts a lookup message down the whole definition tree and stops at the first
    /// match (<c>0x4E8AA0</c> for a sound, <c>0x4EB670</c> for a group, which recurses over its
    /// children). Comparison is <c>strcmp</c>, so it is case-sensitive. This keeps a flat index built
    /// in that same first-match order, which answers identically and does not walk the tree per shot.
    /// </remarks>
    public bool TryResolve(string name, out SoundDefinition? definition) => _table.TryResolve(name, out definition);

    private QvmValue Define(QvmCallContext call, SoundDefinitionKind kind)
    {
        // Arguments 0 to 7 are identical across DefineSound (0x4E8C60), DefineGraph (0x4EAD20) and
        // DefineTriggerOnce (0x4E9EC0) -- same order, same types -- so they are read for all three. What
        // follows differs by kind (see SoundDefinition).
        bool graph = kind == SoundDefinitionKind.Graph;
        bool trigger = kind == SoundDefinitionKind.TriggerOnce;
        SoundDefinition definition = new()
        {
            Name = call.GetString(0),
            BankKey = call.GetString(1),
            Kind = kind,
            Offset = new Vector3d(call.GetSingle(2), call.GetSingle(3), call.GetSingle(4)),
            Volume = call.GetSingle(5),
            MinimumDistanceMetres = call.GetSingle(6),
            MaximumDistanceMetres = call.GetSingle(7),

            // DefineSound: 8 gain, 9 pitch, 10 bank key, 12 loop. DefineGraph: 8 gain, 9 pitch, 10 bank key,
            // 11 pitch added. DefineTriggerOnce: 8 duration, 9 trigger, 10 gain, 11 pitch, 12 bank key, 13 loop,
            // 14 invert.
            VolumeParameterIndex = Index(call, trigger ? 10 : 8),
            PitchParameterIndex = Index(call, trigger ? 11 : 9),
            BankKeyParameterIndex = Index(call, trigger ? 12 : 10),
            PitchAddParameterIndex = graph ? Index(call, 11) : -1,
            TriggerParameterIndex = trigger ? Index(call, 9) : -1,
            DurationSeconds = trigger && call.ArgumentCount > 8 ? call.GetSingle(8) : -1f,
            Invert = trigger && call.ArgumentCount > 14 && call.GetInt32(14) != 0,
            Loop = kind switch
            {
                SoundDefinitionKind.Sound => call.ArgumentCount > 12 && call.GetInt32(12) != 0,
                SoundDefinitionKind.TriggerOnce => call.ArgumentCount > 13 && call.GetInt32(13) != 0,

                // 0x4EA560 issues a graph's voice with the loop flag (0x10) always set.
                SoundDefinitionKind.Graph => true,
                _ => false,
            },
            GainCurve = graph ? Curve(call, 12) : [],
            PitchCurve = graph ? Curve(call, 23) : [],
        };

        _table.Add(definition);
        return QvmValue.FromInt32(1);
    }

    private static int Index(QvmCallContext call, int argument) =>
        call.ArgumentCount > argument ? call.GetInt32(argument) : -1;

    private static float[] Curve(QvmCallContext call, int first)
    {
        float[] samples = new float[SoundDefinition.CurveSamples];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = call.ArgumentCount > first + i ? call.GetSingle(first + i) : 0f;
        }

        return samples;
    }
}
