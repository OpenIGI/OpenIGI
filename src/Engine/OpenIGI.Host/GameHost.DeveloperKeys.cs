using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>Reads the developer keys: captures, overrides, the debug view and the view toggle.</summary>
    private void HandleDeveloperKeys(FrameLoopState state)
    {
        // The camera is advanced on every pass of the loop, not only on the passes that render. The
        // pump clears the mouse's motion accumulator, and the loop pumps several times per rendered
        // frame — so consuming it only on a render threw most of the mouse movement away and made the
        // view lurch. It also runs on real elapsed time: a fixed step tied flying speed to the frame
        // rate, which is not what a person moving a mouse expects.
        // F12 toggles the debug overlay: the player's position and angle, and a crosshair readout of
        // whatever is under it — an object (with its task), a soldier, or nothing.
        if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F12))
        {
            state.ShowDebug = !state.ShowDebug;
        }

        // F11 re-bakes the lighting from scratch, live. It throws away every cached field and the one
        // on screen, then starts tracing again from the same inputs this level loaded with, so the
        // light resolves back in from nothing over the next seconds -- the way to see a change to the
        // bake without reloading the level, and to watch it generate. Ignored where there is nothing to
        // bake (GI off, or the level's scene or cast not yet set up).
        // F8 puts the overrides folder in and out of the frame. Watched as a VARIABLE rather than as
        // a key press, so `r_overrides 0` from the console does the same thing as the key -- the bind
        // sets the variable and this notices, which is one path rather than two that must agree.
        //
        // Nothing happens with an empty folder: the flag is meaningful only against something to
        // compare with, and reloading a level's textures to show the same assets again would be a
        // long hitch for no picture.
        // Any layer with files counts, switched on or not: with every layer off there is still
        // something the switch is the difference between once one comes back on.
        if (_overridesApplied != MaterialCVars.OverridesEnabled.Value && _overrides.HasContent)
        {
            _overridesApplied = MaterialCVars.OverridesEnabled.Value;
            _overrides.Bypassed = !_overridesApplied;

            int swapped = ApplyOverrideChange(out _);

            _logger.Information(
                "Overrides",
                _overridesApplied
                    ? $"Overrides on (F8); {swapped} resource(s) swapped in."
                    : $"Overrides off (F8); {swapped} resource(s) back to the game's own.");
        }

        if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F11)
            && GlobalIlluminationCVars.GlobalIllumination.Value
            && _scene is not null
            && _baked.CanRebake)
        {
            _baked.Rebake(_scene, _loop.TimeSource.Milliseconds);
        }

        // F1 swaps between flying and walking. The player takes the camera's position when it is
        // switched on, so the swap happens where you were looking.
        if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F1) && TitlePlayer is not null)
        {
            ToggleTitlePlayer();
        }
        else if (_window.Input.Keyboard.WasPressed(Input.KeyCode.F1) && HostedPlayer is not null && _camera is not null)
        {
            _playerMode = !_playerMode;
            if (_playerMode)
            {
                if (!HostedPlayer.Place(_mission.Ground, _camera.Position, _camera.Yaw))
                {
                    _playerMode = false;
                    _logger.Warning("Player", "Nothing to stand on here; staying with the free camera.");
                }
                else
                {
                    // Place() drops the player's presentation pair because this is a teleport.
                }
            }
            else
            {
                _camera.MoveTo(HostedPlayer.Camera.Position, HostedPlayer.Body.Yaw, HostedPlayer.Body.Pitch);
            }

            _logger.Information("Player", _playerMode ? "Walking." : "Flying.");
        }
    }
}
