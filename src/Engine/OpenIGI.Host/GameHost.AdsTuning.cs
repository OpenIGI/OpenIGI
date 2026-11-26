using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Player.Aim;
using OpenIGI.Input;

namespace OpenIGI.Host;

/// <content>
/// The numpad keys of the live sight tuning (<see cref="AdsLiveTuning"/>), while <c>g_ads_tune</c> is on: a developer's tool
/// for laying each weapon's sights where they look right, and writing them out for the aim table.
/// </content>
public sealed partial class GameHost
{
    // A step of each kind: two millimetres, a centimetre of relief, a hundredth of the arms' tangent, a twentieth of zoom and a
    // quarter of a degree. Shift takes a quarter of one, Ctrl four.
    private const float TuneMove = 0.002f;
    private const float TuneRelief = 0.01f;
    private const float TuneTan = 0.01f;
    private const float TuneZoom = 0.05f;
    private const float TuneTilt = 0.25f;

    /// <summary>Gets where Numpad Enter writes the tuned sights.</summary>
    public static string AdsTuningFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenIGI", "ads-tuning.json");

    private void HandleAdsTuningKeys()
    {
        if (!AdsTuningCVars.Tune.Value)
        {
            return;
        }

        KeyboardState keys = _window.Input.Keyboard;
        float scale = keys.IsDown(KeyCode.LeftShift) || keys.IsDown(KeyCode.RightShift) ? 0.25f
            : keys.IsDown(KeyCode.LeftControl) || keys.IsDown(KeyCode.RightControl) ? 4f
            : 1f;

        bool changed = false;
        void On(KeyCode key, AdsTune change)
        {
            if (keys.WasPressed(key))
            {
                AdsLiveTuning.Nudge(change);
                changed = true;
            }
        }

        On(KeyCode.Numpad4, new AdsTune(-TuneMove * scale, 0f, 0f, 0f, 0f, 0f));
        On(KeyCode.Numpad6, new AdsTune(TuneMove * scale, 0f, 0f, 0f, 0f, 0f));
        On(KeyCode.Numpad8, new AdsTune(0f, TuneMove * scale, 0f, 0f, 0f, 0f));
        On(KeyCode.Numpad2, new AdsTune(0f, -TuneMove * scale, 0f, 0f, 0f, 0f));
        On(KeyCode.Numpad9, new AdsTune(0f, 0f, TuneRelief * scale, 0f, 0f, 0f));
        On(KeyCode.Numpad3, new AdsTune(0f, 0f, -TuneRelief * scale, 0f, 0f, 0f));
        On(KeyCode.Numpad7, new AdsTune(0f, 0f, 0f, TuneTan * scale, 0f, 0f));
        On(KeyCode.Numpad1, new AdsTune(0f, 0f, 0f, -TuneTan * scale, 0f, 0f));
        On(KeyCode.NumpadAdd, new AdsTune(0f, 0f, 0f, 0f, TuneZoom * scale, 0f));
        On(KeyCode.NumpadSubtract, new AdsTune(0f, 0f, 0f, 0f, -TuneZoom * scale, 0f));
        On(KeyCode.NumpadMultiply, new AdsTune(0f, 0f, 0f, 0f, 0f, TuneTilt * scale));
        On(KeyCode.NumpadDivide, new AdsTune(0f, 0f, 0f, 0f, 0f, -TuneTilt * scale));

        if (keys.WasPressed(KeyCode.Numpad0))
        {
            AdsLiveTuning.ResetHeld();
            changed = true;
        }

        // Every change is written at once, so a session's tuning is never lost to a forgotten save; Enter writes it again.
        if (changed || keys.WasPressed(KeyCode.NumpadEnter))
        {
            int written = AdsLiveTuning.Save(AdsTuningFile, AdsProfileTable.Igi1);
            _logger.Warning("Aim", $"Saved {written} tuned weapon(s) to {AdsTuningFile}.");
        }
    }
}
