using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Input;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The player's three optical machines — the MP5's toggled sight, the Dragunov's held scope and the
/// binoculars — and the per-tick choice of which one answers the alternate-fire action.
/// </summary>
internal sealed class PlayerOptics
{
    private readonly Mp5SightZoom _sightZoom = new();
    private readonly DragunovScope _dragunovScope = new();
    private readonly BinocularsView _binocularsView = new();

    /// <summary>Gets whether the Dragunov's scope is open.</summary>
    internal bool IsSniperScoped => _dragunovScope.IsScoped;

    /// <summary>Gets the binocular overlay's machine.</summary>
    internal BinocularsView Binoculars => _binocularsView;

    /// <summary>Closes all three machines.</summary>
    internal void Reset()
    {
        _binocularsView.Reset();
        _sightZoom.Reset();
        _dragunovScope.Reset();
    }

    /// <summary>Ticks the optics and writes the zoom they answer into both of the view's FOV tangents.</summary>
    /// <param name="weapons">The weapon machine, or <see langword="null"/> before the player has one.</param>
    /// <param name="view">The first-person rig.</param>
    /// <param name="input">The player's input state this tick.</param>
    internal void Tick(PlayerWeapons? weapons, HumanView view, PlayerInputState input)
    {
        // Two machines share the alternate-fire action and the weapon in hand picks which one
        // answers: the MP5's toggled sight, or the Dragunov's held scope. The idle one is still
        // ticked with the current weapon so its own weapon-change reset fires; its answer is a
        // default frame and is discarded.
        WeaponType? heldWeapon = weapons?.InHand?.Weapon;
        bool dragunov = heldWeapon is not null
            && string.Equals(heldWeapon.Weapon.TypeName, DragunovScope.TaskType, StringComparison.Ordinal);

        SightZoomFrame zoom;
        if (dragunov)
        {
            _ = _sightZoom.Tick(heldWeapon, false, view.TanX, view.ViewModelTanX);
            zoom = _dragunovScope.Tick(
                heldWeapon,
                input.Current.IsSet(PlayerInputSlot.AlternateFire),
                input.Previous.IsSet(PlayerInputSlot.AlternateFire),
                input.Current.IsSet(PlayerInputSlot.ZoomIn),
                input.Current.IsSet(PlayerInputSlot.ZoomOut),
                view.TanX,
                view.ViewModelTanX);
        }
        else
        {
            _ = _dragunovScope.Tick(heldWeapon, false, false, false, false, view.TanX, view.ViewModelTanX);
            bool binocular = heldWeapon?.Category == WeaponCategory.Binocular;
            if (!binocular && _binocularsView.HasOpticsState)
            {
                // Binoculars are not ticked once the weapon swap has selected another weapon.
                // Clear their latch and restore the camera before MP5/other sight zoom is applied.
                _binocularsView.Reset();
                view.TanX = HumanView.DefaultTanX;
                view.ViewModelTanX = HumanView.DefaultViewModelTanX;
            }

            zoom = binocular
                ? _binocularsView.Tick(
                    heldWeapon,
                    weapons?.ConsumeBinocularToggle() == true,
                    input.IsSet(PlayerInputSlot.ZoomIn),
                    input.IsSet(PlayerInputSlot.ZoomOut),
                    view.TanX,
                    view.ViewModelTanX)
                : _sightZoom.Tick(
                    heldWeapon,
                    input.IsRisingEdge(PlayerInputSlot.AlternateFire),
                    view.TanX,
                    view.ViewModelTanX);
        }

        view.TanX = zoom.TanX;
        view.ViewModelTanX = zoom.ViewModelTanX;
    }
}
