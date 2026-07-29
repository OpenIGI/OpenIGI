using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Player;

/// <summary>One weapon a mission starts the player with.</summary>
/// <param name="TaskType">
/// The task class the script named — <c>Gun</c>, or a subclass like <c>GunMP5SD</c>.
/// </param>
/// <param name="WeaponId">The <c>WEAPON_ID_*</c> symbol, which is what resolves against the roster.</param>
/// <param name="Slot">The task's second parameter, whose meaning is unresolved. See <see cref="PlayerLoadout"/>.</param>
public readonly record struct LoadoutWeapon(string TaskType, string WeaponId, int Slot);

/// <summary>One block of ammunition a mission starts the player with.</summary>
/// <param name="AmmoId">The <c>AMMO_ID_*</c> symbol.</param>
/// <param name="Count">How many rounds.</param>
public readonly record struct LoadoutAmmo(string AmmoId, int Count);

/// <summary>
/// What a mission starts the player carrying.
/// </summary>
/// <remarks>
/// <para>
/// Like the spawn point (<see cref="PlayerSpawn"/>), the original computes nothing here: the loadout is
/// <b>declared in the level's own <c>objects.qvm</c></b>, as <b>child tasks of the <c>HumanPlayer</c>
/// task</b>. Level 1 builds them in this order:
/// </para>
/// <code>
/// Task_New(-1, "Binocular", "", "WEAPON_ID_BINOCULARS", 0)
/// Task_New(-1, "GunMP5SD",  "", "WEAPON_ID_MP5SD",      0)
/// Task_New(-1, "Gun",       "", "WEAPON_ID_GLOCK",      1)
/// Task_New(-1, "Gun",       "", "WEAPON_ID_KNIFE",      1)
/// Task_New(-1, "AddAmmo",   "", "AMMO_ID_919",         49)
/// </code>
/// <para>
/// So finding the loadout is finding those children — no heuristic, and the same answer the game uses.
/// <c>Task_DeclareParameters</c> names both weapon arguments itself: <c>ID</c> as an
/// <c>EnumString32</c> and <c>Slot</c> as an <c>Int16</c>.
/// </para>
/// <para>
/// <b>A weapon is recognised by its parameters, not by its type name.</b> The script declares a
/// separate task class per weapon family — <c>Gun</c>, <c>GunMP5SD</c>, <c>GunDRAGUNOV</c>,
/// <c>GunM16A2</c>, <c>Binocular</c> — and the executable confirms why: <c>GunMP5SD</c>'s constructor
/// (<c>0x473AB0</c>) chains to <c>Gun</c>'s and then hardcodes its own <c>WEAPON_ID_MP5SD</c>. Keying
/// on an <c>ID</c> that starts with <c>WEAPON_ID_</c> reads every family, including any this list has
/// not seen.
/// </para>
/// <para>
/// <b><c>Slot</c> is not an inventory index and its meaning is open.</b> Across all fourteen missions it
/// is <b>zero everywhere except level 1</b>, where the Glock and the knife carry 1 and the MP5SD and
/// binoculars carry 0. That is too little variation to name from data, and the parameter application in
/// the executable has not been traced, so it is carried through unread rather than guessed at.
/// </para>
/// </remarks>
public sealed class PlayerLoadout
{
    /// <summary>The prefix a weapon argument's enum symbol carries.</summary>
    public const string WeaponIdPrefix = "WEAPON_ID_";

    /// <summary>The prefix an ammunition argument's enum symbol carries.</summary>
    public const string AmmoIdPrefix = "AMMO_ID_";

    /// <summary>The parameter both weapon and ammunition tasks name their subject with.</summary>
    public const string IdParameter = "ID";

    /// <summary>The weapon tasks' second parameter.</summary>
    public const string SlotParameter = "Slot";

    /// <summary>The ammunition task's count parameter.</summary>
    public const string AmmoParameter = "Ammo";

    private PlayerLoadout(LoadoutWeapon[] weapons, LoadoutAmmo[] ammunition)
    {
        Weapons = weapons;
        Ammunition = ammunition;
    }

    /// <summary>Gets the weapons, in the order the script builds them.</summary>
    /// <remarks>
    /// The order is the script's own and is not the same across missions — level 1 ends on the knife,
    /// level 2 on the MP5SD — so it is preserved rather than sorted.
    /// </remarks>
    public IReadOnlyList<LoadoutWeapon> Weapons { get; }

    /// <summary>Gets the ammunition blocks, in declaration order.</summary>
    /// <remarks>
    /// A mission may declare several, and level 13 declares four. They are kept separate rather than
    /// summed, because each names its own <c>AMMO_ID_*</c>.
    /// </remarks>
    public IReadOnlyList<LoadoutAmmo> Ammunition { get; }

    /// <summary>Reads the loadout a level declares for its player.</summary>
    /// <param name="root">The level's task tree.</param>
    /// <param name="loadout">Receives the loadout when the level declares a player.</param>
    /// <returns><see langword="false"/> when no <c>HumanPlayer</c> task was built.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="root"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The weapon task classes have no implementation in this port, so they load as plain
    /// <see cref="DeclaredTask"/>s carrying their parameters. That is enough to read the loadout — and
    /// it means this works without any of them being registered.
    /// </remarks>
    public static bool TryRead(GameTask root, out PlayerLoadout? loadout)
    {
        ArgumentNullException.ThrowIfNull(root);

        loadout = null;

        GameTask? player = FindPlayer(root);
        if (player is null)
        {
            return false;
        }

        List<LoadoutWeapon> weapons = [];
        List<LoadoutAmmo> ammunition = [];

        foreach (GameTask child in player.Children)
        {
            if (child is not DeclaredTask declared
                || !declared.TryGetValue(IdParameter, out object? raw)
                || raw is not string id)
            {
                continue;
            }

            if (id.StartsWith(WeaponIdPrefix, StringComparison.Ordinal))
            {
                weapons.Add(new LoadoutWeapon(declared.Type.Name, id, Integer(declared, SlotParameter)));
            }
            else if (id.StartsWith(AmmoIdPrefix, StringComparison.Ordinal))
            {
                ammunition.Add(new LoadoutAmmo(id, Integer(declared, AmmoParameter)));
            }
        }

        loadout = new PlayerLoadout([.. weapons], [.. ammunition]);
        return true;
    }

    /// <summary>Whether the loadout carries a given weapon.</summary>
    /// <param name="weaponId">The <c>WEAPON_ID_*</c> symbol.</param>
    /// <returns><see langword="true"/> when it is declared.</returns>
    public bool Has(string weaponId) =>
        Weapons.Any(w => string.Equals(w.WeaponId, weaponId, StringComparison.Ordinal));

    /// <summary>Returns a one-line summary, for logs.</summary>
    /// <returns>The weapons and ammunition.</returns>
    public override string ToString() =>
        Weapons.Count == 0 && Ammunition.Count == 0
            ? "no loadout"
            : string.Join(
                ", ",
                Weapons.Select(w => w.WeaponId[WeaponIdPrefix.Length..])
                    .Concat(Ammunition.Select(a => $"{a.Count}x{a.AmmoId[AmmoIdPrefix.Length..]}")));

    private static int Integer(DeclaredTask task, string parameter) =>
        task.TryGetValue(parameter, out object? value) && value is IConvertible convertible
            ? convertible.ToInt32(null)
            : 0;

    private static GameTask? FindPlayer(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            // The concrete type when the player types are registered, the declared one when they are
            // not - a level's loadout reads either way.
            if (child is HumanPlayerTask
                || (child is DeclaredTask declared
                    && string.Equals(declared.Type.Name, "HumanPlayer", StringComparison.Ordinal)))
            {
                return child;
            }

            GameTask? found = FindPlayer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
