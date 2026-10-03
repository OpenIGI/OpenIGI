using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// The ammunition table, read from the <c>ammo/</c> directories.
/// </summary>
/// <remarks>
/// <para>
/// The mirror image of <see cref="WeaponTypeRegistry"/>, down to the shape of the code:
/// <c>0x47CAB0</c> registers the <c>DefineAmmoType</c> native, resets the count and walks
/// <c>LOCAL:ammo</c>; <c>AmmoType_Open</c> runs each sub-directory's <c>ammo.qsc</c>. Fourteen types,
/// fifteen fixed arguments each.
/// </para>
/// <para>
/// Loading this is what retires the <b>second</b> half of the M8 divergence: argument 1 is the
/// <c>AMMO_ID_*</c> symbol name, and the loader registers it against the numeric id as it reads it, so
/// the pattern registered at <c>0x47CF20</c> resolves for real.
/// </para>
/// </remarks>
public sealed class AmmoTypeRegistry
{
    private const string LogCategory = "Weapons";

    /// <summary>The native each ammunition script calls.</summary>
    public const string DefineNative = "DefineAmmoType";

    /// <summary>The directory whose sub-directories are the ammunition types.</summary>
    public const string AmmoDirectory = "LOCAL:ammo";

    /// <summary>The script each ammunition directory holds.</summary>
    public const string AmmoScriptName = "ammo.qvm";

    /// <summary>The lowest calibre number reserved for the thrown and special types.</summary>
    /// <remarks>
    /// Below this a calibre is a real one and names a numerically-titled directory. At or above it the
    /// directory is named instead, so only these scripts give the mapping.
    /// </remarks>
    public const int FirstSpecialCalibre = 10000;

    private readonly List<AmmoType> _types = [];
    private readonly Dictionary<int, AmmoType> _byId = [];
    private readonly Dictionary<int, AmmoType> _byCalibre = [];
    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="AmmoTypeRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public AmmoTypeRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the ammunition types, in load order.</summary>
    public IReadOnlyList<AmmoType> Types => _types;

    /// <summary>Gets the types keyed by their numeric id.</summary>
    public IReadOnlyDictionary<int, AmmoType> ById => _byId;

    /// <summary>Registers the <c>DefineAmmoType</c> native.</summary>
    /// <param name="natives">The script namespace the ammunition scripts resolve against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterFunction(DefineNative, Define);
    }

    /// <summary>Reads every ammunition type under <see cref="AmmoDirectory"/>.</summary>
    /// <param name="fileSystem">The file system the directories are enumerated from.</param>
    /// <param name="resources">Where each script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <returns>The number of types read.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">A script is missing, malformed, or faults.</exception>
    /// <remarks>
    /// <b>Divergence, deliberate</b> — the same one <see cref="WeaponTypeRegistry.Load"/> records. The
    /// directories are sorted ordinally so a run is reproducible; the original takes whatever order the
    /// file system gives. Both lookups scan for a match, so only the array index moves.
    /// </remarks>
    public int Load(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        _types.Clear();
        _byId.Clear();
        _byCalibre.Clear();

        List<string> directories = [.. fileSystem.EnumerateDirectories(AmmoDirectory)];
        directories.Sort(StringComparer.Ordinal);

        foreach (string directory in directories)
        {
            string script = $"{directory}/{AmmoScriptName}";
            if (!resources.TryLoad(script, out ReadOnlyMemory<byte> bytes))
            {
                // AmmoType_Open reports "Couldn't load script" and never returns.
                throw new EngineFatalException($"AmmoType_Open(): Couldn't load script: {script}");
            }

            QvmProgram program;
            try
            {
                program = QvmProgram.Read(bytes);
            }
            catch (QvmFormatException ex)
            {
                throw new EngineFatalException($"AmmoType_Open(): Couldn't load script: {script}", ex);
            }

            QvmInterpreter interpreter = new(program, natives);
            QvmError error = interpreter.Run();
            if (error != QvmError.None)
            {
                throw new EngineFatalException($"{script} faulted with {error}: {interpreter.ErrorMessage}");
            }
        }

        _logger.Information(LogCategory, $"Registered {_types.Count} ammunition type(s).");
        return _types.Count;
    }

    /// <summary>Looks a type up by its numeric id.</summary>
    /// <param name="ammoId">The id.</param>
    /// <param name="type">Receives the type when the id is known.</param>
    /// <returns><see langword="true"/> when the table has that id.</returns>
    /// <remarks>Models the linear search at <c>0x47CD40</c>.</remarks>
    public bool TryGet(int ammoId, out AmmoType type) => _byId.TryGetValue(ammoId, out type!);

    /// <summary>Looks an ammunition type up by its authored <c>AMMO_ID_*</c> symbol.</summary>
    public bool TryGetByScriptId(string scriptId, out AmmoType type)
    {
        ArgumentException.ThrowIfNullOrEmpty(scriptId);
        type = _types.FirstOrDefault(candidate =>
            string.Equals(candidate.ScriptId, scriptId, StringComparison.OrdinalIgnoreCase))!;
        return type is not null;
    }

    /// <summary>Looks a type up by the calibre a weapon names.</summary>
    /// <param name="calibreId">The calibre from a <see cref="WeaponType.CalibreId"/>.</param>
    /// <param name="type">Receives the type when a calibre matches.</param>
    /// <returns><see langword="true"/> when some ammunition is that calibre.</returns>
    /// <remarks>
    /// Models <c>0x47CD70</c>, the join the gun's init (<c>0x4789C0</c>) uses to cache its ammunition
    /// record. The knife's <c>-1</c> matches nothing, which is the point.
    /// </remarks>
    public bool TryGetByCalibre(int calibreId, out AmmoType type) => _byCalibre.TryGetValue(calibreId, out type!);

    private QvmValue Define(QvmCallContext call)
    {
        string scriptId = call.GetString(1);

        AmmoType type = new()
        {
            Index = _types.Count,
            AmmoId = call.GetInt32(0),
            ScriptId = scriptId,
            DisplayName = call.GetString(2),
            Description = call.GetString(3),
            CalibreId = call.GetInt32(4),
            MassInGrams = call.GetSingle(5),
            NearColour = new TracerColour(call.GetInt32(6), call.GetInt32(7), call.GetInt32(8)),
            FarColour = new TracerColour(call.GetInt32(9), call.GetInt32(10), call.GetInt32(11)),
            DrawsTracer = call.GetInt32(12) != 0,
            Model = call.GetString(13),
            Sound = call.GetString(14),
        };

        _types.Add(type);

        // Both of the original's lookups take the first match, so a repeat keeps the earlier entry.
        // Unlike the weapon loader, this one does not check for a duplicate id at all.
        _byId.TryAdd(type.AmmoId, type);
        _byCalibre.TryAdd(type.CalibreId, type);

        if (scriptId.Length > 0 && !call.Interpreter.Natives.Contains(scriptId))
        {
            call.Interpreter.Natives.RegisterConstant(scriptId, type.AmmoId);
        }

        return QvmValue.FromInt32(1);
    }
}
