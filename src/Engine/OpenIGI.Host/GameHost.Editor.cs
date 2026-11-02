using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Host.Editor;
using OpenIGI.Input;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host;

/// <content>
/// The editor's place in the host: opened and closed at frame boundaries like a change of title, given the
/// frame's input while it is open, drawn over the world, and the world projected into its viewport.
/// </content>
public sealed partial class GameHost
{
    private IEditorSession? _editor;
    private bool _editorOpenWanted;
    private bool _editorCloseWanted;
    private int? _editorMissionWanted;
    private int _editorLastMilliseconds;
    private EditorView _editorView;
    private readonly EditorPlacementMotion _editorMotion = new();

    /// <summary>Gets or sets what opens editor sessions; <see langword="null"/> in builds without the editor.</summary>
    /// <remarks>Set by the desktop composition root before <see cref="Run"/>.</remarks>
    public IEditorFactory? EditorFactory { get; set; }

    /// <summary>Gets whether the editor is open.</summary>
    public bool IsEditorOpen => _editor is not null;

    /// <summary>Gets whether the editor is holding the level still for editing.</summary>
    private bool EditorHoldsLevel => _editor is { IsPlaying: false };

    /// <summary>Asks for the editor to open at the next frame boundary.</summary>
    /// <returns><see langword="false"/> when this build has no editor.</returns>
    public bool RequestEditor()
    {
        if (EditorFactory is null)
        {
            _logger.Warning("Editor", "This build has no editor.");
            return false;
        }

        _editorOpenWanted = true;
        return true;
    }

    /// <summary>Registers <c>editor</c>, which opens the editor, optionally on a mission: <c>editor 3</c>.</summary>
    /// <param name="console">The console to register with.</param>
    private void RegisterEditorCommands(ConsoleSystem console)
    {
        console.Commands.Add(
            "editor",
            "Opens the mission editor; 'editor <mission>' opens it on that mission, 'editor close' closes it.",
            context =>
            {
                string argument = context.Arguments.Count > 0 ? context.Arguments[0] : string.Empty;
                if (argument.Equals("close", StringComparison.OrdinalIgnoreCase))
                {
                    _editorCloseWanted = true;
                    return;
                }

                if (!RequestEditor())
                {
                    context.Print("This build has no editor.");
                    return;
                }

                if (int.TryParse(argument, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mission))
                {
                    _editorMissionWanted = mission;
                }
            });
    }

    /// <summary>Opens, closes, or loads a mission for, the editor. Call between frames.</summary>
    /// <param name="state">The frame loop's state.</param>
    private void ApplyEditorRequests(FrameLoopState state)
    {
        if (_editorCloseWanted)
        {
            _editorCloseWanted = false;
            CloseEditor(state);
        }

        if (_editorOpenWanted)
        {
            _editorOpenWanted = false;
            OpenEditor(state);
        }

        if (_editorMissionWanted is { } id && _editor is not null)
        {
            _editorMissionWanted = null;

            // A reload of the same mission keeps the view: the editor reloads to show what it saved, and a camera
            // thrown back to the level's start each time would make every save a hunt for where one was working.
            (Vector3d Position, float Yaw, float Pitch)? pose = _session.Mission?.Id == id && _camera is { } kept
                ? (kept.Position, kept.Yaw, kept.Pitch)
                : null;
            CancelMissionWarmUp();
            _editorMotion.Clear();
            StartMission(id);
            _playerMode = false;
            _window.CursorVisible = true;
            if (pose is { } at)
            {
                _camera?.MoveTo(at.Position, at.Yaw, at.Pitch);
            }
        }
    }

    private void OpenEditor(FrameLoopState state)
    {
        if (_editor is not null || EditorFactory is not { } factory)
        {
            return;
        }

        _logger.Information("Editor", "Opening the editor.");
        state.Shell?.ClosePauseMenu();
        _menuFlow.MenuMode = false;
        _menuFlow.PauseMenu = false;
        _playerMode = false;
        _window.CursorVisible = true;
        _window.CursorLocked = false;
        _editorLastMilliseconds = _loop.TimeSource.Milliseconds;
        _editor = factory.Open(new EditorHostServices(this));
    }

    private void CloseEditor(FrameLoopState state)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        _logger.Information("Editor", "Closing the editor.");
        _editor = null;
        editor.Dispose();
        if (_world is { } world && ReferenceEquals(world.Motions, _editorMotion))
        {
            world.Motions = _editorMotion.Inner;
        }

        _editorMotion.Clear();
        _editorMotion.Inner = null;

        // Back to the front end, as abandoning a mission is: the level the editor had open is released.
        CancelMissionWarmUp();
        ReleasePlayedMission();
        state.Shell?.OpenOn("main");
        _menuFlow.MenuMode = true;
        _menuFlow.ShowcasePending = true;
        _window.CursorLocked = false;
        _window.CursorVisible = true;
    }

    /// <summary>Gives the editor the frame. Returns whether it is open and owns the frame's input.</summary>
    /// <param name="state">The frame loop's state.</param>
    /// <returns>Whether the editor is open.</returns>
    private bool UpdateEditor(FrameLoopState state)
    {
        if (_editor is not { } editor)
        {
            return false;
        }

        // The editor's offsets sit over the title's own motions. A level load hands the renderer the title's again,
        // so the layer is put back on top whenever it is found missing.
        if (_world is { } world && !ReferenceEquals(world.Motions, _editorMotion))
        {
            _editorMotion.Inner = world.Motions;
            world.Motions = _editorMotion;
        }

        int now = _loop.TimeSource.Milliseconds;
        float elapsed = Math.Clamp((now - _editorLastMilliseconds) / 1000f, 0f, 0.1f);
        _editorLastMilliseconds = now;

        // The console keeps the keyboard while it is open, as it does over the shell.
        editor.Frame(elapsed, hasInput: !state.Console.IsOpen);
        DrainPressQueue(state, executeBinds: false);
        if (!state.Console.IsOpen)
        {
            _window.Input.Captured = true;
        }

        return true;
    }

    /// <summary>Draws the editor over the frame, under the console.</summary>
    /// <param name="state">The frame loop's state.</param>
    private void DrawEditor(FrameLoopState state)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        _device.BeginStageTiming(RenderStage.UserInterface);
        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        UiPainter painter = UiPainter.Create(
            state.Batch,
            new LayoutContext(MathF.Max(1f, _window.Width), MathF.Max(1f, _window.Height), new UiVec2(MathF.Max(1f, _window.Width), MathF.Max(1f, _window.Height))));
        state.Batch.Begin();
        editor.Draw(painter);
        state.Batch.End();
        _device.EndStageTiming();
    }

    /// <summary>Narrows the frame's lens to the editor viewport's shape, so the world is not stretched into it.</summary>
    /// <param name="aspectRatio">The aspect the frame would use; replaced by the viewport's.</param>
    /// <param name="verticalFieldOfView">The vertical field of view; replaced to keep the horizontal one.</param>
    private void ApplyEditorLens(ref float aspectRatio, ref float verticalFieldOfView)
    {
        if (EditorViewportRect() is not { } port)
        {
            return;
        }

        aspectRatio = port.Width / port.Height;
        verticalFieldOfView = VerticalFieldOfView(aspectRatio, HumanView.DefaultTanX);
    }

    /// <summary>
    /// Maps a projection for the viewport's shape onto the viewport's place in the window: a scale and offset in
    /// clip space, so the whole window is still one render target and the world lands exactly in the panel.
    /// </summary>
    /// <param name="projection">The projection built for the viewport's aspect.</param>
    /// <returns>The projection for the window.</returns>
    private Matrix4f ApplyEditorViewport(Matrix4f projection)
    {
        if (EditorViewportRect() is not { } port)
        {
            return projection;
        }

        float width = MathF.Max(1f, _window.Width);
        float height = MathF.Max(1f, _window.Height);
        float scaleX = port.Width / width;
        float scaleY = port.Height / height;
        float offsetX = (2f * (port.X + (port.Width * 0.5f)) / width) - 1f;
        float offsetY = 1f - (2f * (port.Y + (port.Height * 0.5f)) / height);
        Matrix4f map = new(
            scaleX, 0f, 0f, offsetX,
            0f, scaleY, 0f, offsetY,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f);
        return map * projection;
    }

    /// <summary>Records the frame's view for the editor's picking and gizmos.</summary>
    /// <param name="eye">The eye the frame is drawn from.</param>
    /// <param name="unjitteredProjection">The frame's projection without the temporal jitter.</param>
    private void NoteEditorView(Camera3D eye, Matrix4f unjitteredProjection)
    {
        if (_editor is null)
        {
            return;
        }

        // Relative to the eye, as the renderer draws: at forty kilometres from the origin a float cannot carry the
        // camera's own position through a view matrix without wobbling the gizmos.
        Matrix4f view = new Camera3D(default, eye.Orientation).GetViewMatrix();
        _editorView = new EditorView(eye.Position, unjitteredProjection * view, _window.Width, _window.Height);
    }

    private UiRect? EditorViewportRect() =>
        _editor?.Viewport is { Width: > 1f, Height: > 1f } port ? port : null;

    /// <summary>The editor's view of the host.</summary>
    private sealed class EditorHostServices(GameHost host) : IEditorHost
    {
        public IGraphicsDevice Device => host._device;

        public InputState Input => host._window.Input;

        public float Width => host._window.Width;

        public float Height => host._window.Height;

        public float UiScale => Core.Graphics.UiScale.Resolve(host._options.UiScale, host._window.Width, host._window.Height);

        public string ClipboardText
        {
            get => host._window.ClipboardText;
            set => host._window.ClipboardText = value;
        }

        public bool CursorLocked
        {
            set => host._window.CursorLocked = value;
        }

        public GameTitle Title => host.ActiveTitle;

        public IReadOnlyList<MissionDescriptor> Missions => [.. host._missions.InCampaignOrder()];

        public MissionDescriptor? Mission => host._session.Mission;

        public bool IsWarmingUp => host.IsWarmingUp;

        public VirtualFileSystem Files => host._fileSystem;

        public FreeCamera? Camera => host._camera;

        public EditorView View => host._editorView;

        public IWorldInspector Inspector => host._inspector;

        public IOpenIgiLogger Logger => host._logger;

        public void OpenMission(int id) => host._editorMissionWanted = id;

        public bool TryReadFile(string path, out ReadOnlyMemory<byte> data) => host._resources.TryLoad(path, out data);

        public void MountContent(string id, string name, string root)
        {
            host._overrides.Mount(new Engine.Resources.OverrideLayer(id, $"{name} (editor)", root));
            host.ApplyOverrideChange(out _);
        }

        public void UnmountContent(string id)
        {
            if (host._overrides.Unmount(id, out _))
            {
                host.ApplyOverrideChange(out _);
            }
        }

        public IReadOnlyList<WorldPlacement> Placements => host._scene?.Placements ?? [];

        public void SetPlacementOffset(GameTask owner, Vector3d offsetMetres) => host._editorMotion.SetOffset(owner, offsetMetres);

        public void ReloadMission()
        {
            if (host._session.Mission is { } mission)
            {
                host._editorMissionWanted = mission.Id;
            }
        }

        public void Close() => host._editorCloseWanted = true;
    }
}
