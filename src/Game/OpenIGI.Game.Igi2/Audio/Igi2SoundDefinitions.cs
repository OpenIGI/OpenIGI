using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Audio;

/// <summary>
/// The level's sound definitions: what every name game code says resolves to, <c>SoundSys_ptSoundDefQTaskList</c> as
/// <c>Game_CreateHandler</c> (<c>igi2 0x4070E0</c>) fills it.
/// </summary>
/// <remarks>
/// <para>
/// Three scripts, one after the other, each the <c>sounds</c> script of a directory (<c>SoundSys_NewSoundDefParentQTask</c>,
/// <c>0x42EEB0</c>): the game's own under <c>LOCAL:common/sounds/</c>, the location's under its common directory, then the
/// mission's under <c>MISSION:sounds/</c>. Every one is made of calls to four natives, <c>DefineGroup</c>,
/// <c>DefineSound</c>, <c>DefineGraph</c> and <c>DefineTriggerOnce</c>, and of nothing else but <c>TRUE</c> and
/// <c>FALSE</c>, so they are run in a namespace of those six.
/// </para>
/// <para>
/// The dedicated server's natives parse nothing (<c>SoundDefSound_ScriptParseCB</c>, <c>0x57C540</c>, returns at once) and
/// its sound is never switched on, so the arguments are read as the editor reads them (<c>igi2ed 0x430B70</c>,
/// <c>0x4349C0</c>, <c>0x434B20</c>, <c>0x435A10</c>). They are I.G.I.'s arguments in I.G.I.'s order, with one rule of
/// I.G.I. 2's own: a sound whose nearest distance is nought hears at full volume to a fiftieth of its farthest.
/// </para>
/// <para>
/// A name is found as <c>SoundSys_NewSoundFn</c> (<c>0x42F6F0</c>) finds it: each script's definitions in turn, a group
/// answering to its own name before any inside it (<c>SoundDefGroup_FindSoundDefHandler</c>, <c>igi2ed 0x434580</c>),
/// <c>strcmp</c> and the first match wins.
/// </para>
/// </remarks>
public sealed class Igi2SoundDefinitions : ISoundDefinitionLookup
{
    /// <summary>The game's own definitions' directory.</summary>
    public const string CommonDirectory = "LOCAL:common/sounds";

    /// <summary>The mission's own definitions' directory.</summary>
    public const string MissionDirectory = "MISSION:sounds";

    /// <summary>The script each directory holds.</summary>
    public const string ScriptName = "sounds.qvm";

    /// <summary>The native that opens a named container of definitions.</summary>
    public const string DefineGroupNative = "DefineGroup";

    /// <summary>The native that defines a plain sound.</summary>
    public const string DefineSoundNative = "DefineSound";

    /// <summary>The native that defines a sound driven by its parent's parameters.</summary>
    public const string DefineGraphNative = "DefineGraph";

    /// <summary>The native that defines a sound fired once by a parameter.</summary>
    public const string DefineTriggerOnceNative = "DefineTriggerOnce";

    /// <summary>The language a directory's speech falls back to, <c>Game_GetLocalizedSoundDir</c>'s <c>"%s/ENGLISH"</c>.</summary>
    public const string DefaultLanguage = "ENGLISH";

    /// <summary>The fraction of its farthest distance a sound with no nearest distance is heard in full to, <c>0x3CA3D70A</c>.</summary>
    public const float NearestOfFarthest = 0.02f;

    private const string LogCategory = "Sound";

    private readonly IOpenIgiLogger _logger;
    private readonly List<SoundDefinition> _roots = [];
    private readonly Dictionary<string, SoundDefinition> _byName = new(StringComparer.Ordinal);
    private readonly Stack<List<SoundDefinition>> _open = new();

    /// <summary>Initializes a new instance of the <see cref="Igi2SoundDefinitions"/> class.</summary>
    /// <param name="logger">Where load diagnostics go; defaults to nowhere.</param>
    public Igi2SoundDefinitions(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the top-level definitions, in the order the scripts declared them.</summary>
    public IReadOnlyList<SoundDefinition> Roots => _roots;

    /// <summary>Gets the number of names defined, at every depth.</summary>
    public int Count => _byName.Count;

    /// <summary>The directories a mission's definitions are read from, in the order they are.</summary>
    /// <param name="mission">The mission.</param>
    /// <returns>The game's, the location's (<c>"LOCAL:%s/sounds"</c> of its common directory, <c>Game + 0x550</c>), the mission's.</returns>
    public static IReadOnlyList<string> Directories(MissionDescriptor mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        return [CommonDirectory, $"LOCAL:{mission.CommonPackDirectory}/sounds", MissionDirectory];
    }

    /// <summary>The directories a mission's sounds are banked from, in the order <c>Game_CreateHandler</c> scans them for archives.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="language">The profile's language, whose folder in each directory holds its speech.</param>
    /// <returns>Each of <see cref="Directories"/>, followed by its language folder.</returns>
    /// <remarks>
    /// <c>Game_GetLocalizedSoundDir</c> (<c>igi2 0x407020</c>) takes the language's folder when it holds a <c>sounds.res</c>
    /// and <see cref="DefaultLanguage"/>'s otherwise.
    /// </remarks>
    public static IReadOnlyList<string> BankDirectories(MissionDescriptor mission, string language = DefaultLanguage)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        return [.. Directories(mission).SelectMany(directory => new[] { directory, $"{directory}/{language}" })];
    }

    /// <summary>Binds the four natives to this table.</summary>
    /// <param name="natives">The namespace.</param>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterFunction(DefineGroupNative, DefineGroup);
        natives.RegisterFunction(DefineSoundNative, DefineSound);
        natives.RegisterFunction(DefineGraphNative, DefineGraph);
        natives.RegisterFunction(DefineTriggerOnceNative, DefineTriggerOnce);
    }

    /// <summary>Forgets every definition, as a level's end deletes its definition tasks.</summary>
    public void Clear()
    {
        _roots.Clear();
        _byName.Clear();
        _open.Clear();
    }

    /// <summary>Reads a level's definitions afresh: every directory's script, in order.</summary>
    /// <param name="resources">Where the scripts are read from.</param>
    /// <param name="directories">The directories, as <see cref="Directories"/> gives them.</param>
    /// <returns>The number of names defined.</returns>
    /// <exception cref="EngineFatalException">A script is present but malformed, or faults.</exception>
    /// <remarks>A directory without a script defines nothing: <c>SoundSys_SoundDefCreateHandler</c> runs only what <c>Script_New</c> opens.</remarks>
    public int Load(ResourceRegistry resources, IReadOnlyList<string> directories)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(directories);
        Clear();

        NativeRegistry natives = new();
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);
        Register(natives);
        foreach (string directory in directories)
        {
            Run(resources, natives, $"{directory}/{ScriptName}");
        }

        _logger.Information(LogCategory, $"{Count} sound definition(s) registered.");
        return Count;
    }

    /// <inheritdoc/>
    public bool TryResolve(string name, out SoundDefinition? definition)
    {
        if (string.IsNullOrEmpty(name))
        {
            definition = null;
            return false;
        }

        return _byName.TryGetValue(name, out definition);
    }

    private void Run(ResourceRegistry resources, NativeRegistry natives, string resource)
    {
        if (!resources.TryLoad(resource, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Debug(LogCategory, $"No sound definitions at {resource}.");
            return;
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

        _open.Clear();
        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException($"{resource} faulted with {error}: {interpreter.ErrorMessage}");
        }
    }

    // SoundDefGroup_ScriptParseCB (igi2ed 0x4349C0): the group is made under the current parent first, the natives are
    // pointed at it, and only then are its arguments -- the calls defining what it holds -- evaluated, one after another
    // while there is one.
    private QvmValue DefineGroup(QvmCallContext call)
    {
        List<SoundDefinition> children = [];
        Add(new SoundDefinition
        {
            Name = call.GetString(0),
            BankKey = string.Empty,
            Kind = SoundDefinitionKind.Group,
            Children = children,
        });

        int argument = 1;
        _open.Push(children);
        try
        {
            for (; argument < call.ArgumentCount; argument++)
            {
                call.GetInt32(argument);
            }
        }
        finally
        {
            _open.Pop();
        }

        return QvmValue.FromInt32(argument);
    }

    // SoundDefSound_ScriptParseCB (igi2ed 0x430B70): name, bank key, offset, volume, nearest and farthest distance; then
    // the parameter indices for volume, pitch and bank key, a real the template keeps (+0x294) and the loop byte.
    private QvmValue DefineSound(QvmCallContext call)
    {
        float farthest = call.GetSingle(7);
        float nearest = call.GetSingle(6);
        if (nearest == 0f)
        {
            nearest = ((Fpu)farthest * NearestOfFarthest).F;
        }

        call.GetSingle(11);
        Add(Common(call, SoundDefinitionKind.Sound, nearest, farthest) with
        {
            VolumeParameterIndex = call.GetInt32(8),
            PitchParameterIndex = call.GetInt32(9),
            BankKeyParameterIndex = call.GetInt32(10),
            Loop = Byte(call, 12),
        });

        return QvmValue.FromInt32(13);
    }

    // SoundDefGraph_ScriptParseCB (igi2ed 0x435A10): the common eight, four parameter indices, then eleven samples of the
    // volume curve and eleven of the pitch curve.
    private QvmValue DefineGraph(QvmCallContext call)
    {
        int argument = 12;
        float[] volume = new float[SoundDefinition.CurveSamples];
        float[] pitch = new float[SoundDefinition.CurveSamples];
        for (int i = 0; i < volume.Length; i++)
        {
            volume[i] = call.GetSingle(argument++);
        }

        for (int i = 0; i < pitch.Length; i++)
        {
            pitch[i] = call.GetSingle(argument++);
        }

        Add(Common(call, SoundDefinitionKind.Graph, call.GetSingle(6), call.GetSingle(7)) with
        {
            VolumeParameterIndex = call.GetInt32(8),
            PitchParameterIndex = call.GetInt32(9),
            BankKeyParameterIndex = call.GetInt32(10),
            PitchAddParameterIndex = call.GetInt32(11),
            Loop = true,
            GainCurve = volume,
            PitchCurve = pitch,
        });

        return QvmValue.FromInt32(argument);
    }

    // SoundDefTriggerOnce_ScriptParseCB (igi2ed 0x434B20): the common eight, the time it plays, four parameter indices
    // (trigger, volume, pitch, bank key), the loop byte and the invert byte.
    private QvmValue DefineTriggerOnce(QvmCallContext call)
    {
        Add(Common(call, SoundDefinitionKind.TriggerOnce, call.GetSingle(6), call.GetSingle(7)) with
        {
            DurationSeconds = call.GetSingle(8),
            TriggerParameterIndex = call.GetInt32(9),
            VolumeParameterIndex = call.GetInt32(10),
            PitchParameterIndex = call.GetInt32(11),
            BankKeyParameterIndex = call.GetInt32(12),
            Loop = Byte(call, 13),
            Invert = Byte(call, 14),
        });

        return QvmValue.FromInt32(15);
    }

    /// <summary>The eight arguments every kind but a group opens with: name, bank key, offset and volume, and its distances.</summary>
    private static SoundDefinition Common(QvmCallContext call, SoundDefinitionKind kind, float nearest, float farthest) => new()
    {
        Name = call.GetString(0),
        BankKey = call.GetString(1),
        Kind = kind,
        Offset = new Vector3d(call.GetSingle(2), call.GetSingle(3), call.GetSingle(4)),
        Volume = call.GetSingle(5),
        MinimumDistanceMetres = nearest,
        MaximumDistanceMetres = farthest,
    };

    /// <summary>An argument kept as a byte: set when its low eight bits are.</summary>
    private static bool Byte(QvmCallContext call, int argument) => (call.GetInt32(argument) & 0xFF) != 0;

    private void Add(SoundDefinition definition)
    {
        (_open.Count > 0 ? _open.Peek() : _roots).Add(definition);

        // Pre-order and first in: the order the find walk meets them, stopping at the first.
        _byName.TryAdd(definition.Name, definition);
    }
}
