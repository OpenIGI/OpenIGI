using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// The runtime weapon table: the roster the firing, HUD and AI code actually read.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not <c>weaponconfig.qsc</c>.</b> <c>0x413B70</c> resets the weapon count and walks
/// <c>LOCAL:weapons</c> with <c>File_WalkTree</c>, and <c>WeaponType_Open</c> (<c>0x413A90</c>) runs
/// <c>&lt;dir&gt;/weapon.qsc</c> for every sub-directory it finds. Each of those scripts is a single
/// <c>DefineWeaponType</c> call. The parallel <see cref="WeaponConfigRegistry"/> declaration loads
/// afterwards and describes the same twenty-four weapons in the same field order, but its task ctor
/// only zeroes its own storage — nothing at runtime reads it.
/// </para>
/// <para>
/// Two consequences worth stating. The call is <b>variable length</b> because the projectile and weapon
/// task classes read their own parameters from its tail (<see cref="WeaponTaskParameters"/>). And the
/// <c>WEAPON_ID_*</c> symbols get their values <i>here</i>, as each weapon is read, which is what the
/// pattern registration in <c>player.md</c> §3.5 was waiting for.
/// </para>
/// </remarks>
public sealed class WeaponTypeRegistry
{
    private const string LogCategory = "Weapons";

    /// <summary>The native each weapon's script calls.</summary>
    public const string DefineNative = "DefineWeaponType";

    /// <summary>The directory whose sub-directories are the weapons.</summary>
    public const string WeaponDirectory = "LOCAL:weapons";

    /// <summary>The script each weapon directory holds.</summary>
    public const string WeaponScriptName = "weapon.qvm";

    /// <summary>The prefix of the task-type constants the last two arguments use.</summary>
    public const string TaskTypeConstantPrefix = "TASKTYPE_";

    /// <summary>The index of the projectile task type, and so of the fixed part's end.</summary>
    /// <remarks>Arguments 0 to 47 are always present; everything after 47 depends on the classes named.</remarks>
    public const int ProjectileTaskTypeArgument = 47;

    private readonly List<WeaponType> _weapons = [];
    private readonly Dictionary<int, WeaponType> _byId = [];
    private readonly Dictionary<int, string> _taskTypeNames = [];
    private readonly IOpenIgiLogger _logger;
    private int _nextTaskTypeId = 1;

    /// <summary>Initializes a new instance of the <see cref="WeaponTypeRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public WeaponTypeRegistry(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the weapons, in load order — the original's array order.</summary>
    public IReadOnlyList<WeaponType> Weapons => _weapons;

    /// <summary>Gets the weapons keyed by their numeric id.</summary>
    public IReadOnlyDictionary<int, WeaponType> ById => _byId;

    /// <summary>Gets the largest id any weapon claimed.</summary>
    /// <remarks>The original tracks this in <c>0x5391B4</c> and returns it from <c>0x413BC0</c>.</remarks>
    public int MaximumWeaponId { get; private set; }

    /// <summary>Registers the <c>DefineWeaponType</c> native and the constants its arguments name.</summary>
    /// <param name="natives">The script namespace the weapon scripts resolve against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Mirrors <c>0x413DF0</c>, which registers the native and then all sixteen enum members by name.
    /// Their values are the executable's own, so nothing here is invented.
    /// </remarks>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);
        natives.RegisterFunction(DefineNative, Define);

        RegisterConstant(natives, "WEAPONTYPE_GUN", (int)WeaponCategory.Gun);
        RegisterConstant(natives, "WEAPONTYPE_PISTOL", (int)WeaponCategory.Pistol);
        RegisterConstant(natives, "WEAPONTYPE_SHOTGUN", (int)WeaponCategory.Shotgun);
        RegisterConstant(natives, "WEAPONTYPE_KNIFE", (int)WeaponCategory.Knife);
        RegisterConstant(natives, "WEAPONTYPE_GRENADE", (int)WeaponCategory.Grenade);
        RegisterConstant(natives, "WEAPONTYPE_BINOCULAR", (int)WeaponCategory.Binocular);
        RegisterConstant(natives, "WEAPONTYPE_MEDIPACK", (int)WeaponCategory.Medipack);
        RegisterConstant(natives, "WEAPONTYPE_PROXIMITYMINE", (int)WeaponCategory.ProximityMine);

        RegisterConstant(natives, "SIGHTDISPLAYTYPE_NONE", (int)SightDisplayType.None);
        RegisterConstant(natives, "SIGHTDISPLAYTYPE_CROSSHAIR", (int)SightDisplayType.Crosshair);

        RegisterConstant(natives, "AMMODISPLAYTYPE_NONE", (int)AmmoDisplayType.None);
        RegisterConstant(natives, "AMMODISPLAYTYPE_NORMAL", (int)AmmoDisplayType.Normal);
        RegisterConstant(natives, "AMMODISPLAYTYPE_CLIP", (int)AmmoDisplayType.Clip);
        RegisterConstant(natives, "AMMODISPLAYTYPE_BARREL", (int)AmmoDisplayType.Barrel);
        RegisterConstant(natives, "AMMODISPLAYTYPE_SHELL", (int)AmmoDisplayType.Shell);
        RegisterConstant(natives, "AMMODISPLAYTYPE_GRENADE", (int)AmmoDisplayType.Grenade);

        // The AI module's own constants (0x44EFF3), in world units: a gunshot carries thirty metres and
        // a silenced one carries one.
        RegisterConstant(natives, "HUMANAI_DETECTIONEVENT_GUNSHOT_RANGE", 0x1E000);
        RegisterConstant(natives, "HUMANAI_DETECTIONEVENT_GUNSHOT_SILENCED_RANGE", 0x1000);
    }

    /// <summary>Reads every weapon under <see cref="WeaponDirectory"/>.</summary>
    /// <param name="fileSystem">The file system the weapon directories are enumerated from.</param>
    /// <param name="resources">Where each weapon's script is read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <returns>The number of weapons read.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">A script is missing, malformed, faults, or repeats an id.</exception>
    /// <remarks>
    /// <b>Divergence, deliberate.</b> The original visits the directories in whatever order the file
    /// system hands them back, which is not defined and differs between an installation and an archive.
    /// This sorts them ordinally so a run is reproducible. Nothing observable depends on the choice:
    /// both of the original's lookups scan for a match — by id at <c>0x413BD0</c>, by gun model at
    /// <c>0x413C00</c> — and ids are unique, so only <see cref="WeaponType.Index"/> moves.
    /// </remarks>
    public int Load(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        _weapons.Clear();
        _byId.Clear();
        MaximumWeaponId = 0;

        List<string> directories = [.. fileSystem.EnumerateDirectories(WeaponDirectory)];
        directories.Sort(StringComparer.Ordinal);

        foreach (string directory in directories)
        {
            string script = $"{directory}/{WeaponScriptName}";
            if (!resources.TryLoad(script, out ReadOnlyMemory<byte> bytes))
            {
                // WeaponType_Open reports "Couldn't load script" and never returns.
                throw new EngineFatalException($"WeaponType_Open(): Couldn't load script: {script}");
            }

            QvmProgram program;
            try
            {
                program = QvmProgram.Read(bytes);
            }
            catch (QvmFormatException ex)
            {
                throw new EngineFatalException($"WeaponType_Open(): Couldn't load script: {script}", ex);
            }

            DeclareTaskTypeConstants(program, natives);

            QvmInterpreter interpreter = new(program, natives);
            QvmError error = interpreter.Run();
            if (error != QvmError.None)
            {
                throw new EngineFatalException($"{script} faulted with {error}: {interpreter.ErrorMessage}");
            }
        }

        _logger.Information(LogCategory, $"Registered {_weapons.Count} weapon(s).");
        return _weapons.Count;
    }

    /// <summary>Looks a weapon up by its numeric id.</summary>
    /// <param name="weaponId">The id.</param>
    /// <param name="weapon">Receives the weapon when the id is known.</param>
    /// <returns><see langword="true"/> when the roster has that id.</returns>
    /// <remarks>Models the linear search at <c>0x413BD0</c>.</remarks>
    public bool TryGet(int weaponId, out WeaponType weapon) => _byId.TryGetValue(weaponId, out weapon!);

    /// <summary>Looks a weapon up by the <c>WEAPON_ID_*</c> symbol a script names it with.</summary>
    /// <param name="scriptId">The symbol, e.g. <c>WEAPON_ID_KNIFE</c>.</param>
    /// <param name="weapon">Receives the weapon when the symbol is known.</param>
    /// <returns><see langword="true"/> when the roster carries it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scriptId"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This is the lookup a <b>level</b> needs, as opposed to the two the executable does: a mission's
    /// <c>objects.qvm</c> declares its loadout with these symbols rather than with numeric ids, so
    /// <see cref="Player.PlayerLoadout"/> resolves through here.
    /// </remarks>
    public bool TryGetByScriptId(string scriptId, out WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(scriptId);
        weapon = _weapons.Find(candidate => string.Equals(candidate.ScriptId, scriptId, StringComparison.Ordinal))!;
        return weapon is not null;
    }

    /// <summary>Looks a weapon up by the model it is drawn with.</summary>
    /// <param name="gunModel">The model name.</param>
    /// <param name="weapon">Receives the first weapon using that model.</param>
    /// <returns><see langword="true"/> when a weapon uses it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gunModel"/> is <see langword="null"/>.</exception>
    /// <remarks>Models the linear search at <c>0x413C00</c>, which compares the record's <c>+1564</c>.</remarks>
    public bool TryGetByGunModel(string gunModel, out WeaponType weapon)
    {
        ArgumentNullException.ThrowIfNull(gunModel);
        weapon = _weapons.Find(candidate => string.Equals(candidate.GunModel, gunModel, StringComparison.Ordinal))!;
        return weapon is not null;
    }

    /// <summary>Replaces the executable-style records with the matching weaponconfig.qvm values.</summary>
    /// <remarks>
    /// The task bindings remain from each weapon's own script because they are class-specific tails;
    /// every shared weapon field comes from the config roster after this method returns.
    /// </remarks>
    public void ApplyRetailConfig(WeaponConfigRegistry config)
    {
        ArgumentNullException.ThrowIfNull(config);

        for (int i = 0; i < _weapons.Count; i++)
        {
            WeaponType current = _weapons[i];
            if (!config.TryGet(current.WeaponId, out WeaponConfigTask declared))
            {
                throw new EngineFatalException($"Weapon config is missing {current.ScriptId} ({current.WeaponId}).");
            }

            WeaponType configured = current.WithRetailConfig(declared);
            _weapons[i] = configured;
            _byId[current.WeaponId] = configured;
        }
    }

    /// <summary>Gets the script constant a task type id came from, for diagnostics.</summary>
    /// <param name="taskType">The id.</param>
    /// <returns>The constant's name, or an empty string when the id is not one this registry allocated.</returns>
    public string TaskTypeName(int taskType) => _taskTypeNames.GetValueOrDefault(taskType, string.Empty);

    private static void RegisterConstant(NativeRegistry natives, string name, int value)
    {
        if (!natives.Contains(name))
        {
            natives.RegisterConstant(name, value);
        }
    }

    /// <summary>
    /// Declares the <c>TASKTYPE_*</c> constants a weapon script names.
    /// </summary>
    /// <remarks>
    /// <b>Divergence, deliberate</b> — the same one <c>MagicObjectRegistry</c> records. The original
    /// allocates a task type id as each class registers at start-up, so the number depends only on
    /// initialisation order and appears in no data file. The classes do not exist yet, so ids are handed
    /// out in first-seen order and kept alongside their names. The <i>name</i> is what selects the
    /// parameter reader, so the numbering does not affect what is read.
    /// </remarks>
    private void DeclareTaskTypeConstants(QvmProgram program, NativeRegistry natives)
    {
        foreach (string? symbol in program.SymbolNames)
        {
            if (symbol is null || !symbol.StartsWith(TaskTypeConstantPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // The TASKTYPE_* constants live in the process-wide native namespace, which OUTLIVES this
            // registry: a mission restart or a level change builds a fresh WeaponTypeRegistry but reuses
            // the same natives. The id->name map (_taskTypeNames), however, is per-instance and starts
            // empty. When an earlier load already registered the constant we must still adopt its value
            // into this instance's map -- otherwise TaskTypeName() returns "" for it, ReadTaskBinding
            // hands WeaponTaskParameters.Read an empty class name, and every optical weapon (the Dragunov
            // scope, the MP5SD sight, the binoculars) loads with no task type and no zoom parameters on
            // every load after the first. That is issue #79: ADS is dead after a restart or level change.
            if (natives.TryResolve(symbol, out QvmSymbol? existing) && existing.IsReadable)
            {
                int existingId = existing.Read().ToInt32();
                _taskTypeNames[existingId] = symbol;
                _nextTaskTypeId = Math.Max(_nextTaskTypeId, existingId + 1);
                continue;
            }

            natives.RegisterConstant(symbol, _nextTaskTypeId);
            _taskTypeNames[_nextTaskTypeId] = symbol;
            _nextTaskTypeId++;
        }
    }

    private QvmValue Define(QvmCallContext call)
    {
        int weaponId = call.GetInt32(0);
        if (_byId.ContainsKey(weaponId))
        {
            // The original prints this and then spins forever, so it is fatal rather than a warning.
            throw new EngineFatalException($"Weapon ID {weaponId} not unique");
        }

        MaximumWeaponId = Math.Max(MaximumWeaponId, weaponId);

        string scriptId = call.GetString(1);

        int cursor = ProjectileTaskTypeArgument;
        WeaponTaskBinding projectile = ReadTaskBinding(call, ref cursor);
        WeaponTaskBinding weapon = ReadTaskBinding(call, ref cursor);

        WeaponType type = new()
        {
            Index = _weapons.Count,
            WeaponId = weaponId,
            ScriptId = scriptId,
            DisplayName = call.GetString(2),
            Manufacturer = call.GetString(3),
            Description = call.GetString(4),
            Category = (WeaponCategory)call.GetInt32(5),
            SightDisplay = (SightDisplayType)call.GetInt32(6),
            AmmoDisplay = (AmmoDisplayType)call.GetInt32(7),
            MassInGrams = call.GetInt32(8),
            CalibreId = call.GetInt32(9),
            DamageFactor = call.GetSingle(10),
            PenetrationPower = call.GetSingle(11),
            ReloadTimeSeconds = call.GetSingle(12),
            MuzzleVelocity = call.GetSingle(13),
            BulletsPerRound = call.GetInt32(14),
            RoundsPerMinute = call.GetInt32(15),
            RoundsPerClip = call.GetInt32(16),
            MaximumRoundsPerBurst = call.GetInt32(17),
            MinimumSpread = call.GetSingle(18),
            MaximumSpread = call.GetSingle(19),
            FixedRecoilX = call.GetSingle(20),
            FixedRecoilZ = call.GetSingle(21),
            RandomRecoilX = call.GetSingle(22),
            RandomRecoilZ = call.GetSingle(23),
            TypeOfWeapon = call.GetString(24),
            EffectiveRange = call.GetSingle(25),
            WeaponUsers = call.GetString(26),
            WeaponLength = call.GetInt32(27),
            BarrelLength = call.GetInt32(28),
            GunModel = call.GetString(29),
            CasingModel = call.GetString(30),
            StandAnimation = call.GetInt32(31),
            MoveAnimation = call.GetInt32(32),
            FireAnimation1 = call.GetInt32(33),
            FireAnimation2 = call.GetInt32(34),
            FireAnimation3 = call.GetInt32(35),
            ReloadAnimation = call.GetInt32(36),
            UpperBodyStandAnimation = call.GetInt32(37),
            UpperBodyWalkAnimation = call.GetInt32(38),
            UpperBodyCrouchAnimation = call.GetInt32(39),
            UpperBodyCrouchRunAnimation = call.GetInt32(40),
            UpperBodyRunAnimation = call.GetInt32(41),
            UpperBodyFireAnimation = call.GetInt32(42),
            UpperBodyReloadAnimation = call.GetInt32(43),
            FireSound = call.GetString(44),
            FireLoopEndSound = call.GetString(45),
            AiDetectionRange = call.GetSingle(46),
            Projectile = projectile,
            Weapon = weapon,
        };

        _weapons.Add(type);
        _byId.Add(weaponId, type);

        // The loader publishes the symbol as it reads it, which is what gives the pattern-registered
        // WEAPON_ID_* names their values.
        if (scriptId.Length > 0 && !call.Interpreter.Natives.Contains(scriptId))
        {
            call.Interpreter.Natives.RegisterConstant(scriptId, weaponId);
        }

        return QvmValue.FromInt32(1);
    }

    private WeaponTaskBinding ReadTaskBinding(QvmCallContext call, ref int cursor)
    {
        int taskTypeId = call.GetInt32(cursor);
        cursor++;

        string name = TaskTypeName(taskTypeId);
        return WeaponTaskParameters.Read(name, taskTypeId, call, ref cursor);
    }
}
