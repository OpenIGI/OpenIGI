using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Configuration;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Projects;
using OpenIGI.Editor.Panels;
using OpenIGI.Editor.Viewport;
using OpenIGI.Host.Editor;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Actions;
using OpenIGI.UI.Editor.Docking;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Rhi;
using OpenIGI.UI.Text;

namespace OpenIGI.Editor;

/// <summary>Opens the editor inside the game.</summary>
public sealed class EditorFactory : IEditorFactory
{
    /// <inheritdoc/>
    public IEditorSession Open(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new EditorSession(host);
    }
}

/// <summary>
/// One open editor: the window chrome (menus, toolbar, status bar), the docked panels, the actions and their
/// shortcuts, and the workspace they all edit.
/// </summary>
internal sealed class EditorSession : IEditorSession
{
    private const string DefaultLayout = "H(0.2:T(outliner*),0.58:T(viewport*),0.22:V(0.65:T(inspector*),0.35:T(history*)))";

    private readonly IEditorHost _host;
    private readonly EditorFonts _fonts;
    private readonly UiSurfaces _surfaces;
    private readonly EditorTheme _theme;
    private readonly EditorRoot _root;
    private readonly MissionWorkspace _workspace;
    private readonly DockSpace _dock;
    private readonly ViewportPanel _viewport;
    private readonly OutlinerPanel _outliner;
    private readonly MenuBar _menu;
    private readonly StatusBar _status;
    private readonly LiveWorldLink _live;
    private bool _disposed;

    public EditorSession(IEditorHost host)
    {
        _host = host;
        _fonts = new EditorFonts(host.Device);
        _surfaces = UiSurfaces.Create(host.Device);
        _theme = new EditorTheme(_fonts, _surfaces, host.UiScale);
        _root = new EditorRoot(_theme) { Clipboard = new HostClipboard(host) };
        // One project per game for now, beside the user's settings; it is mounted over the game while the editor is
        // open, so what is saved is what the level reloads from.
        EditorProject project = EditorProject.OpenOrCreate(
            Path.Combine(SettingsPaths.GetUserDirectory(), "editor", "projects", host.Title.Id, "default"),
            host.Title.Id,
            $"{host.Title.DisplayName} project");
        host.MountContent(project.LayerId, project.Name, project.FilesDirectory);
        _workspace = new MissionWorkspace(host, project);

        _live = new LiveWorldLink(host, _workspace);
        _viewport = new ViewportPanel(_theme, host, _workspace);
        _outliner = new OutlinerPanel(_theme, _workspace);
        _outliner.Activated += _viewport.Frame;
        _workspace.Selection.Changed += () =>
        {
            if (_workspace.Selection.Primary is { } primary)
            {
                _outliner.Reveal(primary);
            }
        };

        _dock = new DockSpace(_theme);
        _dock.Register(new DockPanel("viewport", "Viewport", _viewport) { Icon = KitIcon.Camera, CanClose = false });
        _dock.Register(new DockPanel("outliner", "Outliner", _outliner) { Icon = KitIcon.Hierarchy });
        _dock.Register(new DockPanel("inspector", "Inspector", new InspectorPanel(_theme, _workspace)) { Icon = KitIcon.Gear });
        _dock.Register(new DockPanel("history", "History", new HistoryPanel(_theme, _workspace)) { Icon = KitIcon.History });
        if (!_dock.LoadLayout(ReadSavedLayout() ?? DefaultLayout))
        {
            _dock.LoadLayout(DefaultLayout);
        }

        _dock.LayoutChanged += SaveLayout;

        RegisterActions();
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
    }

    /// <inheritdoc/>
    public UiRect? Viewport =>
        !IsPlaying && _dock.GroupOf("viewport")?.ActivePanel?.Content == _viewport && _workspace.Mission is not null ? _viewport.Bounds : null;

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
        _workspace.Sync();
        _live.Update();
        _noticeSeconds -= elapsedSeconds;
        _menu.Caption = _workspace.Mission is { } mission ? $"{mission.Name}{(_workspace.History.IsDirty ? "  • modified" : string.Empty)}" : _host.Title.DisplayName;
        UpdateStatus();
        if (hasInput)
        {
            _root.Frame(_host.Input, _host.Width, _host.Height, elapsedSeconds);
            _viewport.FlyCamera(elapsedSeconds);
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

        // The system pointer stays visible over the editor, as in any desktop tool, so no drawn cursor goes on top.
        _root.Draw(painter);
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
        _host.UnmountContent(_workspace.Project.LayerId);
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
        actions.Add(new EditorAction("file.close", "Close Editor", _host.Close) { Category = "File" });
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
        actions.Add(new EditorAction("edit.duplicate", "Duplicate", Duplicate)
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
        actions.Add(new EditorAction("view.snap", "Snap to 0.25 m", () => _viewport.Snap = _viewport.Snap > 0 ? 0 : 0.25)
        {
            Category = "View",
            IsChecked = () => _viewport.Snap > 0,
        });
        actions.Add(new EditorAction("view.light", "Light Theme", () =>
        {
            if (_theme.IsLight)
            {
                _theme.ApplyDark();
            }
            else
            {
                _theme.ApplyLight();
            }
        })
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

        foreach (string panel in new[] { "outliner", "inspector", "history" })
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
            FormattableString.Invariant($"{m.Id}. {m.Name}"),
            () => _host.OpenMission(m.Id)))];

        return new MenuBar(_theme)
            .Add("File", [
                MenuItem.Submenu("Open Mission", missions),
                MenuItem.Separator,
                MenuItem.For(actions["file.save"]),
                MenuItem.For(actions["file.reload"]),
                MenuItem.For(actions["file.revert"]),
                MenuItem.Separator,
                MenuItem.For(actions["file.close"]),
            ])
            .Add("Edit", [
                MenuItem.For(actions["edit.undo"]),
                MenuItem.For(actions["edit.redo"]),
                MenuItem.Separator,
                MenuItem.For(actions["edit.duplicate"]),
                MenuItem.For(actions["edit.delete"]),
                MenuItem.For(actions["edit.deselect"]),
            ])
            .Add("View", [
                MenuItem.For(actions["view.frame"]),
                MenuItem.For(actions["view.snap"]),
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
            ]);
    }

    private Toolbar BuildToolbar()
    {
        ActionTable actions = _root.Actions;
        Toolbar toolbar = new(_theme);
        toolbar.Add(actions["file.save"], KitIcon.Save);
        toolbar.Add(actions["file.reload"], KitIcon.Rotate);
        toolbar.AddSeparator();
        toolbar.Add(actions["play.start"], KitIcon.Play, showText: true);
        toolbar.AddSeparator();
        toolbar.Add(actions["edit.undo"], KitIcon.Undo);
        toolbar.Add(actions["edit.redo"], KitIcon.Redo);
        toolbar.AddSeparator();
        toolbar.Add(actions["view.frame"], KitIcon.Search);
        toolbar.Add(actions["view.snap"], KitIcon.Grid);
        toolbar.Add(actions["edit.duplicate"], KitIcon.Copy);
        toolbar.Add(actions["edit.delete"], KitIcon.Trash);
        toolbar.AddSpring();
        toolbar.Add(actions["file.close"], KitIcon.Close, showText: true);
        return toolbar;
    }

    private void Duplicate()
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
                if (TaskPresentation.Position(copy) is { } position && position.TryGetNumbers(out double[] units) && units.Length == 3)
                {
                    position.SetNumbers([units[0] + WorldUnits.PerMetre, units[1], units[2]]);
                }

                TaskLocation at = document.Locate(original);
                _workspace.Execute(new InsertTaskCommand(document, copy, at with { Index = at.Index + 1 }));
                copies.Add(copy);
            }
        }

        _workspace.Selection.Set(copies);
        Notify(copies.Count == 1
            ? "Duplicated. Save and Reload (Ctrl+R) to see a new object in the world."
            : $"Duplicated {copies.Count} objects. Save and Reload (Ctrl+R) to see them in the world.");
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

    private void Notify(string message, bool isError = false)
    {
        _notice = message;
        _noticeIsError = isError;
        _noticeSeconds = 6f;
    }

    private string? _notice;
    private bool _noticeIsError;
    private float _noticeSeconds;

    private void UpdateStatus()
    {
        _status.Segments.Clear();
        if (_workspace.Document is { } document)
        {
            _status.Segments.Add($"{document.EnumerateTasks().Count()} objects");
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
                : "Right mouse: look and fly with W A S D  •  Middle mouse: pan  •  F: frame  •  Ctrl+Z: undo";
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
