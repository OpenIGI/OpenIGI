using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's tuning, read from <c>humanplayer.qvm</c> once at start-up.</summary>
/// <remarks>
/// <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) runs <c>LOCAL:humanplayer/humanplayer.qsc</c> into a
/// <c>HumanPlayerConfig</c> task straight after the AI's settings, before any level: the script declares one
/// <c>HumanPlayerConfigItem</c>, whose figures every player then takes (<c>HumanPlayer_UpdateParameters</c>,
/// <c>0x4BD1F0</c>). The task stays in the tree for the session.
/// <para>
/// The item also carries two scripts, evaluated as it takes its parameters
/// (<c>HumanPlayerConfigItem_UpdateInternalDataHandler</c>, <c>0x438540</c>): the weapon cycle's, which calls
/// <c>DefineHumanPlayerWeaponCycle</c>, then the ammunition limits', which calls <c>DefineHumanPlayerAmmoLimit</c>. Both
/// natives replace a global table; the limits apply to every human made afterwards, soldiers included.
/// </para>
/// </remarks>
public sealed class Igi2PlayerTuning
{
    /// <summary>The script the tuning is read from.</summary>
    public const string ScriptResource = "LOCAL:humanplayer/humanplayer.qvm";

    /// <summary>The native the weapon cycle script calls.</summary>
    public const string DefineWeaponCycleNative = "DefineHumanPlayerWeaponCycle";

    /// <summary>The native the ammunition limit script calls.</summary>
    public const string DefineAmmoLimitNative = "DefineHumanPlayerAmmoLimit";

    private const string LogCategory = "Player";

    private readonly IOpenIgiLogger _logger;
    private readonly List<int> _weaponCycle = [];
    private readonly List<Igi2AmmoLimit> _ammoLimits = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2PlayerTuning"/> class.</summary>
    /// <param name="logger">Where the load reports.</param>
    public Igi2PlayerTuning(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the order weapons are cycled through, by <c>WEAPON_ID_*</c> number (<c>HumanPlayer_tWeaponCycle</c>).</summary>
    public IReadOnlyList<int> WeaponCycle => _weaponCycle;

    /// <summary>Gets the most of each ammunition a human may carry, in the order defined (<c>HumanPlayer_tAmmoLimit</c>).</summary>
    public IReadOnlyList<Igi2AmmoLimit> AmmoLimits => _ammoLimits;

    /// <summary>Registers the two natives the tuning's scripts call.</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="functions">The natives script text may call.</param>
    public void RegisterNatives(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        functions.Register(natives, DefineWeaponCycleNative, DefineWeaponCycle, 0);
        functions.Register(natives, DefineAmmoLimitNative, DefineAmmoLimit, 0);
    }

    /// <summary>Gets the tuning, or <see cref="Igi2HumanPlayerGeneral.Unset"/> before the script has run.</summary>
    public Igi2HumanPlayerGeneral General { get; private set; } = Igi2HumanPlayerGeneral.Unset;

    /// <summary>Runs the script and takes the config item it declares.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with the task commands and the weapon constants bound.</param>
    /// <param name="tree">The task tree the item is declared into.</param>
    /// <param name="functions">The natives the item's two scripts may call.</param>
    /// <returns><see langword="true"/> when the script declared an item.</returns>
    /// <exception cref="EngineFatalException">The script is missing or faults.</exception>
    public bool Load(ResourceRegistry resources, NativeRegistry natives, TaskTree tree, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(functions);

        Igi2SessionScript.Run(resources, natives, ScriptResource);

        if (Find(tree.Root) is not { } item)
        {
            _logger.Warning(LogCategory, $"{ScriptResource} declared no HumanPlayerConfigItem; the player is untuned.");
            return false;
        }

        Igi2LineScript.Evaluate(item.WeaponCycleScript, natives, functions);
        Igi2LineScript.Evaluate(item.AmmoLimitScript, natives, functions);
        General = Igi2HumanPlayerGeneral.From(item);
        _logger.Information(LogCategory, $"Player tuning read from {ScriptResource}.");
        return true;
    }

    /// <summary><c>DefineHumanPlayerWeaponCycle(weaponId, ...)</c> (<c>HumanPlayer_DefineWeaponCycleCB</c>, <c>igi2 0x49EB00</c>).</summary>
    private QvmValue DefineWeaponCycle(Igi2ScriptCall call)
    {
        _weaponCycle.Clear();
        for (int i = 0; i < call.Count; i++)
        {
            _weaponCycle.Add(call.GetInt32(i));
        }

        return QvmValue.FromInt32(1);
    }

    /// <summary>
    /// <c>DefineHumanPlayerAmmoLimit(ammoId, max, ...)</c> (<c>HumanPlayer_DefineAmmoLimitCB</c>, <c>igi2 0x49ED10</c>), read in
    /// pairs. The original reads past the list for an odd count; here the missing limit reads as zero.
    /// </summary>
    private QvmValue DefineAmmoLimit(Igi2ScriptCall call)
    {
        _ammoLimits.Clear();
        for (int i = 0; i < call.Count; i += 2)
        {
            int ammo = call.GetInt32(i);
            int max = i + 1 < call.Count ? call.GetInt32(i + 1) : 0;
            _ammoLimits.Add(new Igi2AmmoLimit(ammo, max));
        }

        return QvmValue.FromInt32(1);
    }

    private static HumanPlayerConfigItemTask? Find(GameTask task)
    {
        if (task is HumanPlayerConfigItemTask item)
        {
            return item;
        }

        foreach (GameTask child in task.Children)
        {
            if (Find(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
