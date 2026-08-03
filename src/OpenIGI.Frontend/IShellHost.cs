namespace OpenIGI.Frontend;

/// <summary>
/// What the shell asks of the application it runs inside: starting a mission, quitting, previewing a
/// mission behind the menu, and persisting settings.
/// </summary>
/// <remarks>
/// The shell is backend-free and holds no window, device or game loop, so everything that needs those
/// goes through this seam, which the desktop composition root implements. It is the same shape as the
/// existing menu host's <c>StartGame</c>/quit hooks, widened for the cinematic shell's needs.
/// </remarks>
public interface IShellHost
{
    /// <summary>Leaves the shell and starts a mission at a difficulty.</summary>
    /// <param name="missionId">The mission to start.</param>
    /// <param name="difficulty">The difficulty index to start at.</param>
    void StartMission(int missionId, int difficulty);

    /// <summary>Closes the game.</summary>
    void Quit();

    /// <summary>Asks the app to show a mission's level behind the menu, or none.</summary>
    /// <param name="missionId">The mission to preview, or <see langword="null"/> to show the default showcase.</param>
    /// <remarks>
    /// The app streams the level on a worker thread and moves the showcase camera to it; the shell only
    /// says which mission the player is looking at. Called as the campaign selection changes.
    /// </remarks>
    void PreviewMission(int? missionId);

    /// <summary>Persists the current settings (the CVar archive), e.g. on leaving the settings screen.</summary>
    void SaveSettings();

    /// <summary>Captures the current frame for a save-slot thumbnail.</summary>
    /// <param name="width">Receives the capture width.</param>
    /// <param name="height">Receives the capture height.</param>
    /// <returns>The captured pixels, or an empty array when no capture is available.</returns>
    OpenIGI.Core.Graphics.Bgra32[] CaptureThumbnail(out int width, out int height);
}
