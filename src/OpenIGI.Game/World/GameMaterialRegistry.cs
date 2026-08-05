using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>
/// The game material table — what every surface in the world is made of.
/// </summary>
/// <remarks>
/// <para>
/// One script, <c>material/material.qvm</c>, declares the lot. It alternates
/// <c>SetActiveMaterial(n)</c> with <c>DefineQMaterial</c> and <c>DefineGameMaterial</c>, so the
/// <b>row a material lands in is the argument to <c>SetActiveMaterial</c></b> — which is what a
/// collision face's fourth field indexes.
/// </para>
/// <para>
/// The module (<c>0x407D60</c>) registers all three natives and allocates three messages: a parameter
/// size query, a parameter reader, and the <b>impact</b> message that <c>0x4083C0</c> sends. Every
/// material in the retail data names <c>TASKTYPE_GUNSHOTEFFECT</c> for both of its effect slots, so
/// that one class is the whole of the decal system.
/// </para>
/// </remarks>
public sealed class GameMaterialRegistry
{
    /// <summary>The script the whole table comes from.</summary>
    public const string ScriptResource = "LOCAL:material/material.qvm";

    /// <summary>The native that chooses which row is being filled.</summary>
    public const string SetActiveNative = "SetActiveMaterial";

    /// <summary>The native that fills a row's rendering half, which nothing here reads.</summary>
    public const string DefineQNative = "DefineQMaterial";

    /// <summary>The native that fills a row.</summary>
    public const string DefineNative = "DefineGameMaterial";

    /// <summary>The class every material names for both of its impact effects.</summary>
    public const string EffectTaskType = "TASKTYPE_GUNSHOTEFFECT";

    /// <summary>How many arguments the effect class consumes, including its own type id.</summary>
    /// <remarks>Six parameters, from <c>0x4387B0</c>, plus the id itself.</remarks>
    public const int EffectArgumentCount = 7;

    /// <summary>How many footstep sounds a material declares.</summary>
    public const int WalkSoundCount = 6;

    /// <summary>How many bullet-impact sounds a material declares.</summary>
    public const int BulletSoundCount = 6;

    private readonly Dictionary<int, GameMaterial> _byIndex = [];
    private readonly IIgiLogger _logger;
    private int _active;

    /// <summary>Initializes a new instance of the <see cref="GameMaterialRegistry"/> class.</summary>
    /// <param name="logger">Where to report a row that cannot be read.</param>
    public GameMaterialRegistry(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the materials, keyed by the row they occupy.</summary>
    public IReadOnlyDictionary<int, GameMaterial> ByIndex => _byIndex;

    /// <summary>Gets how many rows were declared.</summary>
    public int Count => _byIndex.Count;

    /// <summary>Registers the three natives the material script calls.</summary>
    /// <param name="natives">The script namespace the script resolves against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        natives.RegisterFunction(SetActiveNative, SetActive);
        natives.RegisterFunction(DefineNative, Define);

        // The rendering half of a material -- colour and blend. Registered so the script runs, and
        // ignored, because nothing this port draws consults it yet.
        natives.RegisterFunction(DefineQNative, _ => QvmValue.FromInt32(0));

        if (!natives.Contains(EffectTaskType))
        {
            natives.RegisterConstant(EffectTaskType, 1);
        }

        foreach ((string name, int value) in new[] { ("TRUE", 1), ("FALSE", 0) })
        {
            if (!natives.Contains(name))
            {
                natives.RegisterConstant(name, value);
            }
        }
    }

    /// <summary>Runs the material script.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <returns>How many materials were declared.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public int Load(ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        _byIndex.Clear();
        _active = 0;

        if (!resources.TryLoad(ScriptResource, out ReadOnlyMemory<byte> bytes))
        {
            _logger.Warning("Material", $"No material table at {ScriptResource}; no surface leaves a mark.");
            return 0;
        }

        QvmProgram program = QvmProgram.Read(bytes);
        QvmInterpreter interpreter = new(program, natives);
        interpreter.Run();

        return _byIndex.Count;
    }

    /// <summary>Puts a material in a row, replacing whatever was there.</summary>
    /// <param name="material">The material, whose <see cref="GameMaterial.Index"/> is the row.</param>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// What <c>SetActiveMaterial</c> followed by <c>DefineGameMaterial</c> amounts to, exposed so a
    /// table can be built without a script — which is what lets the mark rules be tested against a
    /// material that names a texture and one that does not, side by side.
    /// </remarks>
    public void Add(GameMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        _byIndex[material.Index] = material;
    }

    /// <summary>Finds the material a collision face names.</summary>
    /// <param name="index">The face's fourth field.</param>
    /// <param name="material">The material.</param>
    /// <returns><see langword="false"/> when no row was declared at that index.</returns>
    public bool TryGet(int index, out GameMaterial material) => _byIndex.TryGetValue(index, out material!);

    /// <summary>Finds a material by the name the script declared it under.</summary>
    /// <param name="name">The row's name, such as <c>Ground</c> or <c>Wood</c>.</param>
    /// <param name="material">The material.</param>
    /// <returns><see langword="false"/> when no row carries that name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Rows are addressed by index everywhere a collision face names one; this exists for the places
    /// that have no face to read an index off — a step on bare terrain, which has to reach the
    /// <c>Ground</c> row by name because nothing hands it a number.
    /// </remarks>
    public bool TryGetByName(string name, out GameMaterial material)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (GameMaterial candidate in _byIndex.Values)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                material = candidate;
                return true;
            }
        }

        material = null!;
        return false;
    }

    private QvmValue SetActive(QvmCallContext call)
    {
        _active = call.GetInt32(0);
        return QvmValue.FromInt32(0);
    }

    private QvmValue Define(QvmCallContext call)
    {
        // The reader walks the arguments in the order 0x407F50 stores them, because the record is
        // filled positionally and a miscount would land every string one field out.
        int cursor = 6;
        string[] walk = ReadStrings(call, ref cursor, WalkSoundCount);
        string land = call.GetString(cursor++);
        string rain = call.GetString(cursor++);
        string[] bullet = ReadStrings(call, ref cursor, BulletSoundCount);
        string[] extra = ReadStrings(call, ref cursor, 2);

        GunShotEffect entry = ReadEffect(call, ref cursor);
        GunShotEffect exit = ReadEffect(call, ref cursor);

        GameMaterial material = new()
        {
            Index = _active,
            Name = call.GetString(0),
            Value1 = call.GetSingle(1),
            Value2 = call.GetSingle(2),
            Value3 = call.GetSingle(3),
            Flag4 = call.GetInt32(4) != 0,
            Value5 = call.GetSingle(5),
            WalkSounds = walk,
            LandSound = land,
            RainSound = rain,
            BulletSounds = bullet,
            ExtraSounds = extra,
            Entry = entry,
            Exit = exit,
        };

        _byIndex[_active] = material;
        return QvmValue.FromInt32(0);
    }

    // Each effect slot is a task type id followed by that class's own parameters. Retail names
    // GunShotEffect every time; anything else is skipped with its arguments left unread, which would
    // desynchronise the cursor -- so it is reported rather than guessed at.
    private static GunShotEffect ReadEffect(QvmCallContext call, ref int cursor)
    {
        cursor++;

        GunShotEffect effect = new(
            call.GetInt32(cursor) != 0,
            call.GetInt32(cursor + 1) != 0,
            call.GetString(cursor + 2),
            call.GetString(cursor + 3),
            call.GetSingle(cursor + 4),
            call.GetString(cursor + 5));

        cursor += EffectArgumentCount - 1;
        return effect;
    }

    private static string[] ReadStrings(QvmCallContext call, ref int cursor, int count)
    {
        string[] values = new string[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = call.GetString(cursor++);
        }

        return values;
    }
}
