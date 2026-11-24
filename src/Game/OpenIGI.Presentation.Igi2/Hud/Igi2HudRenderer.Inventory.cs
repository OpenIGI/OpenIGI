using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi2.Hud;

/// <content>The weapons carried, across the top left: <c>HumanPlayer_StatusDisplayHandler</c>'s inventory.</content>
public sealed partial class Igi2HudRenderer
{
    // The slots, left to right: the weapon category each shows, whether it shows the rounds carried and, if so, whether those
    // leave out the rounds loaded, its width, and the key that selects it. The three grenades share one column, the one in hand
    // at its head. Each primary weapon carried has a column of its own, as a tactical loadout carries more than one.
    private static readonly InventorySlot[] InventorySlots =
    [
        new(3, true, false, 120, "1", ColumnEach: true),
        new(2, true, false, 72, "2"),
        new(1, false, false, 64, "3"),
        new(4, true, true, 64, "4"),
        new(6, true, true, 64, "4"),
        new(7, true, true, 64, "4"),
        new(9, false, false, 64, "5"),
        new(5, true, true, 64, "6"),
        new(10, false, false, 64, "7"),
        new(8, true, true, 64, "9"),
    ];

    // The first of the grenades' slots.
    private const int GrenadeSlot = 3;

    // The categories that share the grenades' column.
    private static bool IsGrenade(int category) => category is 4 or 6 or 7;

    // Whether the inventory is shown, on either display: the profile's option, which the inventory key flips (Config_IsInventoryHidden).
    private static bool InventoryShown(Igi2TitlePlayer title) => title.GameConfig?.Active?.HideInventory != true;

    /// <summary>The inventory: <c>HumanPlayer_StatusDisplayHandler</c>.</summary>
    /// <remarks>
    /// <para>
    /// A column for each slot whose category the player carries a weapon of, the first carried of it, from five across and five down,
    /// each a pixel past the last. A column's head is a panel 56 high, its key at its top left, the weapon's sprite centred under the key's
    /// line and, where the slot counts them, the rounds carried at its foot on the right (<see cref="RoundsCarried"/>). Under the head
    /// goes the weapon's short name, brighter for the weapon in hand. Each head and each name line lies on a half-dark panel.
    /// </para>
    /// <para>
    /// The grenades are one column: the one in hand at its head, the others' names below it, each on its own line. The primary
    /// weapons are the other way round: each carried has its own column.
    /// </para>
    /// </remarks>
    private void DrawInventory(SpriteBatch batch, Igi2TitlePlayer title)
    {
        if (title.Inventory is not { } inventory || _font is not { } font || !InventoryShown(title))
        {
            return;
        }

        float lineHeight = font.LineHeight;
        float x = 5f, y = 5f;
        int previousWidth = -1;
        foreach (CarriedSlot carried in new CarriedSlots(inventory))
        {
            InventorySlot slot = carried.Slot;
            Igi2WeaponItem item = carried.Item;
            if (carried.Head)
            {
                if (previousWidth >= 0)
                {
                    x += previousWidth + 1;
                }

                y = 5f;
                previousWidth = slot.Width;
                Fill(batch, new RectF(x, 5f, slot.Width, 56f), Bgra32.FromRgba(0, 0, 0, 128));
                Text(batch, slot.Key, x + 1f, y + 1f, Blue);
                Igi2WeaponTypeTask type = item.WeaponType;
                if (_assets?.Get(type.WeaponSprite) is { } sprite)
                {
                    SpriteFrame picture = sprite.Asset.Frames[0];
                    Sprite(batch, type.WeaponSprite, x + (slot.Width * 0.5f) - (picture.Width * 0.5f), y + lineHeight, Bgra32.White);
                }

                if (RoundsCarried(inventory, carried) is { } count)
                {
                    float countWidth = font.Measure(count).Width;
                    Text(batch, count, x + slot.Width - countWidth - 1f, y + 56f - 1f - lineHeight, Blue);
                }

                y += 56f + 1f;
            }

            Fill(batch, new RectF(x, y, slot.Width, lineHeight), Bgra32.FromRgba(0, 0, 0, 128));
            Bgra32 colour = carried.Selected ? Bgra32.FromRgba(230, 230, 255, 255) : Bgra32.FromRgba(102, 153, 255, 255);
            Text(batch, WeaponShortName(title, item), x + 1f, y + 1f, colour);
            y += lineHeight + 1f;
        }
    }

    // A weapon's short name, as the inventory shows it.
    private static string WeaponShortName(Igi2TitlePlayer title, Igi2WeaponItem item) =>
        title.Strings?.WeaponShortName(item.WeaponType.WeaponName) ?? item.WeaponType.WeaponName;

    /// <summary>
    /// The rounds a column's head shows, or <see langword="null"/> where it shows none: those in the pockets and, unless the slot
    /// leaves them out, those loaded; the M16A2's rounds and, after a stroke, its grenades'.
    /// </summary>
    /// <param name="inventory">The inventory.</param>
    /// <param name="carried">The weapon shown.</param>
    /// <returns>The count, or <see langword="null"/>.</returns>
    private static string? RoundsCarried(Igi2Inventory inventory, in CarriedSlot carried)
    {
        if (!carried.Head || !carried.Slot.CountsRounds)
        {
            return null;
        }

        Igi2WeaponItem item = carried.Item;
        Igi2FireTypeRounds rounds = inventory.FireTypeRounds(item, 0);
        return item.WeaponType.WeaponId == "WEAPON_ID_M16A2"
            ? string.Create(CultureInfo.InvariantCulture, $"{rounds.Available + rounds.Loaded}/{inventory.FireTypeRounds(item, 1).Available}")
            : (rounds.Available + (carried.Slot.LeavesOutLoaded ? 0 : rounds.Loaded)).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A column of the inventory.</summary>
    /// <param name="Category">The weapon category it shows.</param>
    /// <param name="CountsRounds">Whether its head shows the rounds carried.</param>
    /// <param name="LeavesOutLoaded">Whether those leave out the rounds loaded.</param>
    /// <param name="Width">Its width.</param>
    /// <param name="Key">The key that selects it.</param>
    /// <param name="ColumnEach">
    /// Whether every weapon of its category carried has a column of its own, in the inventory's order.
    /// <b>Not retail</b>, which carries one of each and shows the first.
    /// </param>
    private readonly record struct InventorySlot(int Category, bool CountsRounds, bool LeavesOutLoaded, int Width, string Key, bool ColumnEach = false);

    /// <summary>A weapon the inventory shows.</summary>
    /// <param name="Slot">The slot it is shown in.</param>
    /// <param name="Item">The weapon.</param>
    /// <param name="Head">Whether it heads a column, which begins with it; the grenades after the first go under it by name only.</param>
    /// <param name="Selected">Whether it is the one in hand.</param>
    private readonly record struct CarriedSlot(InventorySlot Slot, Igi2WeaponItem Item, bool Head, bool Selected);

    /// <summary>
    /// The weapons the inventory shows, slot by slot, for both displays: the first active weapon of each slot's category, the
    /// grenade in hand heading the grenades' column, and in a slot that gives each its own column every weapon of its category
    /// carried, in the inventory's order so the columns stay put as the weapon in hand changes. A struct enumerator, so a frame's
    /// walk allocates nothing.
    /// </summary>
    private struct CarriedSlots
    {
        private readonly Igi2Inventory _inventory;
        private readonly Igi2WeaponItem? _held;

        // The grenade slot that heads the grenades' column: the one in hand, else the first.
        private readonly int _swap;
        private int _index;
        private bool _inGrenades;

        // In a slot that gives each weapon carried a column: whether the walk is in it, and the place reached in the inventory.
        private bool _columnEach;
        private int _columnAt;

        /// <summary>Initializes a new instance of the <see cref="CarriedSlots"/> struct.</summary>
        /// <param name="inventory">The inventory walked.</param>
        public CarriedSlots(Igi2Inventory inventory)
        {
            _inventory = inventory;
            _held = inventory.Current;
            _swap = GrenadeSlot;
            if (_held is not null && IsGrenade(_held.WeaponType.WeaponCategory))
            {
                int category = _held.WeaponType.WeaponCategory;
                int at = Array.FindIndex(InventorySlots, GrenadeSlot, 3, s => s.Category == category);
                if (at > GrenadeSlot)
                {
                    _swap = at;
                }
            }

            _index = -1;
            _inGrenades = false;
            _columnEach = false;
            _columnAt = -1;
            Current = default;
        }

        /// <summary>Gets the weapon walked to.</summary>
        public CarriedSlot Current { get; private set; }

        /// <summary>Gets the walk, for <see langword="foreach"/>.</summary>
        /// <returns>This walk.</returns>
        public readonly CarriedSlots GetEnumerator() => this;

        /// <summary>Walks to the next slot the player carries a weapon for.</summary>
        /// <returns>Whether there was one.</returns>
        public bool MoveNext()
        {
            if (_columnEach && NextCarried(Current.Slot.Category) is { } next)
            {
                Current = new CarriedSlot(Current.Slot, next, true, ReferenceEquals(next, _held));
                return true;
            }

            _columnEach = false;
            while (++_index < InventorySlots.Length)
            {
                int at = _index == GrenadeSlot ? _swap : _index == _swap ? GrenadeSlot : _index;
                InventorySlot slot = InventorySlots[at];
                _columnAt = -1;
                Igi2WeaponItem? item = NextCarried(slot.Category);
                if (item is null)
                {
                    continue;
                }

                _columnEach = slot.ColumnEach;

                bool grenade = _index is >= GrenadeSlot and < GrenadeSlot + 3;
                bool head = !grenade || !_inGrenades;
                _inGrenades = grenade;
                Current = new CarriedSlot(slot, item, head, ReferenceEquals(item, _held));
                return true;
            }

            return false;
        }

        // The next weapon carried of a category after the place reached, in the inventory's order.
        private Igi2WeaponItem? NextCarried(int category)
        {
            IReadOnlyList<Igi2WeaponItem> weapons = _inventory.Weapons;
            while (++_columnAt < weapons.Count)
            {
                Igi2WeaponItem candidate = weapons[_columnAt];
                if (candidate.IsActive && candidate.WeaponType.WeaponCategory == category)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
