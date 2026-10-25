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
    // at its head.
    private static readonly InventorySlot[] InventorySlots =
    [
        new(3, true, false, 120, "1"),
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

    /// <summary>The inventory: <c>HumanPlayer_StatusDisplayHandler</c>.</summary>
    /// <remarks>
    /// <para>
    /// A column for each slot whose category the player carries a weapon of, the first carried of it, from five across and five down,
    /// each a pixel past the last. A column's head is a panel 56 high, its key at its top left, the weapon's sprite centred under the key's
    /// line and, where the slot counts them, the rounds carried at its foot on the right: the rounds in the pockets and, unless the
    /// slot leaves them out, those loaded; the M16A2's rounds and, after a stroke, its grenades'. Under the head goes the weapon's
    /// short name, brighter for the weapon in hand. Each head and each name line lies on a half-dark panel.
    /// </para>
    /// <para>
    /// The grenades are one column: the one in hand at its head, the others' names below it, each on its own line.
    /// </para>
    /// </remarks>
    private void DrawInventory(SpriteBatch batch, Igi2TitlePlayer title)
    {
        if (title.Inventory is not { } inventory || _font is not { } font)
        {
            return;
        }

        Igi2WeaponItem? held = inventory.Current;
        InventorySlot[] slots = [.. InventorySlots];
        if (held is not null && IsGrenade(held.WeaponType.WeaponCategory))
        {
            // The grenade in hand heads the grenades' column.
            int at = Array.FindIndex(slots, GrenadeSlot, 3, s => s.Category == held.WeaponType.WeaponCategory);
            if (at > GrenadeSlot)
            {
                (slots[GrenadeSlot], slots[at]) = (slots[at], slots[GrenadeSlot]);
            }
        }

        float lineHeight = font.LineHeight;
        float x = 5f, y = 5f;
        int previousWidth = -1;
        bool inGrenades = false;
        for (int i = 0; i < slots.Length; i++)
        {
            InventorySlot slot = slots[i];
            Igi2WeaponItem? item = null;
            foreach (Igi2WeaponItem candidate in inventory.Weapons)
            {
                if (candidate.IsActive && candidate.WeaponType.WeaponCategory == slot.Category)
                {
                    item = candidate;
                    break;
                }
            }

            if (item is null)
            {
                continue;
            }

            bool grenade = i is >= GrenadeSlot and < GrenadeSlot + 3;
            bool head = !grenade || !inGrenades;
            inGrenades = grenade;
            if (head)
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

                if (slot.CountsRounds)
                {
                    Igi2FireTypeRounds rounds = inventory.FireTypeRounds(item, 0);
                    int carried = rounds.Available + (slot.LeavesOutLoaded ? 0 : rounds.Loaded);
                    string count = type.WeaponId == "WEAPON_ID_M16A2"
                        ? string.Create(CultureInfo.InvariantCulture, $"{rounds.Available + rounds.Loaded}/{inventory.FireTypeRounds(item, 1).Available}")
                        : carried.ToString(CultureInfo.InvariantCulture);
                    float countWidth = font.Measure(count).Width;
                    Text(batch, count, x + slot.Width - countWidth - 1f, y + 56f - 1f - lineHeight, Blue);
                }

                y += 56f + 1f;
            }

            string name = title.Strings?.WeaponShortName(item.WeaponType.WeaponName) ?? item.WeaponType.WeaponName;
            Fill(batch, new RectF(x, y, slot.Width, lineHeight), Bgra32.FromRgba(0, 0, 0, 128));
            Bgra32 colour = ReferenceEquals(item, held) ? Bgra32.FromRgba(230, 230, 255, 255) : Bgra32.FromRgba(102, 153, 255, 255);
            Text(batch, name, x + 1f, y + 1f, colour);
            y += lineHeight + 1f;
        }
    }

    private readonly record struct InventorySlot(int Category, bool CountsRounds, bool LeavesOutLoaded, int Width, string Key);
}
