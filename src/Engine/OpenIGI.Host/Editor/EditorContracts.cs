using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
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
    bool IsPlaying { get; }

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

    /// <summary>Gets the window's input.</summary>
    InputState Input { get; }

    /// <summary>Gets the window width in pixels.</summary>
    float Width { get; }

    /// <summary>Gets the window height in pixels.</summary>
    float Height { get; }

    /// <summary>Gets the interface scale the player chose.</summary>
    float UiScale { get; }

    /// <summary>Gets or sets the system clipboard's text.</summary>
    string ClipboardText { get; set; }

    /// <summary>Sets whether the cursor is locked to the window, as while flying the viewport camera.</summary>
    bool CursorLocked { set; }

    /// <summary>Gets the running game.</summary>
    GameTitle Title { get; }

    /// <summary>Gets the running game's missions, in campaign order.</summary>
    IReadOnlyList<MissionDescriptor> Missions { get; }

    /// <summary>Gets the loaded mission, or <see langword="null"/>.</summary>
    MissionDescriptor? Mission { get; }

    /// <summary>Gets whether a loaded level is still settling behind its loading panel.</summary>
    bool IsWarmingUp { get; }

    /// <summary>Gets the file system the game reads through, with its overlays.</summary>
    VirtualFileSystem Files { get; }

    /// <summary>Gets the free camera the world is drawn from, once a level is loaded.</summary>
    FreeCamera? Camera { get; }

    /// <summary>Gets the last rendered frame's view.</summary>
    EditorView View { get; }

    /// <summary>Gets the world inspector: entities, their positions and screen projections.</summary>
    IWorldInspector Inspector { get; }

    /// <summary>Gets the log.</summary>
    IOpenIgiLogger Logger { get; }

    /// <summary>Loads a mission to edit, replacing any loaded one. Runs between frames.</summary>
    /// <param name="id">The mission id.</param>
    void OpenMission(int id);

    /// <summary>Closes the editor and returns to the main menu. Runs between frames.</summary>
    void Close();
}
