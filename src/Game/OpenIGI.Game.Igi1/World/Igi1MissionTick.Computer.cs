using OpenIGI.Game.Computer;
using OpenIGI.Game.Config;
using OpenIGI.Game.Igi1.Computer;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.World;

/// <content>
/// The computer the player carries: opened from the weapon's idle state by the map key (<c>0x40E9C0</c>), run each tick while
/// the game goes on behind it, closed by the key again (<c>0x40E3C0</c>).
/// </content>
public sealed partial class Igi1MissionTick
{
    /// <summary>The smallest and largest the profile's mouse sensitivity is taken as.</summary>
    private const float MinimumSensitivity = 0.05f;

    private const float MaximumSensitivity = 1f;

    // The weapon machine has gone into state 11 on the map key: the body crouches into state 45 and the computer comes up.
    private void OpenMapComputer(PlayerCamera player)
    {
        if (!host.MissionComputer || mission.Computer is not { } computer)
        {
            return;
        }

        if (computer.IsOpen && !host.ComputerOpen)
        {
            // Closed under it, by the player's death or the level's end.
            computer.Close();
        }

        if (player.Weapons?.State != HumanWeaponState.MapComputer || player.IsMapComputerOpen)
        {
            return;
        }

        computer.MapEnabled = host.MapComputerEnabled;
        player.SetMapComputerOpen(true);
        (int width, int height) = host.ComputerDisplay;
        computer.Open(mission.PlayerTask, width, height);
        host.TakeComputerInput();
        host.ComputerChanged(true);
    }

    // State 45's run, then the menu over it.
    private void TickMapComputer(PlayerCamera player)
    {
        if (!host.MissionComputer || mission.Computer is not { IsOpen: true } computer)
        {
            player.TickMapComputer();
            return;
        }

        computer.MapEnabled = host.MapComputerEnabled;
        if (player.TickMapComputer(host.ViewInput))
        {
            computer.Close();
            player.SetMapComputerOpen(false);
            host.ComputerChanged(false);
            return;
        }

        computer.Run(ComputerInput(host.TakeComputerInput(), host.InputProfile), paused: false);
    }

    // 0x45E060's first two channels: the mouse's counts over 255 at the profile's sensitivity, the second inverted when the
    // profile says so.
    private static Igi1ComputerInput ComputerInput(in ComputerPointerInput mouse, PlayerProfile? profile)
    {
        float sensitivity = Math.Clamp(profile?.MouseSensitivity ?? MouseLookSettings.DefaultSensitivity, MinimumSensitivity, MaximumSensitivity);
        bool invert = profile?.InvertMouse ?? false;
        float x = (float)(sensitivity * (mouse.CountsX * (1.0 / 255.0)));
        float y = (float)(sensitivity * (mouse.CountsY * (1.0 / 255.0)));
        return new Igi1ComputerInput(x, invert ? -y : y, invert, mouse.Buttons);
    }
}
