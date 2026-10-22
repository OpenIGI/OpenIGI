using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
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
            : FormattableString.Invariant($"mission{mission}");

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

    // Holds the load open until the probe field lands, keeping the window alive throughout.
    //
    // The pump is the whole point. This runs on the thread that owns the GL context, so a plain wait
    // stops the process answering the operating system and the watchdog resets the driver -- which is
    // the fault that put this bake on a worker to begin with, and it exits 0xCFFFFFFF rather than
    // reporting anything. Draining the queue and presenting a cleared frame costs almost nothing and
    // keeps the process demonstrably alive.
    private IrradianceVolume? WaitForLighting(IrradianceProbeBake bake)
    {
        int startedAt = _loop.TimeSource.Milliseconds;

        while (true)
        {
            // Taken here rather than in the loop's condition, because TryCollect answers true exactly
            // once by contract -- asking again after the loop to find out what it handed over would
            // get nothing, and the level would load unlit having just waited for its lighting.
            if (bake.TryCollect(out IrradianceVolume? ready))
            {
                _logger.Information(
                    "Light", $"Waited {_loop.TimeSource.Milliseconds - startedAt} ms for the probe field.");

                return ready;
            }

            if (_window.IsClosing)
            {
                bake.Cancel();
                return null;
            }

            _window.PumpEvents();

            // Cleared to the level's own fog colour rather than to black, so the wait reads as the
            // level arriving rather than as the picture having gone.
            _device.Clear(_scheduler.ClearColour);
            _device.SwapBuffers();

            // The bake is bounded work on a thread pool rather than a poll of something that may never
            // arrive -- but a level whose trace somehow never finishes must not become a game that
            // never starts. Falling through here leaves the blend to cover it.
            if (_loop.TimeSource.Milliseconds - startedAt > LightingWaitMilliseconds)
            {
                _logger.Warning(
                    "Light",
                    $"The probe field did not arrive within {LightingWaitMilliseconds / 1000} s; "
                    + "entering the level unlit and blending it in when it lands.");

                return null;
            }
        }
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
        if (!_flashlight.CanSwitch(menuMode, consoleOpen, _mission.IsPlayerDead) || HostedPlayer is not { } player)
        {
            return;
        }

        player.InputProfile = _configuration.Active;
        if (!player.WasActionPressed(_window.Input, PlayerAction.Flashlight))
        {
            return;
        }

        bool on = _flashlight.Toggle();
        _audioOut.Sounds?.PlayExternal(on ? FlashlightClick.OnSoundName : FlashlightClick.OffSoundName, gain: 0.8f, priority: 180);
    }
}
