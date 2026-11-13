using OpenIGI.Core.Graphics;
using OpenIGI.Frontend;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Menu;

/// <summary>
/// The desktop composition root's side of the cinematic shell: it turns the shell's requests into calls
/// on the game loop, the device and the console.
/// </summary>
/// <remarks>
/// The shell is backend-free, so everything it cannot do itself — leaving the menu into a mission,
/// previewing a level behind the menu, capturing a save thumbnail, quitting, persisting settings — is a
/// delegate the composition root supplies here. This is the same pattern as <c>MenuHost</c>'s
/// <c>StartGame</c>/quit hooks, made into a full <see cref="IShellHost"/>.
/// </remarks>
/// <param name="startMission">Starts a mission at a difficulty and leaves the shell.</param>
/// <param name="setMissionConditions">Sets the hour and weather the campaign is played under.</param>
/// <param name="resumeMission">Closes the pause shell over the current mission.</param>
/// <param name="restartMission">Reloads the current mission from its authored start.</param>
/// <param name="abandonMission">Releases the current mission and returns to the campaign shell.</param>
/// <param name="quit">Closes the game.</param>
/// <param name="switchTitle">Runs a different game; false when it cannot be run.</param>
/// <param name="previewMission">Shows a mission's level behind the menu, or the default showcase when null.</param>
/// <param name="saveSettings">Persists the current settings.</param>
/// <param name="saveProfiles">Persists player profiles and their progress.</param>
/// <param name="captureThumbnail">Captures the current frame as save-thumbnail pixels.</param>
/// <param name="settingUnavailable">Why a setting cannot take effect here, or empty when it can.</param>
/// <param name="missionSaves">The running mission's own save games, or <see langword="null"/>.</param>
/// <param name="inputBindingLabel">The active title's visible control labels.</param>
/// <param name="resetInputBindings">Restores the active title's control defaults.</param>
/// <param name="commandKeys">The keys that run a command, which the Controls page lists after the profile's own.</param>
/// <param name="bindCommandKey">Moves one of those commands onto another key.</param>
/// <param name="displayResolutions">The resolutions the display offers, largest first.</param>
/// <param name="playedMods">The mods that change play the running title is played with.</param>
/// <param name="coop">Co-op through rooms, where the build can make WebRTC connections.</param>
/// <param name="party">Parties, where the build can play together at all.</param>
/// <param name="missionPicture">A mission's picture taken beforehand, or <see langword="null"/> where there is none.</param>
/// <param name="touchScreen">Whether the game is being played on a touch screen.</param>
/// <param name="editTouchLayout">Leaves the pause menu for the touch layout editor, over the mission.</param>
/// <param name="openEditor">Opens the editor, or <see langword="null"/> in a build without one.</param>
public sealed class ShellHost(
    Action<int, int> startMission,
    Action<MissionConditions> setMissionConditions,
    Action resumeMission,
    Action restartMission,
    Action abandonMission,
    Action quit,
    Func<string, bool> switchTitle,
    Action<int?> previewMission,
    Action saveSettings,
    Action saveProfiles,
    CaptureThumbnail captureThumbnail,
    Func<string, string>? settingUnavailable = null,
    Func<OpenIGI.Game.Player.ITitleSaveGames?>? missionSaves = null,
    Func<string, string?>? inputBindingLabel = null,
    Action<OpenIGI.Game.Config.PlayerProfile>? resetInputBindings = null,
    Func<IReadOnlyList<CommandKey>>? commandKeys = null,
    Action<string, OpenIGI.Input.KeyCode>? bindCommandKey = null,
    Func<IReadOnlyList<(int Width, int Height)>>? displayResolutions = null,
    Func<IReadOnlyList<OpenIGI.Core.Content.ModReference>>? playedMods = null,
    ICoopRooms? coop = null,
    Func<bool>? touchScreen = null,
    Action? editTouchLayout = null,
    Func<bool>? openEditor = null,
    IPartyPanel? party = null,
    Func<int, MissionPicture?>? missionPicture = null) : IShellHost
{
    /// <inheritdoc/>
    public ICoopRooms? Coop => coop;

    /// <inheritdoc/>
    public IPartyPanel? Party => party;

    /// <inheritdoc/>
    public IReadOnlyList<OpenIGI.Core.Content.ModReference> PlayedMods => playedMods?.Invoke() ?? [];

    /// <inheritdoc/>
    public OpenIGI.Game.Player.ITitleSaveGames? MissionSaves => missionSaves?.Invoke();

    /// <inheritdoc/>
    public void StartMission(int missionId, int difficulty) => startMission(missionId, difficulty);

    /// <inheritdoc/>
    public void SetMissionConditions(MissionConditions conditions) => setMissionConditions(conditions);

    /// <inheritdoc/>
    public void ResumeMission() => resumeMission();

    /// <inheritdoc/>
    public void RestartMission() => restartMission();

    /// <inheritdoc/>
    public void AbandonMission() => abandonMission();

    /// <inheritdoc/>
    public bool SwitchTitle(string titleId) => switchTitle(titleId);

    /// <inheritdoc/>
    public void Quit() => quit();

    /// <inheritdoc/>
    public void PreviewMission(int? missionId) => previewMission(missionId);

    /// <inheritdoc/>
    public void SaveSettings() => saveSettings();

    /// <inheritdoc/>
    public void SaveProfiles() => saveProfiles();

    /// <inheritdoc/>
    public string? InputBindingLabel(string action) => inputBindingLabel is { } label ? label(action) : action;

    /// <inheritdoc/>
    public void ResetInputBindings(OpenIGI.Game.Config.PlayerProfile profile)
    {
        if (resetInputBindings is { } reset) { reset(profile); }
        else { profile.ResetBindingsToDefaults(); }
    }

    /// <inheritdoc/>
    public IReadOnlyList<CommandKey> CommandKeys => commandKeys?.Invoke() ?? [];

    /// <inheritdoc/>
    public void BindCommandKey(string statement, OpenIGI.Input.KeyCode key) => bindCommandKey?.Invoke(statement, key);

    /// <inheritdoc/>
    public Bgra32[] CaptureThumbnail(out int width, out int height) => captureThumbnail(out width, out height);

    /// <inheritdoc/>
    public string SettingUnavailable(string variable) => settingUnavailable?.Invoke(variable) ?? string.Empty;

    /// <inheritdoc/>
    public IReadOnlyList<(int Width, int Height)> DisplayResolutions() => displayResolutions?.Invoke() ?? [];

    /// <inheritdoc/>
    public MissionPicture? MissionPicture(int missionId) => missionPicture?.Invoke(missionId);

    /// <inheritdoc/>
    public bool TouchScreen => touchScreen?.Invoke() ?? false;

    /// <inheritdoc/>
    public void EditTouchLayout() => editTouchLayout?.Invoke();

    /// <inheritdoc/>
    public bool CanOpenEditor => openEditor is not null;

    /// <inheritdoc/>
    public void OpenEditor() => openEditor?.Invoke();
}

/// <summary>Captures the current frame for a save-slot thumbnail.</summary>
/// <param name="width">Receives the capture width.</param>
/// <param name="height">Receives the capture height.</param>
/// <returns>The captured pixels, or an empty array when no capture is available.</returns>
public delegate Bgra32[] CaptureThumbnail(out int width, out int height);
