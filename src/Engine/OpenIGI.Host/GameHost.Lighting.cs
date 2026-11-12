using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Config;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    // The mission's authored grade, or the one r_lut_table names instead. Loaded per level rather than
    // per frame -- it is a volume texture upload, while how much of it to apply is a number.
    // A mission with no table of its own is not a fault: it renders ungraded, which is what every
    // mission did before this existed.
    private void ApplyColourLookup(int mission)
    {
        string named = PostCVars.ColourLookupTable.Value.Trim();
        string name = named.Length > 0
            ? named
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"mission{mission}");

        // Anchored to the assets directory and stripped of any path, so r_lut_table cannot be pointed
        // at an arbitrary file on the machine from a settings file or a +statement.
        string file = Path.Combine(
            AppContext.BaseDirectory, "Assets", "luts", Path.GetFileName(name) + ".cube");

        ColourLookupTable? table = ColourLookupTable.Load(file, _logger);

        if (table is null && named.Length > 0)
        {
            _logger.Warning("Grade", $"No colour table called '{named}' in Assets/luts; the frame is ungraded.");
        }

        _device.SetColourLookup(table);
    }

    /// <summary>Reads the Flashlight action and flips the switch, with its click.</summary>
    /// <param name="menuMode">Whether a menu holds the input.</param>
    /// <param name="consoleOpen">Whether the console does.</param>
    /// <remarks>
    /// Read per frame beside the map computer's key rather than on the thirty-hertz tick, so a quick tap is
    /// never missed. Only while the light could be seen -- in the player's own view, alive, with nothing else
    /// holding the picture -- so the key cannot switch it on unseen behind a cutscene.
    /// </remarks>
    private void UpdateFlashlightSwitch(bool menuMode, bool consoleOpen)
    {
        // Any standing player's, unless its title's own keys take the action over for something of its own.
        bool titleTakesIt = Gameplay is { } titleGameplay
            && (TitleInputBindings.ReplacedActions(titleGameplay.InputBindings) & PlayerAction.Flashlight) != 0;
        if (!_flashlight.CanSwitch(menuMode, consoleOpen, _mission.IsPlayerDead) || !PlayerStands || titleTakesIt
            || !PlayerProfileInputBindings.WasPressed(_configuration.Active, _window.Input, PlayerInputBindings.ConfigName(PlayerAction.Flashlight)))
        {
            return;
        }

        bool on = _flashlight.Toggle();
        _audioOut.Sounds?.PlayExternal(on ? FlashlightClick.OnSoundName : FlashlightClick.OffSoundName, gain: 0.8f, priority: 180);
    }
}
