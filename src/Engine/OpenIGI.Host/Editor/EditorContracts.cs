using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host.Editor;

/// <summary>
/// Opens editor sessions. Supplied by the desktop composition root only, so the browser and server builds never
/// link the editor: with no factory the host simply has no editor.
/// </summary>
public interface IEditorFactory
{
    /// <summary>Opens a session over the running host.</summary>
    /// <param name="host">What the session may ask of the host.</param>
    /// <returns>The session; the host disposes it when the editor closes.</returns>
    IEditorSession Open(IEditorHost host);
}

/// <summary>
/// One open editor: it owns the frame's input and draws over the world while it is open, and is disposed — every
/// panel, document and GPU resource with it — when it closes.
/// </summary>
public interface IEditorSession : IDisposable
{
    /// <summary>Gets where in the window the world is shown, or <see langword="null"/> when no world is.</summary>
    /// <remarks>The host projects the world so that it fills this rectangle exactly, undistorted.</remarks>
    UiRect? Viewport { get; }

    /// <summary>Gets whether the level is running (play in editor) rather than held still for editing.</summary>
    /// <remarks>While playing, the game has the whole window and the input; the session only draws its banner.</remarks>
    bool IsPlaying { get; }

    /// <summary>Runs an editor command typed at the console after <c>editor</c>, such as <c>select Crate</c>.</summary>
    /// <param name="arguments">The words after <c>editor</c>.</param>
    /// <returns>What to print, or <see langword="null"/> when the session does not know the command.</returns>
    /// <remarks>A command given before the mission has loaded waits for it, so a launch can name what to select.</remarks>
    string? RunCommand(IReadOnlyList<string> arguments);

    /// <summary>Ends play and goes back to editing; the host calls it when the player presses Escape.</summary>
    void StopPlaying();

    /// <summary>Lays out the interface and handles the frame's input.</summary>
    /// <param name="elapsedSeconds">The real time since the last frame.</param>
    /// <param name="hasInput">Whether the session may read input this frame; false while the console has it.</param>
    void Frame(float elapsedSeconds, bool hasInput);

    /// <summary>Draws the interface over the frame.</summary>
    /// <param name="painter">The painter, over a 2D pass at one unit per pixel.</param>
    void Draw(UiPainter painter);
}

/// <summary>The last rendered frame's view, for picking and for drawing gizmos in the world.</summary>
/// <param name="Eye">The camera position in metres.</param>
/// <param name="ViewProjection">The unjittered world-to-clip transform, for the whole window.</param>
/// <param name="Width">The window width the transform maps to.</param>
/// <param name="Height">The window height.</param>
public readonly record struct EditorView(Vector3d Eye, Matrix4f ViewProjection, float Width, float Height);

/// <summary>What an editor session may ask of the host.</summary>
public interface IEditorHost
{
    /// <summary>Gets the graphics device, for fonts and textures.</summary>
    IGraphicsDevice Device { get; }

    /// <summary>Gets the audio device, for previewing sounds.</summary>
    IAudioDevice Audio { get; }

    /// <summary>Gets the window's input.</summary>
    InputState Input { get; }

    /// <summary>Gets the window width in pixels.</summary>
    float Width { get; }

    /// <summary>Gets the window height in pixels.</summary>
    float Height { get; }

    /// <summary>Gets the size the editor draws at: the <c>ed_ui_scale</c> setting, or one picked from the screen.</summary>
    float UiScale { get; }

    /// <summary>Gets or sets the system clipboard's text.</summary>
    string ClipboardText { get; set; }

    /// <summary>Sets whether the cursor is locked to the window, as while flying the viewport camera.</summary>
    bool CursorLocked { set; }

    /// <summary>Gets the running game.</summary>
    GameTitle Title { get; }

    /// <summary>Gets the running game's missions, in campaign order.</summary>
    IReadOnlyList<MissionDescriptor> Missions { get; }

    /// <summary>Gets a mission's name as the menus show it, in the player's language.</summary>
    /// <param name="mission">The mission.</param>
    /// <returns>The name.</returns>
    string MissionName(MissionDescriptor mission);

    /// <summary>Gets the loaded mission, or <see langword="null"/>.</summary>
    MissionDescriptor? Mission { get; }

    /// <summary>Gets whether a loaded level is still settling behind its loading panel.</summary>
    bool IsWarmingUp { get; }

    /// <summary>Gets the file system the game reads through, with its overlays.</summary>
    VirtualFileSystem Files { get; }

    /// <summary>Gets the free camera the world is drawn from, once a level is loaded.</summary>
    FreeCamera? Camera { get; }

    /// <summary>Gets the open mission's ground as height grids, when it is one the editor can change.</summary>
    Game.World.IEditableTerrain? Terrain { get; }

    /// <summary>Gets the last rendered frame's view.</summary>
    EditorView View { get; }

    /// <summary>Gets the world inspector: entities, their positions and screen projections.</summary>
    IWorldInspector Inspector { get; }

    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Reads a game file exactly as the game would: through the content layers, the editor's project first.</summary>
    /// <param name="path">The path as the game names it: a device or alias, a colon, and the path under it.</param>
    /// <param name="data">Receives the bytes.</param>
    /// <returns><see langword="false"/> when no layer and no archive has the file.</returns>
    bool TryReadFile(string path, out ReadOnlyMemory<byte> data);

    /// <summary>Puts a folder on top of the content layers, so what is saved into it is what the game loads.</summary>
    /// <param name="id">The layer's id; mounting the same id again rescans it.</param>
    /// <param name="name">The name the <c>content</c> command lists it under.</param>
    /// <param name="root">The folder, mirroring the installation.</param>
    void MountContent(string id, string name, string root);

    /// <summary>Takes a folder mounted by <see cref="MountContent"/> off the content layers.</summary>
    /// <param name="id">The layer's id.</param>
    void UnmountContent(string id);

    /// <summary>Gets what the loaded level placed in the world, each with the task that owns it.</summary>
    IReadOnlyList<WorldPlacement> Placements { get; }

    /// <summary>Draws everything a task placed moved and turned, from the next frame, without rebuilding the level.</summary>
    /// <param name="owner">What the task placed, as its placements are keyed (<see cref="PlacementKey.Of"/>).</param>
    /// <param name="pose">How far from where it was built; an identity pose puts it back.</param>
    void SetPlacementPose(PlacementKey owner, EditorPose pose);

    /// <summary>Loads the open mission again from the files as they now stand, keeping the camera where it is.</summary>
    /// <param name="play">Whether to start it as the player (play in editor) rather than held still for editing.</param>
    void ReloadMission(bool play = false);

    /// <summary>Loads a mission to edit, replacing any loaded one. Runs between frames.</summary>
    /// <param name="id">The mission id.</param>
    void OpenMission(int id);

    /// <summary>Closes the editor and returns to the main menu. Runs between frames.</summary>
    void Close();
}
