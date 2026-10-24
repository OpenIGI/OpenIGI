using System.Globalization;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The remaster's weapon wheel over the inventory.</content>
public sealed partial class Igi2TitlePlayer
{
    private readonly List<string> _wheelChoices = [];
    private readonly List<Igi2WeaponItem> _wheelItems = [];
    private int _wheelPick = -1;

    /// <inheritdoc/>
    public WeaponWheel Wheel { get; } = new();

    /// <inheritdoc/>
    /// <remarks>
    /// Every weapon that may be put in hand (<see cref="Igi2Inventory.IsSelectable"/>), by its category and then its number, so
    /// the ring keeps the order the number keys' slots run in; each named by its inventory number.
    /// </remarks>
    public (IReadOnlyList<string> Choices, int Current) WheelChoices()
    {
        _wheelChoices.Clear();
        _wheelItems.Clear();
        int current = -1;
        if (Inventory is { } inventory)
        {
            foreach (Igi2WeaponItem item in inventory.Weapons)
            {
                if (inventory.IsSelectable(item))
                {
                    _wheelItems.Add(item);
                }
            }

            _wheelItems.Sort(static (a, b) => a.WeaponType.WeaponCategory != b.WeaponType.WeaponCategory
                ? a.WeaponType.WeaponCategory.CompareTo(b.WeaponType.WeaponCategory)
                : a.Number.CompareTo(b.Number));
            foreach (Igi2WeaponItem item in _wheelItems)
            {
                if (ReferenceEquals(item, inventory.Current))
                {
                    current = _wheelChoices.Count;
                }

                _wheelChoices.Add(item.Number.ToString(CultureInfo.InvariantCulture));
            }
        }

        return (_wheelChoices, current);
    }

    /// <inheritdoc/>
    public void ChooseFromWheel(string choice)
    {
        if (int.TryParse(choice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            _wheelPick = number;
        }
    }

    // The wheel's pick goes into the weapon-select channel as a number key's would (Human_SelectWeaponFromCategory), on the tick
    // after it was taken; a number key pressed on the same tick writes over it.
    private void TakeWheelPick()
    {
        if (_wheelPick >= 0)
        {
            _frame[(int)Igi2HumanChannel.SelectWeapon] = _wheelPick;
            _wheelPick = -1;
        }
    }
}
