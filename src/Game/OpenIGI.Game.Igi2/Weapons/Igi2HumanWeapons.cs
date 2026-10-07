using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>How a human comes to carry weapons: its inventory, and the script that fills it.</summary>
/// <remarks>
/// <para>
/// Every human makes its inventory when it is made, limited by the player tuning's ammunition limits
/// (<c>Human_CreateHandler</c>, <c>igi2 0x4FDC90</c>). Once the level is loaded, a human whose <c>Weapon</c> parameter holds
/// script text has it evaluated with three natives pointed at that inventory and released after
/// (<c>Human_UpdateInternalDataAfterCarryoverHandler</c>, <c>0x4FF440</c>): <c>Human_AddWeapon</c>, <c>Human_AddAmmo</c> and
/// <c>Human_AddAmmoForWeapon</c>, registered by <c>Human_Open</c> (<c>0x504710</c>).
/// </para>
/// <para>
/// The text is evaluated as a single expression (<see cref="Igi2LineScript"/>), so a script that ends its first call with
/// <c>;</c> adds only that one weapon.
/// </para>
/// </remarks>
public sealed class Igi2HumanWeapons
{
    /// <summary>The native that starts a human carrying a weapon, with a load of its ammunition.</summary>
    public const string AddWeaponNative = "Human_AddWeapon";

    /// <summary>The native that adds rounds of an ammunition.</summary>
    public const string AddAmmoNative = "Human_AddAmmo";

    /// <summary>The native that adds rounds of whatever ammunition a weapon's fire type uses.</summary>
    public const string AddAmmoForWeaponNative = "Human_AddAmmoForWeapon";

    private static readonly string[] Natives = [AddWeaponNative, AddAmmoNative, AddAmmoForWeaponNative];

    private readonly NativeRegistry _natives;
    private readonly Igi2ScriptFunctions _functions;
    private readonly Igi2WeaponTables _tables;

    /// <summary>Initializes a new instance of the <see cref="Igi2HumanWeapons"/> class and registers its natives.</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="functions">The natives script text may call.</param>
    /// <param name="tables">The session's weapons and ammunition.</param>
    public Igi2HumanWeapons(NativeRegistry natives, Igi2ScriptFunctions functions, Igi2WeaponTables tables)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(tables);
        _natives = natives;
        _functions = functions;
        _tables = tables;

        FireTaskTypes = new Igi2FireTaskTypes(
            TaskTypeConstant(natives, "TASKTYPE_GUN"),
            TaskTypeConstant(natives, "TASKTYPE_ZOOM"),
            TaskTypeConstant(natives, "TASKTYPE_MISSILE"));

        functions.Register(natives, AddWeaponNative, AddWeapon, 1);
        functions.Register(natives, AddAmmoNative, AddAmmo, 2);
        functions.Register(natives, AddAmmoForWeaponNative, AddAmmoForWeapon, 3);
    }

    /// <summary>Gets the task-type ids a held weapon is tested against.</summary>
    public Igi2FireTaskTypes FireTaskTypes { get; }

    /// <summary>Gets the session's weapons and ammunition.</summary>
    public Igi2WeaponTables Tables => _tables;

    /// <summary>Makes a human's inventory, carrying nothing, within the ammunition limits.</summary>
    /// <param name="ammoLimits">The player tuning's limits.</param>
    /// <returns>The inventory.</returns>
    public Igi2Inventory CreateInventory(IEnumerable<Igi2AmmoLimit> ammoLimits)
    {
        Igi2Inventory inventory = new(_tables);
        inventory.ApplyAmmoLimits(ammoLimits);
        return inventory;
    }

    /// <summary>Evaluates a human's <c>Weapon</c> script against its inventory.</summary>
    /// <param name="inventory">The inventory the natives fill.</param>
    /// <param name="script">The script text; nothing happens for none.</param>
    public void RunWeaponScript(Igi2Inventory inventory, string? script)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (string.IsNullOrEmpty(script))
        {
            return;
        }

        foreach (string name in Natives)
        {
            _natives.SetSymbolContext(name, inventory);
        }

        try
        {
            Igi2LineScript.Evaluate(script, _natives, _functions);
        }
        finally
        {
            foreach (string name in Natives)
            {
                _natives.SetSymbolContext(name, null);
            }
        }
    }

    private static int TaskTypeConstant(NativeRegistry natives, string name) =>
        natives.TryResolve(name, out QvmSymbol? symbol) ? symbol.Read().ToInt32() : Igi2WeaponConstants.NoTaskType;

    /// <summary><c>Human_AddWeapon(weaponId)</c>: carries the weapon with a load of ammunition (<c>Human_AddWeaponCB</c>, <c>igi2 0x4FB740</c>).</summary>
    private static QvmValue AddWeapon(Igi2ScriptCall call)
    {
        if (call.Context is Igi2Inventory inventory)
        {
            string weaponId = call.GetText(0);
            if (weaponId.Length > 0)
            {
                inventory.EnableWeaponType(weaponId, addAmmo: true);
            }
        }

        return QvmValue.FromInt32(1);
    }

    /// <summary><c>Human_AddAmmo(ammoId, rounds)</c> (<c>Human_AddAmmoCB</c>, <c>igi2 0x4FB800</c>): answers 0 to any other argument count, 2 otherwise.</summary>
    private static QvmValue AddAmmo(Igi2ScriptCall call)
    {
        if (call.Context is not Igi2Inventory inventory)
        {
            return QvmValue.FromInt32(2);
        }

        if (call.Count != 2)
        {
            return QvmValue.Zero;
        }

        inventory.AddAmmo(call.GetText(0), call.GetInt32(1));
        return QvmValue.FromInt32(2);
    }

    /// <summary>
    /// <c>Human_AddAmmoForWeapon(weaponId, rounds[, slot])</c> (<c>Human_AddAmmoForWeaponCB</c>, <c>igi2 0x4FB8D0</c>): the
    /// slot is the primary unless a third argument above zero names the secondary, and the weapon is found by evaluating
    /// its name as a constant.
    /// </summary>
    private QvmValue AddAmmoForWeapon(Igi2ScriptCall call)
    {
        if (call.Count >= 2 && call.Context is Igi2Inventory inventory)
        {
            string weaponId = call.GetText(0);
            int rounds = call.GetInt32(1);
            int slot = call.Count > 2 && call.GetInt32(2) > 0 ? 1 : 0;

            if (_tables.Weapon(Igi2LineScript.Evaluate(weaponId, _natives, _functions)) is { } weaponType
                && weaponType.AmmoInfo(slot, _tables).AmmoType is { } ammoType)
            {
                inventory.AddAmmo(ammoType.AmmoId, rounds);
            }
        }

        return QvmValue.Zero;
    }
}

/// <summary>The task-type ids a held weapon is tested against, as their <c>TASKTYPE_*</c> constants give them.</summary>
/// <param name="Gun">A gun, <c>Gun_eQTaskType</c>.</param>
/// <param name="Zoom">A zoom, <c>Zoom_eQTaskType</c>.</param>
/// <param name="Missile">A missile, <c>Missile_eQTaskType</c>.</param>
public readonly record struct Igi2FireTaskTypes(int Gun, int Zoom, int Missile);
