using OpenIGI.Rendering;

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

    /// <summary>Sets the conditions the campaign is played under, from this mission onwards.</summary>
    /// <param name="conditions">The hour missions open at and the weather they are played in.</param>
    /// <remarks>
    /// Separate from <see cref="StartMission"/> because the choice outlives the mission it was made
    /// beside: it is the campaign's, and the app holds it until the player changes it again. Told as it
    /// is chosen rather than on the way into a level so that the mission previewed behind the menu can
    /// show the hour the player has just asked for.
    /// </remarks>
    void SetMissionConditions(MissionConditions conditions);

    /// <summary>Closes the pause menu and lets the mission underneath run on.</summary>
    /// <remarks>
    /// The pause menu is the shell drawn over a level that is still loaded and simply not being ticked, so
    /// resuming is the app dropping the menu — nothing is reloaded and nothing is placed again.
    /// </remarks>
    void ResumeMission();

    /// <summary>Restarts the mission the pause menu is open over, from its beginning.</summary>
    void RestartMission();

    /// <summary>Abandons the mission the pause menu is open over and returns the shell to the front end.</summary>
    void AbandonMission();

    /// <summary>Closes the game.</summary>
    void Quit();

    /// <summary>Asks the app to run a different game.</summary>
    /// <param name="titleId">The title's id, as <c>GameTitle.Id</c> gives it.</param>
    /// <returns><see langword="false"/> when nothing will happen — it is already running, or not installed.</returns>
    /// <remarks>
    /// The shell says which game the player picked and nothing more. Whether it can be run, when the
    /// swap actually happens and what has to be torn down first are all the app's, and the swap does not
    /// take effect on this call: the app takes it up at a frame boundary, because the shell asking for
    /// it is itself being drawn out of the things a switch replaces.
    /// </remarks>
    bool SwitchTitle(string titleId);

    /// <summary>Asks the app to show a mission's level behind the menu, or none.</summary>
    /// <param name="missionId">The mission to preview, or <see langword="null"/> to show the default showcase.</param>
    /// <remarks>
    /// The app streams the level on a worker thread and moves the showcase camera to it; the shell only
    /// says which mission the player is looking at. Called as the campaign selection changes.
    /// </remarks>
    void PreviewMission(int? missionId);

    /// <summary>Persists the current settings (the CVar archive), e.g. on leaving the settings screen.</summary>
    void SaveSettings();

    /// <summary>Persists player profiles and their profile-bound progress immediately.</summary>
    void SaveProfiles();

    /// <summary>Gets the running mission's own save games, for a title that keeps them, or <see langword="null"/>.</summary>
    OpenIGI.Game.Player.ITitleSaveGames? MissionSaves => null;

    /// <summary>Captures the current frame for a save-slot thumbnail.</summary>
    /// <param name="width">Receives the capture width.</param>
    /// <param name="height">Receives the capture height.</param>
    /// <returns>The captured pixels, or an empty array when no capture is available.</returns>
    OpenIGI.Core.Graphics.Bgra32[] CaptureThumbnail(out int width, out int height);

    /// <summary>Says why a setting cannot take effect on this machine, or nothing when it can.</summary>
    /// <param name="variable">The console variable the setting's row drives.</param>
    /// <returns>The reason, for the row's tooltip; empty when the setting works here.</returns>
    /// <remarks>
    /// For the settings that depend on hardware or an optional download -- NVIDIA's features -- and for the
    /// ones another setting takes over, such as the render scale while DLSS chooses the render size. The
    /// row stays visible and is greyed out, so the player learns the option exists and what it needs.
    /// </remarks>
    string SettingUnavailable(string variable) => string.Empty;
}
