using OpenIGI.Core.Graphics;
using OpenIGI.Frontend;

namespace OpenIGI.Desktop;

/// <summary>
/// The desktop composition root's side of the cinematic shell: it turns the shell's requests into calls
/// on the game loop, the device and the console.
/// </summary>
/// <remarks>
/// The shell is backend-free, so everything it cannot do itself — leaving the menu into a mission,
/// previewing a level behind the menu, capturing a save thumbnail, quitting, persisting settings — is a
/// delegate the composition root supplies here. This is the same pattern as <see cref="DesktopMenuHost"/>'s
/// <c>StartGame</c>/quit hooks, made into a full <see cref="IShellHost"/>.
/// </remarks>
/// <param name="startMission">Starts a mission at a difficulty and leaves the shell.</param>
/// <param name="resumeMission">Closes the pause shell over the current mission.</param>
/// <param name="restartMission">Reloads the current mission from its authored start.</param>
/// <param name="abandonMission">Releases the current mission and returns to the campaign shell.</param>
/// <param name="quit">Closes the game.</param>
/// <param name="previewMission">Shows a mission's level behind the menu, or the default showcase when null.</param>
/// <param name="saveSettings">Persists the current settings.</param>
/// <param name="captureThumbnail">Captures the current frame as save-thumbnail pixels.</param>
public sealed class DesktopShellHost(
    Action<int, int> startMission,
    Action resumeMission,
    Action restartMission,
    Action abandonMission,
    Action quit,
    Action<int?> previewMission,
    Action saveSettings,
    CaptureThumbnail captureThumbnail) : IShellHost
{
    /// <inheritdoc/>
    public void StartMission(int missionId, int difficulty) => startMission(missionId, difficulty);

    /// <inheritdoc/>
    public void ResumeMission() => resumeMission();

    /// <inheritdoc/>
    public void RestartMission() => restartMission();

    /// <inheritdoc/>
    public void AbandonMission() => abandonMission();

    /// <inheritdoc/>
    public void Quit() => quit();

    /// <inheritdoc/>
    public void PreviewMission(int? missionId) => previewMission(missionId);

    /// <inheritdoc/>
    public void SaveSettings() => saveSettings();

    /// <inheritdoc/>
    public Bgra32[] CaptureThumbnail(out int width, out int height) => captureThumbnail(out width, out height);
}

/// <summary>Captures the current frame for a save-slot thumbnail.</summary>
/// <param name="width">Receives the capture width.</param>
/// <param name="height">Receives the capture height.</param>
/// <returns>The captured pixels, or an empty array when no capture is available.</returns>
public delegate Bgra32[] CaptureThumbnail(out int width, out int height);
