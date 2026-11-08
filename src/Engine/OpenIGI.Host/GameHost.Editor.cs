using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
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
    private bool _editorMissionCloseWanted;

    // The player's graphics settings while the editor holds the Vanilla preset, to put back.
    private GraphicsPresetSnapshot? _editorPresetBefore;
    private string? _editorTitleWanted;
    private List<string[]>? _editorAfterSwitch;
    private bool _editorPlayWanted;
    private readonly List<string[]> _editorPendingCommands = [];
    private int _editorLastMilliseconds;
    private EditorView _editorView;
    private readonly EditorPlacementMotion _editorMotion = new();
    private readonly EditorPlacementVisibility _editorVisibility = new();
    private int _editorSpawned;

    /// <summary>Gets or sets what opens editor sessions; <see langword="null"/> in builds without the editor.</summary>
    /// <remarks>Set by the desktop composition root before <see cref="Run"/>.</remarks>
    public IEditorFactory? EditorFactory { get; set; }

    /// <summary>Gets whether the editor is open.</summary>
    public bool IsEditorOpen => _editor is not null;

    /// <summary>Gets whether the editor is holding the level still for editing.</summary>
    private bool EditorHoldsLevel => _editor is { IsPlaying: false };

    /// <summary>Gets the cut-scene frame the player is shown: none while the editor holds the level, whose view is its own.</summary>
    private CutsceneFrame? CutsceneFrameShown => EditorHoldsLevel ? null : _mission.CutsceneFrameShown;

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

                if (_editor is { } open && argument.Length > 0 && !int.TryParse(argument, out _))
                {
                    context.Print(open.RunCommand(context.Arguments) ?? $"Unknown editor command '{argument}'.");
                    return;
                }

                if (argument.Length > 0 && !int.TryParse(argument, out _))
                {
                    // Given before the editor has opened (a launch's +editor select ...): run, in order, once it has.
                    _editorPendingCommands.Add([.. context.Arguments]);
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

        // Another game: the editor closes with the level, the session switches at the next boundary, and the editor comes
        // back in the new game (ReopenEditorAfterSwitch).
        if (_editorTitleWanted is { } title)
        {
            _editorTitleWanted = null;
            CloseEditor(state);
            if (!RequestTitle(title))
            {
                _editorAfterSwitch = null;
            }
        }

        if (_editorMissionCloseWanted)
        {
            // The level goes, the editor stays, with nothing open until a mission is opened again.
            _editorMissionCloseWanted = false;
            _editorMissionWanted = null;
            CancelMissionWarmUp();
            _editorMotion.Clear();
            ReleasePlayedMission();
            _playerMode = false;
            _window.CursorVisible = true;
            _window.CursorLocked = false;
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
            bool play = _editorPlayWanted;
            _editorPlayWanted = false;
            StartMission(id);

            // Play stands the player up where the mission starts and hands over the window, as a played mission
            // does; editing keeps the free camera and the pointer.
            _playerMode = play;
            _window.CursorVisible = !play;
            _window.CursorLocked = false;
            if (pose is { } at)
            {
                _camera?.MoveTo(at.Position, at.Yaw, at.Pitch);
            }
        }
    }

    // After a switch the editor asked for: opened again, with the commands it left to run.
    private void ReopenEditorAfterSwitch()
    {
        if (_editorAfterSwitch is not { } commands)
        {
            return;
        }

        _editorAfterSwitch = null;
        _editorPendingCommands.AddRange(commands);
        RequestEditor();
    }

    // The editor shows the level as the original game draws it, whatever the player's preset: what is built in it is meant to
    // run there, and the remastered look would hide what the retail renderer will show. The player's settings are put back
    // when the editor closes, and before they are saved, so the editor never changes them.
    private void UseEditorPreset()
    {
        GraphicsPresetRegistry presets = GraphicsPresetRegistry.Default;
        if (_editorPresetBefore is not null || !presets.IsAllowed(GraphicsPreset.Vanilla))
        {
            return;
        }

        _editorPresetBefore = presets.Capture();
        presets.Apply(GraphicsPreset.Vanilla);
        _logger.Information("Editor", "The viewport draws with the Vanilla preset while the editor is open.");
    }

    private void RestorePlayerPreset()
    {
        if (_editorPresetBefore is { } before)
        {
            _editorPresetBefore = null;
            GraphicsPresetRegistry.Default.Restore(before);
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
        UseEditorPreset();
        _editor = factory.Open(new EditorHostServices(this));
        foreach (string[] pending in _editorPendingCommands)
        {
            _logger.Information("Editor", $"editor {string.Join(' ', pending)}: {_editor.RunCommand(pending)}");
        }

        _editorPendingCommands.Clear();
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
        RestorePlayerPreset();
        if (_world is { } world && ReferenceEquals(world.Motions, _editorMotion))
        {
            world.Motions = _editorMotion.Inner;
        }

        _editorMotion.Clear();
        _editorMotion.Inner = null;
        if (_world is { } culled && ReferenceEquals(culled.Visibility, _editorVisibility))
        {
            culled.Visibility = _editorVisibility.Inner;
        }

        _editorVisibility.Clear();
        _editorVisibility.Inner = null;

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

        // Playing: the game has the frame. Escape is the way back, taken before the game's own Escape could open the
        // pause menu over a level the editor is about to reload. Not while a cutscene plays: there Escape is held to skip
        // it, as it opens no pause menu either, and the press that starts the hold must not end the test.
        if (editor.IsPlaying)
        {
            bool cutscene = _mission.Cutscenes?.IsPlaying == true || TitlePlayer is { IsCutscenePlaying: true };
            if (!state.Console.IsOpen && !cutscene && _window.Input.Keyboard.WasPressed(Input.KeyCode.Escape))
            {
                editor.StopPlaying();
                _window.Input.Captured = true;
                return true;
            }

            return false;
        }

        // The editor's offsets sit over the title's own motions. A level load hands the renderer the title's again,
        // so the layer is put back on top whenever it is found missing.
        if (_world is { } world && !ReferenceEquals(world.Motions, _editorMotion))
        {
            _editorMotion.Inner = world.Motions;
            world.Motions = _editorMotion;
        }

        // And its hidden and isolated objects over the title's culling, the same way.
        if (_world is { } culled && !ReferenceEquals(culled.Visibility, _editorVisibility))
        {
            _editorVisibility.Inner = culled.Visibility;
            culled.Visibility = _editorVisibility;
        }

        int now = _loop.TimeSource.Milliseconds;
        float elapsed = Math.Clamp((now - _editorLastMilliseconds) / 1000f, 0f, 0.1f);
        _editorLastMilliseconds = now;

        // The console keeps the keyboard while it is open, as it does over the shell. A fault in the editor closes the
        // editor, not the game: the player's session and whatever the editor saved are worth more than its frame.
        try
        {
            editor.Frame(elapsed, hasInput: !state.Console.IsOpen);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            EditorFailed(state, error);
            return false;
        }

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
        try
        {
            editor.Draw(painter);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            EditorFailed(state, error);
        }

        state.Batch.End();
        _device.EndStageTiming();
    }

    private void EditorFailed(FrameLoopState state, Exception error)
    {
        _logger.Error("Editor", $"The editor failed and was closed: {error}");
        _editorCloseWanted = true;
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
        verticalFieldOfView = VerticalFieldOfView(aspectRatio, WidescreenLens.HorizontalTangent(HumanView.DefaultTanX, aspectRatio));
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
        private Scripting.Vocabulary.ScriptVocabulary? _vocabulary;
        private Core.Titles.GameTitle? _vocabularyTitle;

        public IGraphicsDevice Device => host._device;

        public InputState Input => host._window.Input;

        public OpenIGI.Audio.IAudioDevice Audio => host._audioOut.Device;

        public float Width => host._window.Width;

        public float Height => host._window.Height;

        public float UiScale => Core.Graphics.UiScale.Desktop(EditorCVars.UiScale.Value, host._window.Height);

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

        public IReadOnlyList<GameTitle> InstalledTitles =>
            [.. host._titles.Titles.Where(t => host._titles.TryResolve(t, out Titles.TitleBinding? binding) && binding.IsInstalled)];

        public void SwitchTitle(string titleId, IReadOnlyList<string[]> editorCommands)
        {
            host._editorTitleWanted = titleId;
            host._editorAfterSwitch = [.. editorCommands];
        }

        public IReadOnlyList<MissionDescriptor> Missions => [.. host._missions.InCampaignOrder()];

        public MissionDescriptor? Mission => host._session.Mission;

        public string MissionName(MissionDescriptor mission) => host._menuStrings?.Get(mission.Name) ?? mission.Name;

        public bool IsWarmingUp => host.IsWarmingUp;

        public VirtualFileSystem Files => host._fileSystem;

        public FreeCamera? Camera => host._camera;

        public Game.World.IGroundHeightField? Ground => host._mission.Ground;

        public bool? HasModel(string model) => host._mission.Models is { } models ? models(model) is not null : null;

        public double? SurfaceBelow(Vector3d pointMetres, double reachMetres)
        {
            Game.World.LevelCollision collision = host._mission.Collision;
            double units = Game.World.WorldScene.UnitsPerMetre;
            Vector3d from = pointMetres * units;
            Vector3d to = new(from.X, from.Y, from.Z - (reachMetres * units));
            return !collision.IsEmpty && collision.Cast(from, to, out Game.World.WorldRayHit hit) ? hit.Position.Z / units : null;
        }

        public Engine.Tasks.ScriptVarRegistry ScriptVariables => host._loop.Variables;

        public IReadOnlyCollection<string> ScriptNames => host._loop.Natives.Names;

        private OpenIGI.Game.World.MissionStringResolver? _missionText;

        public string? MissionText(string key)
        {
            // Built on first use from the mounted resources, so it reads the open mission's text as the game does.
            _missionText ??= new OpenIGI.Game.World.MissionStringResolver(host._resources, new OpenIGI.Game.World.MenuStringTable(host._resources));
            return !string.IsNullOrWhiteSpace(key) && _missionText.TryGet(key, out string text) && text.Length > 0 ? text : null;
        }

        public Scripting.Vocabulary.ScriptVocabulary Vocabulary
        {
            get
            {
                // The title can switch under a live editor (New Mission for the other game), so the cache is per title.
                if (_vocabulary is null || !Equals(_vocabularyTitle, host._titleRuntime.Title))
                {
                    _vocabulary = host._titleRuntime.CreateScriptVocabulary();
                    _vocabularyTitle = host._titleRuntime.Title;
                }

                return _vocabulary;
            }
        }

        public bool TryReadModelTexture(string model, int materialSlot, out ReadOnlyMemory<byte> data)
        {
            data = ReadOnlyMemory<byte>.Empty;
            return host._mission.Models?.Invoke(model) is { } bound
                && bound.Pack.TryGetTextureName(bound.Instance, materialSlot, out string texture)
                && host._resources.TryLoad(Game.World.LoadedPack.TextureResourceName(texture), out data);
        }

        public Game.World.AnimationFilePreview? PreviewAnimation(ReadOnlyMemory<byte> file)
        {
            try
            {
                return host._mission.AnimationFiles?.Invoke(file);
            }
            catch (Exception error) when (error is ArgumentException or InvalidDataException or Formats.Iff.IffFormatException or Formats.Ilff.IlffFormatException)
            {
                // Bytes that claim to be an animation but are not one are shown as bytes.
                return null;
            }
        }

        public Game.World.IModelAnimator? AnimateModel(Formats.World.MefModel model, string? name = null)
        {
            try
            {
                return host._mission.Animators?.Invoke(model, name);
            }
            catch (ArgumentException)
            {
                // A rig the level's clips do not fit is shown still.
                return null;
            }
        }

        public Game.World.IEditableTerrain? Terrain => host._terrain?.Editable;

        public EditorView View => host._editorView;

        public IWorldInspector Inspector => host._inspector;

        public IOpenIgiLogger Logger => host._logger;

        public void OpenMission(int id) => host._editorMissionWanted = id;

        public void CloseMission() => host._editorMissionCloseWanted = true;

        public void RescanMissions() => host._missions.Scan(host._fileSystem, host._resources, host._loop.Natives);

        public bool TryReadFile(string path, out ReadOnlyMemory<byte> data) => host._resources.TryLoad(path, out data);

        public bool TryReadShippedFile(string path, out ReadOnlyMemory<byte> data)
        {
            data = default;
            int colon = path.IndexOf(':', StringComparison.Ordinal);
            string device = colon > 0 ? path[..colon] : Engine.Vfs.LocalFileDevice.DeviceName;
            string relative = path[(colon + 1)..].Replace('\\', '/').TrimStart('/');
            string project = Path.Combine(EditorProjects.DefaultFiles(host._titleRuntime.Title.Id), relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(project))
            {
                data = File.ReadAllBytes(project);
                return true;
            }

            if (!host._fileSystem.TryGetDevice(device, out Engine.Vfs.IFileDevice? files) || !files.TryOpenRead(relative, out Stream? stream) || stream is null)
            {
                return false;
            }

            using (stream)
            {
                using MemoryStream copy = new();
                stream.CopyTo(copy);
                data = copy.ToArray();
                return true;
            }
        }

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

        public void SetPlacementHidden(Game.World.PlacementKey owner, bool hidden) => host._editorVisibility.SetHidden(owner, hidden);

        public void SetPlacementIsolation(IReadOnlyCollection<Game.World.PlacementKey>? owners) => host._editorVisibility.SetIsolated(owners);

        public Game.World.PlacementKey? SpawnPlacementCopy(Game.World.PlacementKey source)
        {
            if (host._scene is not { } scene)
            {
                return null;
            }

            // Copies carry a level id no level authors (below -1000), so their key is their own and no title motion claims
            // them; their placement objects are new, so the renderer prepares only them.
            int id = -1000 - (++host._editorSpawned);
            List<Game.World.WorldPlacement> copies = [];
            foreach (Game.World.WorldPlacement placement in scene.Placements)
            {
                if (Game.World.PlacementKey.Of(placement) == source)
                {
                    copies.Add(placement with { LevelId = id });
                }
            }

            if (copies.Count == 0)
            {
                return null;
            }

            host._scene = scene.WithPlacements([.. scene.Placements, .. copies]);
            host._logger.Information("Editor", $"Drew a copy of {copies[0].Owner.Label}: {copies.Count} placement(s), {copies[0].Instance.Name}.");
            return new Game.World.PlacementKey(copies[0].Owner, id);
        }

        public Game.World.PlacementKey? SpawnPlacement(string model, Vector3d positionMetres, Matrix3f orientation)
        {
            if (host._scene is not { } scene || host._session.LevelRoot is not { } root || host._mission.Models?.Invoke(model) is not { } bound)
            {
                return null;
            }

            int id = -1000 - (++host._editorSpawned);
            Game.World.WorldPlacement placement = new(positionMetres, orientation, bound.Instance, bound.Pack, bound.Model, $"editor:{model}", root)
            {
                LevelId = id,
            };
            host._scene = scene.WithPlacements([.. scene.Placements, placement]);
            host._logger.Information("Editor", $"Drew {model}, which the level did not place.");
            return new Game.World.PlacementKey(root, id);
        }

        public void SetPlacementPose(Game.World.PlacementKey owner, EditorPose pose) => host._editorMotion.SetPose(owner, pose);

        public void ReloadMission(bool play = false)
        {
            if (host._session.Mission is { } mission)
            {
                host._editorMissionWanted = mission.Id;
                host._editorPlayWanted = play;
            }
        }

        public void Close() => host._editorCloseWanted = true;
    }
}
