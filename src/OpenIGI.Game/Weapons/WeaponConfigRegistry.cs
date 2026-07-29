using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Weapons;

/// <summary>
/// The weapon roster as <c>weapons/weaponconfig.qsc</c> declares it — <b>not</b> the table the game runs on.
/// </summary>
/// <remarks>
/// <para>
/// The script is an ordinary task tree, so loading it is just running it against the task system, and
/// it does describe all twenty-four weapons in the same field order as the live path. But it is
/// <b>inert</b>: <c>0x402070</c> loads it after the weapons themselves, and the <c>WeaponConfig</c>
/// ctor (<c>0x407BB0</c>) only zeroes its own storage and defaults the id to <c>-1</c>. Nothing in the
/// firing, HUD or AI code reads the tree. <see cref="WeaponTypeRegistry"/> is the real roster.
/// </para>
/// <para>
/// It is not dead weight, though. A <c>WeaponConfig</c> instance is 1800 bytes, which is exactly the
/// 32-byte task header plus the 1768-byte runtime record, and its extra fiftieth parameter — <c>Is
/// weapon selectable when empty</c> — lands at the very offset the live loader hardcodes to 1. So this
/// is the <i>same struct</i>, declared a field further on: an intended replacement for
/// <c>DefineWeaponType</c> that shipped without ever being wired up. That makes it a genuine
/// cross-check on the live reader, which is what the tests use it for.
/// </para>
/// </remarks>
public sealed class WeaponConfigRegistry
{
    private const string LogCategory = "Weapons";

    /// <summary>The script that carries the roster.</summary>
    public const string ScriptResource = "LOCAL:weapons/weaponconfig.qvm";

    /// <summary>The task type each weapon is declared as.</summary>
    public const string WeaponConfigType = "WeaponConfig";

    /// <summary>The type the weapons hang off.</summary>
    public const string WeaponConfigContainerType = "WeaponConfigContainer";

    private readonly List<WeaponConfigTask> _weapons = [];
    private readonly Dictionary<int, WeaponConfigTask> _byId = [];
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="WeaponConfigRegistry"/> class.</summary>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    public WeaponConfigRegistry(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the weapons, in script order.</summary>
    public IReadOnlyList<WeaponConfigTask> Weapons => _weapons;

    /// <summary>Gets the weapons keyed by their numeric id.</summary>
    public IReadOnlyDictionary<int, WeaponConfigTask> ById => _byId;

    /// <summary>Registers the weapon task types.</summary>
    /// <param name="types">The registry to add to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="types"/> is <see langword="null"/>.</exception>
    public static void RegisterTypes(TaskTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(types);
        types.Register<WeaponConfigTask>(WeaponConfigType);
        types.Register<WeaponConfigContainerTask>(WeaponConfigContainerType);
    }

    /// <summary>Reads and runs the weapon configuration.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace; the enum constants must already be registered.</param>
    /// <param name="tree">The task tree the configuration is built into.</param>
    /// <returns>The number of weapons registered.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">The script is missing, malformed, or faults.</exception>
    public int Load(ResourceRegistry resources, NativeRegistry natives, TaskTree tree)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(tree);

        _weapons.Clear();
        _byId.Clear();

        if (!resources.TryLoad(ScriptResource, out ReadOnlyMemory<byte> bytes))
        {
            throw new EngineFatalException($"Couldn't load script: {ScriptResource}");
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {ScriptResource}", ex);
        }

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException(
                $"{ScriptResource} faulted with {error}: {interpreter.ErrorMessage}");
        }

        Collect(tree.Root);
        _logger.Information(LogCategory, $"Registered {_weapons.Count} weapon(s).");
        return _weapons.Count;
    }

    /// <summary>
    /// Publishes each weapon's <c>WEAPON_ID_*</c> symbol with its real numeric id.
    /// </summary>
    /// <param name="natives">The namespace later scripts resolve against.</param>
    /// <returns>The number of symbols published.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Run this before anything that names a weapon — <c>humanplayer.qvm</c> above all. A symbol
    /// somebody else has already registered is left alone rather than overwritten, so a caller can
    /// still pin one by hand.
    /// </remarks>
    public int PublishScriptIds(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        int published = 0;
        foreach (WeaponConfigTask weapon in _weapons)
        {
            if (weapon.ScriptId.Length == 0 || natives.Contains(weapon.ScriptId))
            {
                continue;
            }

            natives.RegisterConstant(weapon.ScriptId, weapon.WeaponId);
            published++;
        }

        return published;
    }

    /// <summary>Looks a weapon up by its numeric id.</summary>
    /// <param name="weaponId">The id.</param>
    /// <param name="weapon">Receives the weapon when the id is known.</param>
    /// <returns><see langword="true"/> when the roster has that id.</returns>
    public bool TryGet(int weaponId, out WeaponConfigTask weapon) => _byId.TryGetValue(weaponId, out weapon!);

    private void Collect(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is WeaponConfigTask weapon)
            {
                _weapons.Add(weapon);

                // The original's lookup takes the first match, so a repeated id keeps the earlier one.
                _byId.TryAdd(weapon.WeaponId, weapon);
            }

            Collect(child);
        }
    }
}
