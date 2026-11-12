using System.Globalization;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <content>The remaster's weapon wheel over what the player carries.</content>
public sealed partial class PlayerCamera
{
    private readonly List<string> _wheelChoices = [];

    // The weapon the wheel chose, put in the next tick's frame (its scratch slot) so a server is sent the pick; -1 for none.
    private int _wheelChoice = -1;

    /// <inheritdoc/>
    public WeaponWheel Wheel { get; } = new();

    /// <inheritdoc/>
    /// <remarks>
    /// Everything carried in the order it was given, each by its weapon id; the binoculars, taken, are raised to the eye as
    /// their key raises them.
    /// </remarks>
    public (IReadOnlyList<string> Choices, int Current) WheelChoices()
    {
        _wheelChoices.Clear();
        int current = -1;
        if (_weapons is { } weapons)
        {
            int inHand = weapons.InHand is { } held ? held.Weapon.WeaponId : -1;
            foreach (CarriedWeapon carried in weapons.Carried)
            {
                if (carried.Weapon.WeaponId == inHand)
                {
                    current = _wheelChoices.Count;
                }

                _wheelChoices.Add(carried.Weapon.WeaponId.ToString(CultureInfo.InvariantCulture));
            }
        }

        return (_wheelChoices, current);
    }

    /// <inheritdoc/>
    public void ChooseFromWheel(string choice)
    {
        if (_weapons is not null && int.TryParse(choice, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weaponId))
        {
            _wheelChoice = weaponId;
        }
    }
}
