using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Configuration;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Editor.Panels;
using OpenIGI.Editor.Terrain;
using OpenIGI.Editor.Viewport;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Docking;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Editor;

/// <summary>Opens the editor inside the game.</summary>
public sealed class EditorFactory : IEditorFactory
{
    /// <summary>
    /// Gets the system's folder picker, given what it asks and the folder it opens on, answering the folder chosen or null when
    /// cancelled; null for a build without one, where exports go to the editor's own folder.
    /// </summary>
    public Func<string, string?, Task<string?>>? PickFolder { get; init; }

    /// <inheritdoc/>
    public IEditorSession Open(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new EditorSession(host) { PickFolder = PickFolder };
    }
}

/// <summary>
/// One open editor: the window chrome (menus, toolbar, status bar), the docked panels, the actions and their
/// shortcuts, and the workspace they all edit.
/// </summary>
internal sealed partial class EditorSession : IEditorSession
{
    private const string DefaultLayout = "H(0.2:T(outliner*),0.58:V(0.7:T(viewport*|script),0.3:T(content*|problems|terrain)),0.22:V(0.65:T(inspector*),0.35:T(history*|preview)))";

    private readonly IEditorHost _host;
    private readonly EditorFonts _fonts;
    private readonly UiSurfaces _surfaces;
    private readonly EditorTheme _theme;
    private readonly EditorRoot _root;
    private readonly MissionWorkspace _workspace;
    private readonly DockSpace _dock;
    private readonly ViewportPanel _viewport;
    private readonly TerrainTool _terrainTool;
    private readonly TerrainPanel _terrainPanel;
    private readonly OutlinerPanel _outliner;
    private readonly MenuBar _menu;
    private readonly StatusBar _status;
    private readonly LiveWorldLink _live;
    private readonly ContentPanel _content;
    private readonly ProblemsPanel _problems;
    private readonly GraphPanel _logic;
    private bool _disposed;

    public EditorSession(IEditorHost host)
    {
        _host = host;
        _fonts = new EditorFonts(host.Device);
        _surfaces = UiSurfaces.Create(host.Device);
        _theme = new EditorTheme(_fonts, _surfaces, host.UiScale);
        _root = new EditorRoot(_theme) { Clipboard = new HostClipboard(host) };
        _root.Actions.Failed = (action, error) =>
        {
            host.Logger.Error("Editor", $"{action.Title} failed.", error);
            Notify($"{action.Title} failed: {error.Message}", isError: true);
        };
        // One project per game for now, beside the user's settings; it is mounted over the game while the editor is
        // open, so what is saved is what the level reloads from.
        EditorProject project = EditorProject.OpenOrCreate(
            EditorProjects.DefaultRoot(host.Title.Id),
            host.Title.Id,
            $"{host.Title.DisplayName} project");
        host.MountContent(project.LayerId, project.Name, project.FilesDirectory);
        _workspace = new MissionWorkspace(host, project);

        _live = new LiveWorldLink(host, _workspace);
        TerrainBrush brush = new();
        _terrainTool = new TerrainTool(host, _workspace, brush, _live, message => Notify(message));
        _viewport = new ViewportPanel(_theme, host, _workspace, _live, _terrainTool);
        _outliner = new OutlinerPanel(_theme, _workspace, task => _live.IsHidden(task), task => _live.SetHidden([task], hidden: !_live.IsHidden(task)));
        _viewport.DuplicateRequested += () => Duplicate(nudge: false);
        MissionGraphs.PlacedFrame = _live.PlacedFrame;
        _outliner.Activated += _viewport.Frame;
        _workspace.Selection.Changed += () =>
        {
            if (_workspace.Selection.Primary is { } primary)
            {
                _outliner.Reveal(primary);
            }
        };

        _dock = new DockSpace(_theme);
        _dock.Register(new DockPanel("viewport", "Viewport", _viewport) { Icon = KitIcon.Camera, CanClose = false, IsTransparent = true });
        _dock.Register(new DockPanel("outliner", "Outliner", _outliner) { Icon = KitIcon.Hierarchy });
        _dock.Register(new DockPanel("inspector", "Inspector", new InspectorPanel(_theme, _workspace, host)) { Icon = KitIcon.Gear });
        _dock.Register(new DockPanel("history", "History", new HistoryPanel(_theme, _workspace)) { Icon = KitIcon.History });
        ScriptPanel script = new(_theme, host, _workspace);
        _dock.Register(new DockPanel("script", "Script", script) { Icon = KitIcon.Script });
        _content = new ContentPanel(_theme, host, path =>
        {
            script.OpenFile(path);
            _dock.Show("script");
        })
        {
            ExportsDirectory = Path.Combine(_workspace.Project.Root, "exports"),
            WriteToProject = (gamePath, bytes) => _workspace.WriteToProject(gamePath, bytes),
        };
        _content.Reported += Notify;
        _dock.Register(new DockPanel("content", "Content", _content) { Icon = KitIcon.Folder });
        _dock.Register(new DockPanel("preview", "Preview", _content.PreviewPanel) { Icon = KitIcon.Search });

        // Config scripts (a weapon, an ammunition type) as forms, beside the script they can always be opened as.
        ConfigPanel config = new(_theme, host, _workspace, path =>
        {
            script.OpenFile(path);
            _dock.Show("script");
        });
        _dock.Register(new DockPanel("config", "Config Editor", config) { Icon = KitIcon.Document });
        _content.OpenConfig = path =>
        {
            if (config.Open(path))
            {
                _dock.Show("config");
            }
            else
            {
                Notify($"{Path.GetFileName(path)} is not a config the form can edit.", isError: true);
            }
        };
        _problems = new ProblemsPanel(_theme, _workspace, new EditorMissionFacts(host, _workspace));
        _problems.GoTo += _viewport.Frame;
        _dock.Register(new DockPanel("problems", "Problems", _problems) { Icon = KitIcon.Search });
        _logic = new GraphPanel(_theme, host, _workspace, () => _problems.Issues, (message, isError) => Notify(message, isError));
        _dock.Register(new DockPanel("logic", "Logic", _logic) { Icon = KitIcon.Graph });
        _terrainPanel = new TerrainPanel(_theme, host, brush);
        _dock.Register(new DockPanel("terrain", "Terrain", _terrainPanel) { Icon = KitIcon.Terrain });
        _dock.Register(new DockPanel("preferences", "Preferences", new PreferencesPanel(_theme, _root.Actions, host, Notify)) { Icon = KitIcon.Gear });
        string? saved = ReadSavedLayout();
        if (!_dock.LoadLayout(saved ?? DefaultLayout))
        {
            _dock.LoadLayout(DefaultLayout);
        }
        else if (saved is not null && !saved.Contains("preview", StringComparison.Ordinal))
        {
            // A layout saved before the preview had a panel of its own: it joins History's tabs, History still in front.
            _dock.Show("preview", beside: "history");
            _dock.Show("history");
        }

        _dock.LayoutChanged += SaveLayout;

        RegisterActions();
        _root.Actions.LoadBindings(EditorCVars.KeyBindings.Value);
        _menu = BuildMenu();
        _status = new StatusBar(_theme);
        Grid shell = new();
        shell.RowDefinitions.Add(GridLength.Auto);
        shell.RowDefinitions.Add(GridLength.Auto);
        shell.RowDefinitions.Add(GridLength.Star);
        shell.RowDefinitions.Add(GridLength.Auto);
        shell.ColumnDefinitions.Add(GridLength.Star);
        shell.Add(_menu, 0, 0);
        shell.Add(BuildToolbar(), 1, 0);
        shell.Add(_dock, 2, 0);
        shell.Add(_status, 3, 0);
        _root.Content = shell;
        _viewport.Overlay = BuildViewportToolbar();
    }

    /// <inheritdoc/>
    public UiRect? Viewport =>
        !IsPlaying && _dock.GroupOf("viewport")?.ActivePanel?.Content == _viewport && _workspace.Mission is not null ? _viewport.WorldRect : null;

    /// <inheritdoc/>
    public bool IsPlaying { get; private set; }

    /// <inheritdoc/>
    public void StopPlaying()
    {
        if (IsPlaying)
        {
            IsPlaying = false;
            _host.ReloadMission(play: false);
            Notify("Back to editing.");
        }
    }

    /// <inheritdoc/>
    public void Frame(float elapsedSeconds, bool hasInput)
    {
        TakePickedExports();

        // The screen or the setting can change while the editor is open: a switch to full screen, ed_ui_scale typed in.
        _theme.Scale = _host.UiScale;
        if (EditorCVars.LightTheme.Value != _theme.IsLight)
        {
            if (EditorCVars.LightTheme.Value)
            {
                _theme.ApplyLight();
            }
            else
            {
                _theme.ApplyDark();
            }
        }

        _workspace.Sync();
        _live.Update();
        if (!_pendingPick && _workspace.Document is not null && _host.View.Width > 0f
            && _pendingSteps.TryPeek(out Func<bool>? step) && step())
        {
            _pendingSteps.Dequeue();
        }

        // A second after the view is up and linked, so the camera has settled where the level puts it.
        _pickReadyFrames = _pendingPick && _host.View.Width > 0f && !_host.IsWarmingUp && _live.LinkedCount > 0 ? _pickReadyFrames + 1 : 0;
        if (_pickReadyFrames > 60)
        {
            _pickReadyFrames = 0;
            _pendingPick = false;
            _viewport.PickAtCentre();
            Notify(_workspace.Selection.Primary is { } picked ? $"Picked {TaskPresentation.Name(picked)}." : "Nothing under the middle of the view.");
        }

        _noticeSeconds -= elapsedSeconds;
        _menu.Caption = _workspace.Mission is not null ? $"{_workspace.MissionName}{(_workspace.History.IsDirty ? "  • modified" : string.Empty)}" : _host.Title.DisplayName;
        UpdateStatus();
        if (hasInput)
        {
            _root.Frame(_host.Input, _host.Width, _host.Height, elapsedSeconds);
            _viewport.UpdateCamera(elapsedSeconds);
            _terrainTool.Tick(elapsedSeconds);
        }
        else
        {
            _root.Layout(_host.Width, _host.Height);
            _root.Update(elapsedSeconds);
        }
    }

    /// <inheritdoc/>
    public void Draw(UiPainter painter)
    {
        if (IsPlaying)
        {
            DrawPlayingBanner(painter);
            return;
        }

        // The system pointer stays visible over the editor, as in any desktop tool, so no drawn cursor goes on top. Its
        // small round marks (pins, radio dots) are drawn round; the game's own circles keep their look.
        int circleSteps = painter.MinimumCircleSteps;
        painter.MinimumCircleSteps = 24;
        try
        {
            _root.Draw(painter);
        }
        finally
        {
            painter.MinimumCircleSteps = circleSteps;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SaveLayout();
        // Played outside the editor too (ed_play_project), the project stays over the game when the editor closes.
        if (!EditorCVars.PlayProject.Value)
        {
            _host.UnmountContent(_workspace.Project.LayerId);
        }
        _content.Release();
        _host.CursorLocked = false;
        _surfaces.Dispose();
        _fonts.Dispose();
    }

    private static string LayoutPath => Path.Combine(SettingsPaths.GetUserDirectory(), "editor", "layout.txt");

    private static string? ReadSavedLayout()
    {
        try
        {
            return File.Exists(LayoutPath) ? File.ReadAllText(LayoutPath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void SaveLayout()
    {
        // The viewport-only layout of F3 is a passing state, not the layout to come back to.
        if (_maximizedLayout is not null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
            File.WriteAllText(LayoutPath, _dock.SaveLayout());
        }
        catch (IOException error)
        {
            _host.Logger.Warning("Editor", $"Could not save the editor layout: {error.Message}");
        }
    }

    private void RegisterActions()
    {
        ActionTable actions = _root.Actions;
        actions.Add(new EditorAction("file.close", "Close Editor", () => AfterSaving(_host.Close)) { Category = "File" });
        actions.Add(new EditorAction("file.closemission", "Close Mission", () => AfterSaving(_host.CloseMission))
        {
            Category = "File",
            CanExecute = () => _workspace.Document is not null,
        });
        actions.Add(new EditorAction("file.save", "Save", () => Save())
        {
            Category = "File",
            Shortcut = KeyChord.Parse("Ctrl+S"),
            CanExecute = () => _workspace.Document is not null && _workspace.History.IsDirty,
        });
        actions.Add(new EditorAction("file.reload", "Save and Reload Level", () =>
        {
            if (!_workspace.History.IsDirty || Save())
            {
                _host.ReloadMission();
            }
        })
        {
            Category = "File",
            Shortcut = KeyChord.Parse("Ctrl+R"),
            CanExecute = () => _workspace.Document is not null,
        });
        actions.Add(new EditorAction("file.export", "Export for the Retail Game…", Export)
        {
            Category = "File",
            CanExecute = () => _workspace.Document is not null,
        });
        actions.Add(new EditorAction("play.start", "Play", Play)
        {
            Category = "Play",
            Shortcut = KeyChord.Parse("F5"),
            CanExecute = () => _workspace.Document is not null,
        });
        actions.Add(new EditorAction("file.revert", "Revert Mission to Game Files", () =>
        {
            if (_workspace.RevertMission())
            {
                Notify($"{_workspace.Project.Name} no longer changes this mission.");
            }
        })
        {
            Category = "File",
            CanExecute = () => _workspace.MissionIsInProject,
        });
        actions.Add(new EditorAction("edit.undo", "Undo", () => _workspace.History.Undo())
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+Z"),
            Icon = nameof(KitIcon.Undo),
            CanExecute = () => _workspace.History.CanUndo,
            DynamicTitle = () => _workspace.History.UndoName is { } name ? $"Undo {name}" : "Undo",
        });
        actions.Add(new EditorAction("edit.redo", "Redo", () => _workspace.History.Redo())
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+Y"),
            CanExecute = () => _workspace.History.CanRedo,
            DynamicTitle = () => _workspace.History.RedoName is { } name ? $"Redo {name}" : "Redo",
        });
        actions.Add(new EditorAction("edit.redo-alt", "Redo", () => _workspace.History.Redo())
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+Shift+Z"),
            CanExecute = () => _workspace.History.CanRedo,
        });
        actions.Add(new EditorAction("edit.delete", "Delete", () => _outliner.Delete([.. _workspace.Selection.Items]))
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Delete"),
            CanExecute = () => _workspace.Selection.Count > 0,
        });
        actions.Add(new EditorAction("edit.duplicate", "Duplicate", () => Duplicate(nudge: true))
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+D"),
            CanExecute = () => _workspace.Selection.Count > 0,
        });
        actions.Add(new EditorAction("edit.deselect", "Deselect All", _workspace.Selection.Clear)
        {
            Category = "Edit",
            Shortcut = KeyChord.Parse("Ctrl+Shift+A"),
            CanExecute = () => _workspace.Selection.Count > 0,
        });
        actions.Add(new EditorAction("view.frame", "Frame Selection", () =>
        {
            if (_workspace.Selection.Primary is { } task)
            {
                _viewport.Frame(task);
            }
        })
        {
            Category = "View",
            CanExecute = () => _workspace.Selection.Primary is not null,
        });
        actions.Add(new EditorAction("tool.move", "Move Tool", () => _viewport.Tool = ViewportTool.Move)
        {
            Category = "Tools",
            Shortcut = KeyChord.Parse("W"),
            IsChecked = () => _viewport.Tool == ViewportTool.Move,
        });
        actions.Add(new EditorAction("tool.rotate", "Rotate Tool", () => _viewport.Tool = ViewportTool.Rotate)
        {
            Category = "Tools",
            Shortcut = KeyChord.Parse("E"),
            IsChecked = () => _viewport.Tool == ViewportTool.Rotate,
        });
        actions.Add(new EditorAction("tool.terrain", "Terrain Tool", () =>
        {
            _viewport.Tool = ViewportTool.Terrain;
            _dock.Show("terrain");
        })
        {
            Category = "Tools",
            Shortcut = KeyChord.Parse("T"),
            IsChecked = () => _viewport.Tool == ViewportTool.Terrain,
            CanExecute = () => _host.Terrain is not null,
            DynamicTitle = () => _host.Terrain is null ? "Terrain Tool (this mission's ground cannot be sculpted yet)" : "Terrain Tool",
        });
        foreach ((TerrainBrushMode mode, string title) in new[]
        {
            (TerrainBrushMode.Raise, "Raise"), (TerrainBrushMode.Lower, "Lower"), (TerrainBrushMode.Smooth, "Smooth"), (TerrainBrushMode.Flatten, "Flatten"), (TerrainBrushMode.Paint, "Paint"),
        })
        {
            actions.Add(new EditorAction($"terrain.{title.ToLowerInvariant()}", $"Terrain Brush: {title}", () =>
            {
                _terrainTool.Brush.Mode = mode;
                _viewport.Tool = ViewportTool.Terrain;
            })
            {
                Category = "Tools",
                IsChecked = () => _terrainTool.Brush.Mode == mode,
                CanExecute = () => _host.Terrain is not null,
            });
        }

        foreach ((TerrainObjectMode mode, string id, string title) in new[]
        {
            (TerrainObjectMode.MoveWithGround, "move", "Move Objects With Ground"), (TerrainObjectMode.Protect, "protect", "Protect Objects"), (TerrainObjectMode.Ignore, "ignore", "Ignore Objects"),
        })
        {
            actions.Add(new EditorAction($"terrain.objects.{id}", $"Terrain Brush: {title}", () => _terrainTool.Brush.Objects = mode)
            {
                Category = "Tools",
                IsChecked = () => _terrainTool.Brush.Objects == mode,
            });
        }
        actions.Add(new EditorAction("tool.scale", "Resize Tool", () => _viewport.Tool = ViewportTool.Scale)
        {
            Category = "Tools",
            Shortcut = KeyChord.Parse("R"),
            IsChecked = () => _viewport.Tool == ViewportTool.Scale,
        });
        RegisterViewportActions(actions);
        RegisterVisibilityActions(actions);
        RegisterClipboardActions(actions);
        RegisterObjectActions(actions);
        RegisterTerrainActions(actions);
        RegisterNavGraphActions(actions);
        RegisterPatrolActions(actions);
        RegisterMissionActions(actions);
        RegisterShellActions(actions);
        actions.Add(new EditorAction("view.light", "Light Theme", () => EditorCVars.LightTheme.Value = !EditorCVars.LightTheme.Value)
        {
            Category = "View",
            IsChecked = () => _theme.IsLight,
        });
        actions.Add(new EditorAction("view.reset-layout", "Reset Layout", () =>
        {
            _dock.LoadLayout(DefaultLayout);
            SaveLayout();
        })
        { Category = "View" });

        foreach (string panel in new[] { "outliner", "inspector", "history", "preview", "script", "content", "problems", "logic", "terrain" })
        {
            actions.Add(new EditorAction($"window.{panel}", _dock.Panels[panel].Title, () =>
            {
                if (_dock.IsOpen(panel))
                {
                    _dock.Close(panel);
                }
                else
                {
                    _dock.Show(panel);
                }
            })
            {
                Category = "Window",
                IsChecked = () => _dock.IsOpen(panel),
            });
        }
    }

    private MenuBar BuildMenu()
    {
        ActionTable actions = _root.Actions;
        IReadOnlyList<MenuItem> missions = [.. _host.Missions.Select(m => MenuItem.Command(
            FormattableString.Invariant($"{m.Id}. {_host.MissionName(m)}"),
            () => AfterSaving(() => _host.OpenMission(m.Id))))];

        return new MenuBar(_theme)
            .Add("File", [
                MenuItem.Submenu("Open Mission", missions),
                MenuItem.Separator,
                MenuItem.For(actions["file.newmission"]),
                MenuItem.For(actions["file.missionsettings"]),
                MenuItem.Separator,
                MenuItem.For(actions["file.save"]),
                MenuItem.For(actions["file.reload"]),
                MenuItem.For(actions["file.revert"]),
                MenuItem.For(actions["file.export"]),
                MenuItem.Separator,
                MenuItem.For(actions["file.closemission"]),
                MenuItem.For(actions["file.close"]),
            ])
            .Add("Edit", [
                MenuItem.For(actions["edit.undo"]),
                MenuItem.For(actions["edit.redo"]),
                MenuItem.Separator,
                MenuItem.For(actions["edit.cut"]),
                MenuItem.For(actions["edit.copy"]),
                MenuItem.For(actions["edit.paste"]),
                MenuItem.For(actions["edit.paste-child"]),
                MenuItem.Separator,
                MenuItem.For(actions["edit.rename"]),
                MenuItem.For(actions["edit.duplicate"]),
                MenuItem.For(actions["edit.delete"]),
                MenuItem.For(actions["edit.deselect"]),
                MenuItem.Separator,
                MenuItem.For(actions["edit.hide"]),
                MenuItem.For(actions["edit.show-all"]),
                MenuItem.For(actions["edit.isolate"]),
                MenuItem.Separator,
                MenuItem.For(actions["edit.preferences"]),
            ])
            .Add("Tools", [
                MenuItem.For(actions["tool.move"]),
                MenuItem.For(actions["tool.rotate"]),
                MenuItem.For(actions["tool.scale"]),
                MenuItem.For(actions["tool.terrain"]),
            ])
            .Add("View", [
                MenuItem.For(actions["view.palette"]),
                MenuItem.For(actions["view.goto"]),
                MenuItem.Separator,
                MenuItem.For(actions["view.frame"]),
                MenuItem.For(actions["view.fullscreen"]),
                MenuItem.Separator,
                MenuItem.For(actions["view.grid-snap"]),
                MenuItem.For(actions["view.angle-snap"]),
                MenuItem.For(actions["view.local-space"]),
                MenuItem.For(actions["view.gizmos"]),
                MenuItem.For(actions["view.grid"]),
                MenuItem.Separator,
                MenuItem.For(actions["view.light"]),
                MenuItem.For(actions["view.reset-layout"]),
            ])
            .Add("Play", [
                MenuItem.For(actions["play.start"]),
            ])
            .Add("Window", [
                MenuItem.For(actions["window.outliner"]),
                MenuItem.For(actions["window.inspector"]),
                MenuItem.For(actions["window.history"]),
                MenuItem.For(actions["window.script"]),
                MenuItem.For(actions["window.content"]),
                MenuItem.For(actions["window.problems"]),
                MenuItem.For(actions["window.logic"]),
            ]);
    }

    private Toolbar BuildToolbar()
    {
        ActionTable actions = _root.Actions;
        Toolbar toolbar = new(_theme);
        toolbar.Add(actions["file.save"], KitIcon.Save);
        toolbar.Add(actions["file.reload"], KitIcon.History);
        toolbar.AddSeparator();
        toolbar.Add(actions["edit.undo"], KitIcon.Undo);
        toolbar.Add(actions["edit.redo"], KitIcon.Redo);
        toolbar.AddSeparator();
        toolbar.Add(actions["view.frame"], KitIcon.Search);
        toolbar.Add(actions["edit.duplicate"], KitIcon.Copy);
        toolbar.Add(actions["edit.delete"], KitIcon.Trash);
        toolbar.AddSpring();
        toolbar.Add(actions["file.close"], KitIcon.Close, showText: true);
        return toolbar;
    }

    // Nudged copies stand a metre along X so they show beside their originals; a Shift-drag's copies stand on them and move.
    private void Duplicate(bool nudge)
    {
        if (_workspace.Document is not { } document || _workspace.Selection.Count == 0)
        {
            return;
        }

        List<MissionTask> copies = [];
        IReadOnlyList<MissionTask> originals = [.. _workspace.Selection.Items];
        using (_workspace.History.BeginTransaction(originals.Count == 1 ? $"Duplicate {TaskPresentation.Name(originals[0])}" : $"Duplicate {originals.Count} objects"))
        {
            foreach (MissionTask original in originals)
            {
                MissionTask copy = original.Clone();

                // Nudged a metre along X, so the copy is visible beside its original rather than inside it.
                if (nudge && TaskPresentation.Position(copy) is { } position && position.TryGetNumbers(out double[] units) && units.Length == 3)
                {
                    position.SetNumbers([units[0] + WorldUnits.PerMetre, units[1], units[2]]);
                }

                TaskLocation at = document.Locate(original);
                _workspace.Execute(new InsertTaskCommand(document, copy, at with { Index = at.Index + 1 }));
                copies.Add(copy);
            }
        }

        _workspace.Selection.Set(copies);
    }

    private void Export()
    {
        if (_workspace.Mission is { } open && _workspace.Project.TargetOf(open.Id) == Core.Projects.MissionTarget.OpenIgi)
        {
            Notify($"{_workspace.MissionName} is OpenIGI only, so the original game cannot play it. Make it retail compatible in Mission Settings to export it.", isError: true);
            return;
        }

        // Retail loads exactly what is written, with no editor to catch a mistake, so errors stop the export.
        if (_problems.ErrorCount is > 0 and int errors)
        {
            _dock.Show("problems");
            Notify($"Not exported: {errors} problem(s) would break the mission in the retail game. See Problems.", isError: true);
            return;
        }

        if (_workspace.History.IsDirty && !Save())
        {
            return;
        }

        string fallback = Path.Combine(SettingsPaths.GetUserDirectory(), "editor", "exports", _host.Title.Id, Path.GetFileName(_workspace.Project.Root));
        if (PickFolder is not { } pick)
        {
            ExportTo(fallback);
            return;
        }

        // Asked where, starting at the last export's folder; the answer comes back on the picker's thread, so the export
        // itself runs on the editor's next frame.
        string? last = EditorCVars.ExportFolder.Value is { Length: > 0 } remembered && Directory.Exists(remembered) ? remembered : null;
        pick($"Export {_workspace.MissionName} for the retail game to", last ?? fallback).ContinueWith(
            answer =>
            {
                if (answer.IsCompletedSuccessfully && answer.Result is { Length: > 0 } folder)
                {
                    lock (_pickedExports)
                    {
                        _pickedExports.Enqueue(folder);
                    }
                }
            },
            TaskScheduler.Default);
    }

    // The folder pickers' answers, taken up on the editor's frame.
    private readonly Queue<string> _pickedExports = new();

    /// <summary>Gets the system's folder picker, or null for none (see <see cref="EditorFactory.PickFolder"/>).</summary>
    internal Func<string, string?, Task<string?>>? PickFolder { get; init; }

    // Exports picked since the last frame.
    private void TakePickedExports()
    {
        while (true)
        {
            string folder;
            lock (_pickedExports)
            {
                if (!_pickedExports.TryDequeue(out folder!))
                {
                    return;
                }
            }

            // The game's own folder is never written over: an export goes beside it, to be copied in by hand.
            if (File.Exists(Path.Combine(folder, "igi.exe")) || File.Exists(Path.Combine(folder, "IGI2.exe")) || File.Exists(Path.Combine(folder, "igi2.exe")))
            {
                Notify($"{folder} is the game's own folder, whose files the editor never changes. Pick another folder, then copy the export over the game's files yourself.", isError: true);
                continue;
            }

            EditorCVars.ExportFolder.Value = folder;
            ExportTo(folder);
        }
    }

    private void ExportTo(string target)
    {
        try
        {
            IReadOnlyList<string> written = _workspace.Project.ExportTo(target);
            _host.Logger.Information("Editor", $"Exported {written.Count} file(s) to {target}: {string.Join(", ", written)}.");
            Notify(written.Count == 0
                ? "Nothing to export yet: save a change first."
                : $"Exported {written.Count} file(s) to {target}. Copy its folders over the game's own to play them in the retail game.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Notify($"Could not export: {error.Message}", isError: true);
        }
    }

    private void Play()
    {
        if (_workspace.Document is null || (_workspace.History.IsDirty && !Save()))
        {
            return;
        }

        _root.CloseAllPopups();
        _root.SetFocus(null);
        IsPlaying = true;
        _host.ReloadMission(play: true);
    }

    private void DrawPlayingBanner(UiPainter painter)
    {
        const string text = "Playing  •  Esc returns to the editor";
        float width = _theme.Font.MeasureWidth(text, _theme.TextSize) + _theme.Px(28f);
        UiRect banner = new(MathF.Round((_host.Width - width) * 0.5f), _theme.Px(14f), width, _theme.ControlHeight + _theme.Px(4f));
        EditorDraw.Shadow(painter, _theme, banner);
        EditorDraw.Frame(painter, _theme, banner, _theme.Popup.WithAlpha(230), _theme.Accent);
        EditorDraw.Label(painter, _theme, text, banner.Deflate(new Thickness(_theme.Px(14f), 0f)), _theme.Text);
    }

    // Does something that leaves the open mission, asking first whether to save it when it has changes.
    private void AfterSaving(Action leave)
    {
        if (_workspace.Document is null || !_workspace.History.IsDirty)
        {
            leave();
            return;
        }

        Dialog.Ask(
            _root,
            "Unsaved Changes",
            $"Save the changes to {_workspace.MissionName} first?",
            ("Cancel", static () => { }),
            ("Don't Save", leave),
            ("Save", () =>
            {
                if (Save())
                {
                    leave();
                }
            }));
    }

    private bool Save()
    {
        try
        {
            if (_workspace.Save())
            {
                Notify($"Saved to {_workspace.Project.Name}. Ctrl+R reloads the level from it.");
                return true;
            }
        }
        catch (IOException error)
        {
            Notify($"Could not save: {error.Message}", isError: true);
        }
        catch (UnauthorizedAccessException error)
        {
            Notify($"Could not save: {error.Message}", isError: true);
        }

        return false;
    }

    // A notification in the corner; errors also stay in the status bar a while, where they can be read again.
    private void Notify(string message, bool isError = false)
    {
        _root.Toasts.BottomInset = _theme.StatusBarHeight;
        _root.Toasts.Show(message, isError ? ToastKind.Error : ToastKind.Info, isError ? 8f : 5f);
        if (isError)
        {
            _notice = message;
            _noticeIsError = true;
            _noticeSeconds = 8f;
        }
    }

    private string? _notice;
    private bool _noticeIsError;
    private float _noticeSeconds;

    private void UpdateStatus()
    {
        _status.Segments.Clear();
        if (_workspace.Document is { } document)
        {
            _status.Segments.Add($"{_workspace.TaskCount} objects");
            _status.Segments.Add($"{_workspace.Selection.Count} selected");
        }

        if (_notice is { } notice && _noticeSeconds > 0f)
        {
            _status.Message = notice;
            _status.MessageColor = _noticeIsError ? _theme.Error : _theme.Success;
            return;
        }

        _status.Message = _workspace.LoadError is { } error
            ? $"Could not read the mission: {error}"
            : _workspace.Mission is null
                ? "Choose a mission to open."
                : "Right mouse: look, W A S D Q E to fly, wheel for speed  •  Middle: pan  •  Alt+Left: orbit  •  Wheel: dolly  •  F: frame  •  W/E/R/T tools";
        _status.MessageColor = _workspace.LoadError is null ? null : _theme.Error;
    }

    private sealed class HostClipboard(IEditorHost host) : IUiClipboard
    {
        public string Text
        {
            get => host.ClipboardText;
            set => host.ClipboardText = value;
        }
    }
}
