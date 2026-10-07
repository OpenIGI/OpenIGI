using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// The session's ammunition and weapons, read from <c>WEAPONS/ammo.qvm</c> and <c>WEAPONS/weapon.qvm</c> at start-up.
/// </summary>
/// <remarks>
/// <para>
/// <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) loads the ammunition first, then the weapons, before the
/// materials, the AI's settings and the player's tuning, each into a container that lasts the session.
/// </para>
/// <para>
/// Each <c>AmmoType</c> takes the next number of a count that is never reset (<c>AmmoType_CreateHandler</c>,
/// <c>0x43AE20</c>), and unless its <c>Ammo ID</c> is <c>AMMO_ID_UNDEFINED</c> that name becomes a script constant with
/// the number (<c>AmmoType_UpdateInternalDataHandler</c>, <c>0x43AF10</c>); the weapon script names its ammunition by
/// those constants. Each <c>WeaponType</c> does the same with its own count and its <c>Weapon ID</c>
/// (<c>WeaponType_CreateHandler</c>, <c>0x434FA0</c>; <c>WeaponType_UpdateInternalDataHandler</c>, <c>0x435270</c>), which
/// the player's tuning and the levels' scripts then name. The first weapon is the undefined one, so the real ones
/// count from one.
/// </para>
/// </remarks>
public sealed class Igi2WeaponTables
{
    /// <summary>The ammunition script.</summary>
    public const string AmmoScript = "LOCAL:weapons/ammo.qvm";

    /// <summary>The weapon script.</summary>
    public const string WeaponScript = "LOCAL:weapons/weapon.qvm";

    /// <summary>The ammunition id that registers nothing.</summary>
    public const string UndefinedAmmo = "AMMO_ID_UNDEFINED";

    /// <summary>The weapon id that registers nothing.</summary>
    public const string UndefinedWeapon = "WEAPON_ID_UNDEFINED";

    private const string LogCategory = "Weapons";

    private readonly IOpenIgiLogger _logger;
    private readonly List<Igi2AmmoTypeTask> _ammo = [];
    private readonly List<Igi2WeaponTypeTask> _weapons = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2WeaponTables"/> class.</summary>
    /// <param name="logger">Where the load reports.</param>
    public Igi2WeaponTables(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the ammunition, by its number.</summary>
    public IReadOnlyList<Igi2AmmoTypeTask> AmmoTypes => _ammo;

    /// <summary>Gets the weapons, by their number.</summary>
    public IReadOnlyList<Igi2WeaponTypeTask> WeaponTypes => _weapons;

    /// <summary>Finds a weapon by its number, the value of its <c>WEAPON_ID_*</c> constant.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The weapon, or <see langword="null"/>.</returns>
    public Igi2WeaponTypeTask? Weapon(int number) => (uint)number < (uint)_weapons.Count ? _weapons[number] : null;

    /// <summary>Finds a kind of ammunition by its number, the value of its <c>AMMO_ID_*</c> constant.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The ammunition, or <see langword="null"/>.</returns>
    public Igi2AmmoTypeTask? Ammo(int number) => (uint)number < (uint)_ammo.Count ? _ammo[number] : null;

    /// <summary>Runs the two scripts and registers the ids they declare.</summary>
    /// <param name="resources">Where the scripts are read from.</param>
    /// <param name="natives">The script namespace, with the task commands and the weapon constants bound.</param>
    /// <param name="tree">The task tree the containers are declared into.</param>
    /// <exception cref="EngineFatalException">A script is missing or faults.</exception>
    public void Load(ResourceRegistry resources, NativeRegistry natives, TaskTree tree)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(tree);

        Igi2SessionScript.Run(resources, natives, AmmoScript);
        int before = _ammo.Count;
        Collect(tree.Root, _ammo);
        RegisterIds(natives, _ammo, before, static a => a.AmmoId, UndefinedAmmo);

        Igi2SessionScript.Run(resources, natives, WeaponScript);
        before = _weapons.Count;
        Collect(tree.Root, _weapons);
        RegisterIds(natives, _weapons, before, static w => w.WeaponId, UndefinedWeapon);

        _logger.Information(LogCategory, $"{_ammo.Count} ammunition types and {_weapons.Count} weapon types read.");
    }

    /// <summary>Gathers the tasks of one type in the order they were made, which is their order in the tree.</summary>
    private static void Collect<T>(GameTask task, List<T> into)
        where T : GameTask
    {
        foreach (GameTask child in task.Children)
        {
            if (child is T found && !into.Contains(found))
            {
                into.Add(found);
            }

            Collect(child, into);
        }
    }

    private static void RegisterIds<T>(NativeRegistry natives, List<T> tasks, int from, Func<T, string> id, string undefined)
    {
        for (int number = from; number < tasks.Count; number++)
        {
            string name = id(tasks[number]);
            if (!string.Equals(name, undefined, StringComparison.Ordinal))
            {
                natives.RegisterConstant(name, number);
            }
        }
    }
}
