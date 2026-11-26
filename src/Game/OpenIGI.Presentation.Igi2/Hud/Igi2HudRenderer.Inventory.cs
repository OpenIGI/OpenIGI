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
    // leave out the rounds loaded, its width, the key that selects it, and how many of its category it shows side by side. The
    // three grenades share one column, the one in hand at its head. The primary weapons show up to three, as a tactical loadout
    // carries that many.
    private static readonly InventorySlot[] InventorySlots =
    [
        new(3, true, false, 120, "1", Columns: 3),
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

    // The least width of the mark after a slot's columns that counts the weapons of it carried and not shown.
    private const float MoreWidth = 24f;

    // The marks' labels, "+1" on, made once.
    private static readonly string[] MoreLabels = [.. Enumerable.Range(1, 64).Select(static n => string.Create(CultureInfo.InvariantCulture, $"+{n}"))];

    // The weapons the inventory shows this frame, for either display.
    private readonly InventoryStrip _inventoryStrip = new();

    // The categories that share the grenades' column.
    private static bool IsGrenade(int category) => category is 4 or 6 or 7;

    // Whether the inventory is shown, on either display: the profile's option, which the inventory key flips (Config_IsInventoryHidden).
    private static bool InventoryShown(Igi2TitlePlayer title) => title.GameConfig?.Active?.HideInventory != true;

    // A mark's label: how many more are carried.
    private static string MoreLabel(int more) =>
        more <= MoreLabels.Length ? MoreLabels[more - 1] : string.Create(CultureInfo.InvariantCulture, $"+{more}");

    /// <summary>The inventory: <c>HumanPlayer_StatusDisplayHandler</c>.</summary>
    /// <remarks>
    /// <para>
    /// A column for each slot whose category the player carries a weapon of, from five across and five down, each a pixel past the
    /// last. A column's head is a panel 56 high, its key at its top left, the weapon's sprite centred under the key's line and, where
    /// the slot counts them, the rounds carried at its foot on the right (<see cref="RoundsCarried"/>). Under the head goes the
    /// weapon's short name, brighter for the weapon in hand. Each head and each name line lies on a half-dark panel.
    /// </para>
    /// <para>
    /// The grenades are one column: the one in hand at its head, the others' names below it, each on its own line. The primary
    /// weapons are the other way round: each shown has its own column.
    /// </para>
    /// <para>
    /// <b>Not retail</b>, which carries one of each category and shows it: where more are carried than a slot shows (the all-weapons
    /// cheat, a tactical loadout), the slot shows the one in hand, or the one last in hand, and a mark after it as tall as its column
    /// counts the rest (<see cref="InventoryStrip"/>).
    /// </para>
    /// </remarks>
    private void DrawInventory(SpriteBatch batch, Igi2TitlePlayer title)
    {
        if (title.Inventory is not { } inventory || _font is not { } font || !InventoryShown(title))
        {
            return;
        }

        float lineHeight = font.LineHeight;
        Bgra32 panel = Bgra32.FromRgba(0, 0, 0, 128);
        float x = 5f, y = 5f, next = 5f;
        foreach (CarriedSlot carried in _inventoryStrip.Collect(inventory))
        {
            InventorySlot slot = carried.Slot;
            Igi2WeaponItem item = carried.Item;
            if (carried.Head)
            {
                x = next;
                y = 5f;
                next = x + slot.Width + 1f;
                Fill(batch, new RectF(x, 5f, slot.Width, 56f), panel);
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

            Fill(batch, new RectF(x, y, slot.Width, lineHeight), panel);
            Bgra32 colour = carried.Selected ? Bgra32.FromRgba(230, 230, 255, 255) : Bgra32.FromRgba(102, 153, 255, 255);
            Text(batch, WeaponShortName(title, item), x + 1f, y + 1f, colour);
            y += lineHeight + 1f;

            if (carried.More > 0)
            {
                // The mark: as tall as the column it follows, the count centred on it.
                string more = MoreLabel(carried.More);
                float labelWidth = font.Measure(more).Width;
                float moreWidth = MathF.Max(MoreWidth, labelWidth + 6f);
                float tall = 56f + 1f + lineHeight;
                Fill(batch, new RectF(next, 5f, moreWidth, tall), panel);
                Text(batch, more, next + ((moreWidth - labelWidth) * 0.5f), 5f + ((tall - lineHeight) * 0.5f), Blue);
                next += moreWidth + 1f;
            }
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
    /// <param name="Columns">
    /// How many weapons of its category carried it shows, each in a column of its own. <b>Not retail</b> past one, which carries one
    /// of each and shows it.
    /// </param>
    private readonly record struct InventorySlot(int Category, bool CountsRounds, bool LeavesOutLoaded, int Width, string Key, int Columns = 1);

    /// <summary>A weapon the inventory shows.</summary>
    /// <param name="Slot">The slot it is shown in.</param>
    /// <param name="Item">The weapon.</param>
    /// <param name="Head">Whether it heads a column, which begins with it; the grenades after the first go under it by name only.</param>
    /// <param name="Selected">Whether it is the one in hand.</param>
    /// <param name="More">How many more of its slot's category are carried than the slot shows, on the last it shows; else nought.</param>
    private readonly record struct CarriedSlot(InventorySlot Slot, Igi2WeaponItem Item, bool Head, bool Selected, int More);

    /// <summary>
    /// The weapons the inventory shows, slot by slot, for both displays: for each slot, as many weapons of its category carried as
    /// it has columns, in the inventory's order, the grenade in hand heading the grenades' column.
    /// </summary>
    /// <remarks>
    /// Where more are carried than a slot shows, the ones shown are a window on them that moves only as far as it must to take in
    /// the one in hand: so the columns stay put as the weapon in hand changes, step one place as the category's key walks past their
    /// end, and keep the one last in hand while another category's is. The list is the strip's own, refilled each frame, so a frame's
    /// walk allocates nothing.
    /// </remarks>
    private sealed class InventoryStrip
    {
        private readonly List<CarriedSlot> _shown = [];

        // Where each slot's window starts among the weapons of its category carried, kept from frame to frame.
        private readonly int[] _first = new int[InventorySlots.Length];

        /// <summary>Walks the inventory into the weapons shown.</summary>
        /// <param name="inventory">The inventory.</param>
        /// <returns>The weapons shown, slot by slot; the strip's own list, good until the next walk.</returns>
        public List<CarriedSlot> Collect(Igi2Inventory inventory)
        {
            _shown.Clear();
            Igi2WeaponItem? held = inventory.Current;

            // The grenade slot that heads the grenades' column: the one in hand, else the first.
            int swap = GrenadeSlot;
            if (held is not null && IsGrenade(held.WeaponType.WeaponCategory))
            {
                int category = held.WeaponType.WeaponCategory;
                int at = Array.FindIndex(InventorySlots, GrenadeSlot, 3, s => s.Category == category);
                if (at > GrenadeSlot)
                {
                    swap = at;
                }
            }

            IReadOnlyList<Igi2WeaponItem> weapons = inventory.Weapons;
            bool inGrenades = false;
            for (int index = 0; index < InventorySlots.Length; index++)
            {
                int at = index == GrenadeSlot ? swap : index == swap ? GrenadeSlot : index;
                InventorySlot slot = InventorySlots[at];

                // How many of the category are carried, and where among them the one in hand is.
                int carried = 0, heldAt = -1;
                for (int i = 0; i < weapons.Count; i++)
                {
                    Igi2WeaponItem weapon = weapons[i];
                    if (Shows(weapon, slot))
                    {
                        if (ReferenceEquals(weapon, held))
                        {
                            heldAt = carried;
                        }

                        carried++;
                    }
                }

                if (carried == 0)
                {
                    continue;
                }

                int columns = Math.Min(carried, Math.Max(1, slot.Columns));
                int first = Scroll(at, carried, columns, heldAt);
                bool grenade = index is >= GrenadeSlot and < GrenadeSlot + 3;
                int place = 0, shown = 0;
                for (int i = 0; i < weapons.Count && shown < columns; i++)
                {
                    Igi2WeaponItem weapon = weapons[i];
                    if (!Shows(weapon, slot) || place++ < first)
                    {
                        continue;
                    }

                    bool head = !grenade || !inGrenades;
                    inGrenades = grenade;
                    shown++;

                    // The grenades' column lists its kinds by name and carries no mark.
                    int more = shown == columns && !grenade ? carried - columns : 0;
                    _shown.Add(new CarriedSlot(slot, weapon, head, ReferenceEquals(weapon, held), more));
                }
            }

            return _shown;
        }

        private static bool Shows(Igi2WeaponItem weapon, in InventorySlot slot) =>
            weapon.IsActive && weapon.WeaponType.WeaponCategory == slot.Category;

        // Where a slot's window starts this frame: where it did, moved only as far as takes in the one in hand.
        private int Scroll(int at, int carried, int columns, int heldAt)
        {
            int first = Math.Clamp(_first[at], 0, carried - columns);
            if (heldAt >= 0 && heldAt < first)
            {
                first = heldAt;
            }
            else if (heldAt >= first + columns)
            {
                first = heldAt - columns + 1;
            }

            _first[at] = first;
            return first;
        }
    }
}
