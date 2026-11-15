using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The cutscene sequencer: a scene's camera shots laid along its clock, and beneath them everything the mission cues off
/// that clock -- messages, animations, sounds. Scrub or play it and the viewport looks through the scene's camera as the
/// game moves it (the engine's own path, <see cref="CutsceneCameraMath.Evaluate"/>); drag a shot's end to lengthen it, a cue
/// to move it, and put the viewport's camera in as a new shot.
/// </summary>
/// <remarks>Every change is a step on the mission: the shots are its <c>EditCamera</c> tasks, the cues its conditions.</remarks>
internal sealed partial class SequencerPanel : EditorElement
{
    private const float LabelWidth = 120f;
    private const float RulerHeight = 22f;
    private const float LaneHeight = 30f;
    private const float EdgeGrip = 6f;

    // A cue lane: diamonds nearer than this are one mark; a label keeps this far from its diamond and the next; narrower room than
    // this shows no label; a pointer this near a diamond is on it.
    private const float CueMerge = 10f;
    private const float CueLabelGap = 8f;
    private const float CueShortestLabel = 24f;
    private const float CueReach = 8f;

    // The lanes under the camera: what a cue's task is, by its type.
    // The lanes cues are laid in, the first that holds a cue's task taking it: dialogue apart from the other sounds.
    private static readonly (string Title, Func<MissionTask, bool> Holds)[] CueLanes =
    [
        ("Messages", static t => t.TypeName is "StatusMessage"),
        ("Voice", CutsceneModel.IsDialogue),
        ("Animation", static t => t.TypeName is "AnimTask" or "AnimRigidDynCubeObj"),
        ("Sound", static t => t.TypeName is "ConditionalSound" or "SoundDefSound"),
        ("Other", static _ => true),

        // Not cued off the scene's clock: what waits for its end, and what goes by whether it plays (CutsceneModel.Links).
        (LinksLane, static _ => false),
    ];

    private const string LinksLane = "Links";

    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly Dropdown _scenes;
    private readonly Button _play;
    private readonly Button _preview;
    private readonly Button _live;
    private readonly Button _addShot;
    private readonly Button _deleteShot;
    private readonly Button _playInGame;
    private readonly Button _newScene;
    private readonly Button _addMove;
    private readonly Button _pilotButton;
    private readonly Button _curves;
    private IReadOnlyList<MissionTask> _sceneList = [];
    private MissionTask? _scene;
    private double _time;
    private bool _playing;
    private double _pixelsPerSecond = 40;
    private double _scroll;
    private bool _stale = true;

    // What a drag holds: the clock, a shot's end, or a cue.
    private bool _scrubbing;
    private (CutsceneCue Cue, double Seconds)? _moving;
    private UiVec2? _panFrom;

    // Where the pointer is over the panel, for the list of a cue mark's cues; and the mark last clicked and which of its cues it picked.
    private UiVec2? _hover;
    private (MissionTask First, int Picked)? _markClicked;

    /// <summary>Initializes a new instance of the <see cref="SequencerPanel"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="host">The editor host, for the viewport's camera.</param>
    /// <param name="workspace">The open mission.</param>
    public SequencerPanel(EditorTheme theme, IEditorHost host, MissionWorkspace workspace)
        : base(theme)
    {
        _host = host;
        _workspace = workspace;
        _scenes = new Dropdown(theme) { Placeholder = "No cutscenes", Tooltip = "The cutscene to edit" };
        _scenes.SelectionChanged += index =>
        {
            _scene = index >= 0 && index < _sceneList.Count ? _sceneList[index] : null;
            _time = 0;
            _playing = false;
            _scroll = 0;
            StopPilot();
            EndLive();
        };
        _play = new Button(theme, "Play", KitIcon.Play) { Style = ButtonStyle.Flat, Tooltip = "Play the scene from the playhead (Space)" };
        _play.Clicked += TogglePlay;
        _preview = new Button(theme, "Look Through") { Style = ButtonStyle.Flat, IsToggle = true, IsChecked = true, Tooltip = "Move the viewport's camera along the scene's own camera as you scrub and play" };
        _preview.Clicked += () => _preview.IsChecked = !_preview.IsChecked;
        _live = new Button(theme, "Live") { Style = ButtonStyle.Flat, IsToggle = true, IsChecked = host.CutscenePreview is not null, IsEnabled = host.CutscenePreview is not null, Tooltip = "Play the scene on the level itself, as the game plays it: its vehicles, soldiers and sounds, through its own camera. Off: the shots' path alone, which shows changes not yet saved" };
        _live.Clicked += () =>
        {
            _live.IsChecked = !_live.IsChecked;
            if (!_live.IsChecked)
            {
                _host.CutscenePreview?.Finish();
                _wantPlay = false;
            }
        };
        _addShot = new Button(theme, "Add Shot from View", KitIcon.Plus) { Style = ButtonStyle.Flat, Tooltip = "Put the viewport's camera in as a new shot after the one at the playhead" };
        _addShot.Clicked += AddShotFromView;
        _deleteShot = new Button(theme, "Delete Key", KitIcon.Close) { Style = ButtonStyle.Flat, Tooltip = "Delete the selected key: the key before takes its time, so the keys after keep theirs" };
        _deleteShot.Clicked += DeleteShot;
        _playInGame = new Button(theme, "Play in Game", KitIcon.Play) { Style = ButtonStyle.Flat, Tooltip = "Save and play the mission, to see its scenes as the game plays them, vehicles and all" };
        _playInGame.Clicked += () => PlayInGame?.Invoke();
        _newScene = new Button(theme, "New Cutscene", KitIcon.Plus) { Style = ButtonStyle.Flat, Tooltip = "Make a cutscene of the mission's own, its first shot the viewport's camera; a rule plays it" };
        _newScene.Clicked += NewScene;
        _addMove = new Button(theme, "Add Move…") { Style = ButtonStyle.Flat, Tooltip = "Add a camera rig -- a rail clicked out on the ground, a crane or an orbit round the selected object -- or a ready-made move round it: push in, fly-over, pan" };
        _addMove.Clicked += OfferMoves;
        _pilotButton = new Button(theme, "Pilot", KitIcon.Camera) { Style = ButtonStyle.Flat, IsToggle = true, Tooltip = "Fly the selected key with the viewport: every move is written into it, as seen (Shift+P; Esc stops, [ ] go to the key before or after, Alt+wheel changes its lens)" };
        _pilotButton.Clicked += TogglePilot;
        _curves = new Button(theme, "Curves") { Style = ButtonStyle.Flat, IsToggle = true, Tooltip = "Show the camera's curves under its lane: its speed, its turn rate, or a value it blends; drag a key's dot to change it" };
        _curves.Clicked += () => _curves.IsChecked = !_curves.IsChecked;
        foreach (UiElement child in (UiElement[])[_scenes, _play, _preview, _live, _pilotButton, _curves, _addShot, _deleteShot, _playInGame, _newScene, _addMove])
        {
            AddChild(child);
        }

        workspace.Changed += () => _stale = true;
    }

    /// <summary>Gets or sets what puts the viewport's camera at a point looking at another, in world metres.</summary>
    public Action<Vector3d, Vector3d>? PlaceCamera { get; set; }

    /// <summary>Gets or sets what shows a message: the text, and whether it is an error.</summary>
    public Action<string, bool>? Notify { get; set; }

    /// <summary>Gets or sets what plays the mission in the game, as Play does.</summary>
    public Action? PlayInGame { get; set; }

    /// <inheritdoc/>
    public override bool Focusable => true;

    private float Bar => Theme.ControlHeight + Theme.Px(10f);

    private UiRect Timeline => UiRect.FromEdges(Bounds.X + Theme.Px(LabelWidth), Bounds.Y + Bar, Bounds.Right, Bounds.Bottom);

    /// <summary>Opens a scene by its id or the start of its name, for the console.</summary>
    /// <param name="name">The id (<c>#1200</c>) or name.</param>
    /// <returns>Whether one was found.</returns>
    public bool Open(string name)
    {
        Refresh();
        // A name given exactly first, then the first title holding it.
        int index = -1;
        for (int i = 0; i < _sceneList.Count && index < 0; i++)
        {
            if (_sceneList[i].Label.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
            }
        }

        for (int i = 0; i < _sceneList.Count && index < 0; i++)
        {
            MissionTask scene = _sceneList[i];
            if ((name.StartsWith('#') && int.TryParse(name.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && scene.Id == id)
                || Title(scene).Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
            }
        }

        if (index < 0)
        {
            return false;
        }

        _scenes.SelectedIndex = index;
        _scene = _sceneList[index];
        return true;
    }

    /// <summary>Puts the playhead at a moment of the scene, as a scrub does.</summary>
    /// <param name="seconds">The moment.</param>
    public void Seek(double seconds) => ScrubTo(seconds);

    /// <summary>Plays or pauses the scene, as Play does.</summary>
    public void PlayOrPause() => TogglePlay();

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        Refresh();
        float x = contentRect.X + Theme.Px(6f);
        float y = contentRect.Y + Theme.Px(5f);
        _scenes.Place(context, new UiRect(x, y, Theme.Px(220f), Theme.ControlHeight));
        x += Theme.Px(228f);
        foreach (Button button in (Button[])[_play, _preview, _live, _pilotButton, _curves, _addShot, _addMove, _deleteShot, _newScene, _playInGame])
        {
            float width = Theme.Font.MeasureWidth(button.Text, Theme.TextSize) + Theme.Px(button.Icon == KitIcon.None ? 24f : 44f);
            button.Place(context, new UiRect(x, y, width, Theme.ControlHeight));
            x += width + Theme.Px(4f);
        }

        _play.Text = _playing ? "Pause" : "Play";
        _play.Icon = _playing ? KitIcon.Pause : KitIcon.Play;
        _addShot.IsEnabled = _scene is not null && _host.Camera is not null;
        _pilotButton.IsChecked = Piloting;
        _pilotButton.IsEnabled = Piloting || (_scene is not null && _host.Camera is not null && CutsceneModel.Shots(_scene).Count > 0);
        _deleteShot.IsEnabled = SelectedShot() is not null;
        _addMove.IsEnabled = _scene is not null;
        _newScene.IsEnabled = _workspace.Document?.Declarations.ContainsKey(CutsceneModel.SceneType) == true;
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        TickPilot();
        if (UpdateLive())
        {
            return;
        }

        if (!_playing || _scene is null)
        {
            return;
        }

        _time += elapsedSeconds;
        double length = CutsceneModel.Length(_scene);
        if (_time >= length)
        {
            _time = length;
            _playing = false;
        }

        Look();
    }

    private void Refresh()
    {
        if (!_stale)
        {
            return;
        }

        _stale = false;
        _sceneList = _workspace.Document is { } document ? CutsceneModel.Scenes(document) : [];
        _scenes.Options = [.. _sceneList.Select(Title)];
        int index = _scene is null ? -1 : IndexOf(_sceneList, _scene);
        if (index < 0 && _sceneList.Count > 0)
        {
            index = 0;
        }

        _scene = index >= 0 ? _sceneList[index] : null;
        _scenes.SelectedIndex = index;
    }

    private static int IndexOf(IReadOnlyList<MissionTask> list, MissionTask task)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], task))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Title(MissionTask scene)
    {
        string where = scene.Parent is { Label.Length: > 0 } parent ? parent.Label : string.Empty;
        string name = scene.Label.Length > 0 ? scene.Label : where.Length > 0 ? where : "CutScene";
        return scene.Id >= 0 ? string.Create(CultureInfo.InvariantCulture, $"{name} ({scene.Id})") : name;
    }

    private static CutsceneShot? ShotAt(IReadOnlyList<CutsceneShot> shots, double time) => shots.LastOrDefault(s => s.Start <= time) ?? (shots.Count > 0 ? shots[0] : null);

    private float X(double seconds) => (float)(Timeline.X + ((seconds - _scroll) * _pixelsPerSecond));

    private double Seconds(float x) => Math.Max(0, _scroll + ((x - Timeline.X) / _pixelsPerSecond));

    /// <inheritdoc/>
    protected override bool OnPointerDown(in PointerEvent e)
    {
        if (_scene is not { } scene || _workspace.Document is not { } document || !Timeline.Contains(e.Position))
        {
            return false;
        }

        // A right click on a shot is its menu; anywhere else, as the middle button, it drags the timeline.
        if (e.Button == MouseButton.Right && ShotAt(e.Position) is { } clicked)
        {
            OpenShotMenu(clicked);
            return true;
        }

        if (e.Button is MouseButton.Middle or MouseButton.Right)
        {
            _panFrom = e.Position;
            return true;
        }

        if (e.Button != MouseButton.Left)
        {
            return false;
        }

        float rulerBottom = Timeline.Y + Theme.Px(RulerHeight);
        float laneY = rulerBottom + Theme.Px(4f);
        if (e.Position.Y >= laneY && e.Position.Y < laneY + Theme.Px(LaneHeight) && PressCameraLane(scene, e.Position, e.Shift))
        {
            return true;
        }

        // An animation's clip: selected, so the Inspector edits its keys; one started by a cue is dragged along the clock by it.
        if (PressCurves(scene, e.Position, laneY + Theme.Px(LaneHeight)))
        {
            return true;
        }

        UiVec2 pointer = e.Position;
        float y = laneY + Theme.Px(LaneHeight) + CurvesHeight;
        IReadOnlyList<CutsceneAnimation> animations = CutsceneModel.Animations(document, scene);
        foreach (CutsceneAnimation hit in animations)
        {
            if (pointer.Y >= y && pointer.Y < y + Theme.Px(LaneHeight)
                && pointer.X >= X(hit.Start) && pointer.X < X(hit.Start + Math.Max(hit.Length, 0.2)))
            {
                _workspace.Selection.Set(hit.Task);
                if (CutsceneModel.Cues(document, scene).FirstOrDefault(c => ReferenceEquals(c.Task, hit.Task)) is { } started)
                {
                    _moving = (started, started.Seconds);
                }

                return true;
            }

            y += Theme.Px(LaneHeight);
        }

        // A cue's diamond takes hold of the cue.
        foreach ((string title, _) in CueLanes)
        {
            CutsceneCue[] mine = LaneCues(document, scene, CutsceneModel.Cues(document, scene), animations, title);
            if (mine.Length == 0)
            {
                continue;
            }

            if (pointer.Y >= y && pointer.Y < y + Theme.Px(LaneHeight))
            {
                (CutsceneCue[] ordered, IReadOnlyList<CueLaneLayout.Mark> marks) = LaneMarks(mine);
                if (CueLaneLayout.MarkAt(marks, pointer.X, Theme.Px(CueReach)) is >= 0 and int at)
                {
                    // A mark of several: each click on it picks the next of its cues.
                    CueLaneLayout.Mark mark = marks[at];
                    MissionTask first = ordered[mark.First].Task;
                    int picked = _markClicked is { } last && ReferenceEquals(last.First, first) ? (last.Picked + 1) % mark.Count : 0;
                    _markClicked = (first, picked);
                    CutsceneCue cue = ordered[mark.First + picked];
                    _workspace.Selection.Set(cue.Task);

                    // A link has no moment of its own to move: it is selected, so the Inspector shows what it waits for.
                    if (title != LinksLane)
                    {
                        _moving = (cue, cue.Seconds);
                    }

                    return true;
                }
            }

            y += Theme.Px(LaneHeight);
        }

        _scrubbing = true;
        ScrubTo(Seconds(e.Position.X));
        return true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMove(in PointerEvent e)
    {
        _hover = Bounds.Contains(e.Position) ? e.Position : null;
        if (_panFrom is { } from)
        {
            _scroll = Math.Max(0, _scroll - ((e.Position.X - from.X) / _pixelsPerSecond));
            _panFrom = e.Position;
        }
        else if (_scrubbing)
        {
            ScrubTo(Seconds(e.Position.X));
        }
        else if (DragCameraLane(e.Position.X, e.Shift) || DragCurves(e.Position.Y, Timeline.Y + Theme.Px(RulerHeight + 4f + LaneHeight)))
        {
        }
        else if (_moving is { } move)
        {
            _moving = (move.Cue, Snap(Seconds(e.Position.X), e.Shift));
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerUp(in PointerEvent e)
    {
        if (_scene is { } scene && _workspace.Document is { } document)
        {
            ReleaseCameraLane(document, scene);
            ReleaseCurves(document);
            if (_moving is { } move && Math.Abs(move.Seconds - move.Cue.Seconds) > 1e-6
                && CutsceneModel.Retime(document, scene, move.Cue, move.Seconds) is { } retime)
            {
                _workspace.Execute(retime);
            }
        }

        _scrubbing = false;
        _moving = null;
        _panFrom = null;
    }

    /// <inheritdoc/>
    protected override void OnCaptureLost()
    {
        _scrubbing = false;
        _moving = null;
        _panFrom = null;
    }

    /// <inheritdoc/>
    protected override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        // About the moment under the pointer, which stays under it.
        double at = Seconds(position.X);
        _pixelsPerSecond = Math.Clamp(_pixelsPerSecond * Math.Pow(1.2, notches), 4, 400);
        _scroll = Math.Max(0, at - ((position.X - Timeline.X) / _pixelsPerSecond));
        return true;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(in KeyEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Space:
                TogglePlay();
                return true;
            case KeyCode.Home:
                Seek(0);
                return true;
            case KeyCode.Delete:
                DeleteShot();
                return true;
            default:
                return false;
        }
    }

    // A tenth of a second, or whole seconds with Shift.
    private static double Snap(double seconds, bool whole) => Math.Max(0, whole ? Math.Round(seconds) : Math.Round(seconds * 10) / 10);

    private void TogglePlay()
    {
        if (_scene is null)
        {
            return;
        }

        if (Live is not null && _scene.Id >= 0)
        {
            LiveTogglePlay();
            return;
        }

        if (!_playing && _time >= CutsceneModel.Length(_scene) - 1e-3)
        {
            _time = 0;
        }

        _playing = !_playing;
    }

    private CutsceneShot? SelectedShot() =>
        _scene is { } scene && _workspace.Selection.Primary is { TypeName: CutsceneModel.CameraType } camera
            ? CutsceneModel.Shots(scene).FirstOrDefault(s => ReferenceEquals(s.Camera, camera))
            : null;

    /// <summary>Stops looking through the scene's camera and stands the viewport back and above, where the whole path shows.</summary>
    public void ViewPath()
    {
        _preview.IsChecked = false;
        if (Path() is not { Line.Count: > 0 } path || _host.Camera is not { } view)
        {
            return;
        }

        Vector3d low = path.Line[0].At, high = path.Line[0].At;
        foreach ((Vector3d p, _) in path.Line)
        {
            low = new Vector3d(Math.Min(low.X, p.X), Math.Min(low.Y, p.Y), Math.Min(low.Z, p.Z));
            high = new Vector3d(Math.Max(high.X, p.X), Math.Max(high.Y, p.Y), Math.Max(high.Z, p.Z));
        }

        Vector3d centre = (low + high) * 0.5;
        double reach = Math.Max(20, (high - low).Length);
        Vector3d eye = centre + new Vector3d(0, -reach * 0.9, reach * 0.7);
        Place(view, eye, centre);
    }

    /// <summary>Opens a shot's menu, as a right click on it does.</summary>
    /// <param name="number">The shot's number, from one.</param>
    public void ShotMenu(int number)
    {
        if (_scene is { } scene && CutsceneModel.Shots(scene) is { } shots && number >= 1 && number <= shots.Count)
        {
            OpenShotMenu(shots[number - 1]);
        }
    }

    /// <summary>
    /// Makes an intro from the mission's objectives: a scene that pushes in on each thing an objective is about in turn, gliding
    /// from one to the next, played from the mission's start as retail intros are; one undo step, then opened.
    /// </summary>
    public void MakeIntro()
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        MissionTask[] targets = [.. CutsceneModel.ObjectiveTargets(document).Where(static t => TaskPresentation.PositionMetres(t) is not null)];
        if (targets.Length == 0)
        {
            Notify?.Invoke("No objective names a thing in the world to show: give the mission an objective that is about a task (a terminal hacked, a guard dead).", true);
            return;
        }

        HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id)];
        int id = Enumerable.Range(1, MissionDocument.MaximumTaskId - 1).First(i => !used.Contains(i));
        if (CutsceneModel.AddScene(document, "Intro", id) is not { } made)
        {
            Notify?.Invoke("This level does not declare cutscenes.", true);
            return;
        }

        List<IEditorCommand> steps = [made.Step];
        made.Step.Apply();
        MissionTask? after = null;
        foreach (MissionTask target in targets)
        {
            Vector3d centre = TaskPresentation.PositionMetres(target)!.Value;
            double? SurfaceBelow(double x, double y) => _host.SurfaceBelow(new Vector3d(x, y, centre.Z + 300), 600);

            // From the south-west and above, as an establishing shot comes in, then in to it over four seconds.
            foreach (MoveShot shot in CameraMoves.Shots(CameraMove.PushIn, centre, centre + new Vector3d(-25, -25, 12), 4, SurfaceBelow))
            {
                if (CutsceneModel.AddShot(document, made.Scene, after, (shot.PositionUnits.X, shot.PositionUnits.Y, shot.PositionUnits.Z), (shot.Angles.X, shot.Angles.Y, shot.Angles.Z), shot.Seconds == 0 ? 1.5 : shot.Seconds) is not { } added)
                {
                    break;
                }

                added.Step.Apply();
                steps.Add(added.Step);
                after = added.Camera;
            }
        }

        IEditorCommand start = CutsceneModel.PlayAtStart(document, made.Scene);
        start.Apply();
        steps.Add(start);
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            steps[i].Undo();
        }

        _workspace.Execute(new CompositeCommand("Make Intro from Objectives", steps));
        _stale = true;
        Refresh();
        _scene = made.Scene;
        _scenes.SelectedIndex = IndexOf(_sceneList, made.Scene);
        _time = 0;
        Notify?.Invoke(string.Create(CultureInfo.InvariantCulture, $"Made an intro over {targets.Length} objective target(s), played as the mission starts. A level's own intro plays too: take its Run away to have only this one."), false);
    }

    /// <summary>Makes a new scene, as New Cutscene does.</summary>
    public void MakeScene() => NewScene();

    /// <summary>Adds a camera move round an object, as Add Move does.</summary>
    /// <param name="move">The move.</param>
    /// <param name="subject">What it goes round.</param>
    public void Move(CameraMove move, MissionTask subject) => AddMove(move, subject);

    // A new scene, opened, its first shot where the viewport's camera is.
    private void NewScene()
    {
        if (_workspace.Document is not { } document)
        {
            return;
        }

        HashSet<int> used = [.. document.EnumerateTasks().Select(static t => t.Id)];
        int id = Enumerable.Range(1, MissionDocument.MaximumTaskId - 1).First(i => !used.Contains(i));
        int number = CutsceneModel.Scenes(document).Count + 1;
        if (CutsceneModel.AddScene(document, string.Create(CultureInfo.InvariantCulture, $"Cutscene {number}"), id) is not { } added)
        {
            Notify?.Invoke("This level does not declare cutscenes.", true);
            return;
        }

        _workspace.Execute(added.Step);
        _stale = true;
        Refresh();
        _scene = added.Scene;
        _scenes.SelectedIndex = IndexOf(_sceneList, added.Scene);
        _time = 0;
        AddShotFromView();
        Notify?.Invoke("Made a cutscene. Fly the viewport and Add Shot from View for each shot, or Add Move round an object; a rule's \"Play the cutscene\" starts it.", false);
    }

    // What a move goes round: the selected object, unless it is one of the scene's own cameras.
    private MissionTask? Subject() =>
        _workspace.Selection.Primary is { } task && task.TypeName != CutsceneModel.CameraType && TaskPresentation.PositionMetres(task) is not null ? task : null;

    private void OfferMoves()
    {
        if (Root is not { } root || _scene is null)
        {
            return;
        }

        MissionTask? subject = Subject();
        List<PaletteEntry> entries =
        [
            new(subject is null ? "Rail…" : $"Rail Watching {TaskPresentation.Name(subject)}…", StartRail)
            {
                Detail = "a dolly track clicked out on the ground in the viewport, flown at one speed" + (subject is null ? ", looking along the way" : ", the camera kept on it") + " (a rig: change it later)",
                Icon = KitIcon.Play,
            },
        ];
        if (subject is not null)
        {
            entries.Add(new PaletteEntry("Crane", () => AddSwing(CutsceneRigKind.Crane)) { Detail = "an arm swung a quarter round it, rising, watching it (a rig: change it later)", Icon = KitIcon.Play });
            entries.Add(new PaletteEntry("Orbit", () => AddSwing(CutsceneRigKind.Orbit)) { Detail = "a full turn round it at one height (a rig: change it later)", Icon = KitIcon.Play });
            foreach ((CameraMove move, string title, string detail) in ((CameraMove, string, string)[])[
                (CameraMove.PushIn, "Push In", "from well back straight in"),
                (CameraMove.FlyOver, "Fly-Over", "comes in high and settles in front"),
                (CameraMove.Pan, "Pan", "turns across it from where the camera stands"),
            ])
            {
                entries.Add(new PaletteEntry(title, () => AddMove(move, subject)) { Detail = detail, Icon = KitIcon.Play });
            }
        }

        CommandPalette.Open(root, entries, subject is null ? "A camera move (select an object for more)" : $"A camera move round {TaskPresentation.Name(subject)}");
    }

    // A move's shots, after the one at the playhead, from the side the viewport's camera is on, six seconds long.
    private void AddMove(CameraMove move, MissionTask subject)
    {
        if (_scene is not { } scene || _workspace.Document is not { } document || TaskPresentation.PositionMetres(subject) is not { } centre)
        {
            return;
        }

        Vector3d from = _host.Camera?.Position ?? centre + new Vector3d(30, 0, 10);
        MissionTask? after = CutsceneModel.Shots(scene).LastOrDefault(s => s.Start <= _time)?.Camera;
        List<IEditorCommand> steps = [];
        // The surface under each shot, found from well above it so one laid under the ground still finds the ground.
        double? SurfaceBelow(double x, double y) => _host.SurfaceBelow(new Vector3d(x, y, centre.Z + 300), 600);
        foreach (MoveShot shot in CameraMoves.Shots(move, centre, from, 6, SurfaceBelow))
        {
            if (CutsceneModel.AddShot(document, scene, after, (shot.PositionUnits.X, shot.PositionUnits.Y, shot.PositionUnits.Z), (shot.Angles.X, shot.Angles.Y, shot.Angles.Z), shot.Seconds) is not { } added)
            {
                return;
            }

            // Each shot goes in after the last, so they run in order.
            added.Step.Apply();
            steps.Add(added.Step);
            after = added.Camera;
        }

        // Taken out again and put through the history as one step.
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            steps[i].Undo();
        }

        _workspace.Execute(new CompositeCommand(string.Create(CultureInfo.InvariantCulture, $"Add {move} Move"), steps));
    }

    // The shot under a point of the camera lane.
    private CutsceneShot? ShotAt(UiVec2 point)
    {
        float laneY = Timeline.Y + Theme.Px(RulerHeight) + Theme.Px(4f);
        if (_scene is not { } scene || point.Y < laneY || point.Y >= laneY + Theme.Px(LaneHeight))
        {
            return null;
        }

        return CutsceneModel.Shots(scene).FirstOrDefault(s => point.X >= X(s.Start) && point.X < X(s.Start + s.Length));
    }

    // What can be done to a shot: put it where the view is, cut or glide on, watch or ride something, take it out.
    private void OpenShotMenu(CutsceneShot shot)
    {
        if (Root is not { } root || _workspace.Document is not { } document)
        {
            return;
        }

        MissionTask camera = CutsceneEasing.OwnerOf(_scene!, shot.Camera);
        _workspace.Selection.Set(camera);
        int watching = (int)Number(camera, "Target task ID", -1), riding = (int)Number(camera, "Link task ID", -1);
        bool smooth = Flag(camera, "Smooth to next");
        List<PaletteEntry> entries =
        [
            new("Pilot", () => { StopPilot(); StartPilot(camera); }) { Detail = "fly the viewport to it; every move is written into it, as seen", Icon = KitIcon.Camera },
            new("Set to View", SetShotToView) { Detail = "stand where the viewport's camera is, looking the same way", Icon = KitIcon.Camera },
            new("Add Key at the Playhead", AddKeyAtPlayhead) { Detail = "a key where the camera's path is now, splitting the key it falls in (Shift+K)", Icon = KitIcon.Plus },
            new("Duplicate Key", () => DuplicateKey(document, _scene!, shot)) { Detail = "a copy after it, taking half its time" },
            new("Copy View", () => CopyView(camera)) { Detail = "where it stands, how it is turned and its lens, to paste into another key" },
            smooth
                ? new("Cut to the Next Shot", () => _workspace.Execute(CutsceneModel.SetSmooth(document, camera, false))) { Detail = "jump to the next shot rather than glide into it" }
                : new("Glide into the Next Shot", () => _workspace.Execute(CutsceneModel.SetSmooth(document, camera, true))) { Detail = "fly on into the next shot rather than cut" },
            new("Watch an Object…", () => PickObject("What the shot watches", id => _workspace.Execute(CutsceneModel.Watch(document, camera, id)))) { Detail = "turn to follow it wherever it goes" },
            new("Ride an Object…", () => PickObject("What the shot rides", id => _workspace.Execute(CutsceneModel.Ride(document, camera, id)))) { Detail = "go where it goes, as from a car or a helicopter" },
        ];
        entries.AddRange(EaseEntries(document, _scene!, camera));
        if (watching >= 0)
        {
            entries.Add(new PaletteEntry(string.Create(CultureInfo.InvariantCulture, $"Stop Watching #{watching}"), () => _workspace.Execute(CutsceneModel.Watch(document, camera, -1))));
        }

        if (riding >= 0)
        {
            entries.Add(new PaletteEntry(string.Create(CultureInfo.InvariantCulture, $"Stop Riding #{riding}"), () => _workspace.Execute(CutsceneModel.Ride(document, camera, -1))));
        }

        if (_copiedView is not null)
        {
            entries.Add(new PaletteEntry("Paste View", () => PasteView(document, camera)) { Detail = "the view copied from another key" });
        }

        entries.Add(new PaletteEntry("Delete Key", DeleteShot) { Detail = "the key before takes its time, so the keys after keep theirs", Icon = KitIcon.Close });
        CommandPalette.Open(root, entries, string.Create(CultureInfo.InvariantCulture, $"Shot at {shot.Start:0.##} s"));
    }

    // Every object with an id and a place, to choose one from by name.
    private void PickObject(string title, Action<int> chosen)
    {
        if (Root is not { } root || _workspace.Document is not { } document)
        {
            return;
        }

        PaletteEntry[] objects =
        [
            .. document.EnumerateTasks()
                .Where(static t => t.Id >= 0 && t.TypeName != CutsceneModel.CameraType && TaskPresentation.PositionMetres(t) is not null)
                .Select(t => new PaletteEntry(TaskPresentation.Name(t), () => chosen(t.Id)) { Detail = string.Create(CultureInfo.InvariantCulture, $"#{t.Id}  {t.TypeName}") }),
        ];
        CommandPalette.Open(root, objects, title);
    }

    // The selected shot stands where the viewport's camera is and looks the same way.
    private void SetShotToView()
    {
        if (SelectedShot() is not { } shot || _workspace.Document is not { } document || _host.Camera is not { } view)
        {
            return;
        }

        (Vector3d at, Vector3f angles) = ViewAsKey(view, shot.Camera, AnglesOf(shot.Camera));
        _workspace.Execute(CutsceneModel.SetView(document, shot.Camera, (at.X, at.Y, at.Z), (angles.X, angles.Y, angles.Z)));
    }

    // The viewport's camera as a key has to be written to be seen there: through the space the shown frame carried that key in
    // (a riding key is written relative to where its ride stood as the level loaded), its angles the ones nearest those given, so
    // the game's blend between keys does not turn the long way round. Not live, a key is drawn as written, so it is written as seen.
    private (Vector3d Units, Vector3f Angles) ViewAsKey(OpenIGI.Presentation.Cameras.FreeCamera view, MissionTask camera, Vector3f near)
    {
        Vector3d world = view.Position * WorldUnits.PerMetre;
        Matrix3f turn = CutsceneCameraMath.FromCameraOrientation(view.Camera.Orientation);
        CutsceneShotSpace space = CutsceneShotSpace.World;
        if (LiveActive && Live!.ShotSpace is { } shown && _scene is { } scene
            && CutsceneModel.Shots(scene).ElementAtOrDefault(shown.Key) is { } current && ReferenceEquals(current.Camera, camera))
        {
            space = shown.Space;
        }

        (Vector3d at, Matrix3f written) = space.ToAuthored(world, turn);
        return (at, CameraAngles.Closest(written, near));
    }

    // A key's angles as written: alpha, beta, gamma.
    private static Vector3f AnglesOf(MissionTask camera) =>
        new((float)Number(camera, "Alpha", 0), (float)Number(camera, "Beta", 0), (float)Number(camera, "Gamma", 0));

    // The viewport's camera as a new shot, after the one at the playhead (or last), held three seconds.
    private void AddShotFromView()
    {
        if (_scene is not { } scene || _workspace.Document is not { } document || _host.Camera is not { } view)
        {
            return;
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        MissionTask? after = shots.LastOrDefault(s => s.Start <= _time)?.Camera;

        // A new key rides nothing: written as seen, its angles nearest the key it follows so the blend into it is the short way.
        Vector3d at = view.Position * WorldUnits.PerMetre;
        Vector3f angles = CameraAngles.Closest(
            CutsceneCameraMath.FromCameraOrientation(view.Camera.Orientation),
            after is not null ? AnglesOf(after) : Vector3f.Zero);
        if (CutsceneModel.AddShot(document, scene, after, (at.X, at.Y, at.Z), (angles.X, angles.Y, angles.Z), 3) is not { } added)
        {
            return;
        }

        _workspace.Execute(added.Step);
        _workspace.Selection.Set(added.Camera);
    }

    // The viewport looking through the scene's camera at the playhead: the engine's own path through the shots, a shot that
    // watches a target turned to it where the target stands in the mission.
    private void Look()
    {
        if (Piloting || !_preview.IsChecked || _scene is not { } scene || _host.Camera is not { } view || _workspace.Document is not { } document)
        {
            return;
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        if (shots.Count == 0)
        {
            return;
        }

        CutsceneKey[] keys = [.. shots.Select(static s => CutsceneMoves.KeyOf(s.Camera))];
        CutscenePathPoint point = CutsceneCameraMath.Evaluate(keys, (float)CutsceneModel.TimeScale(scene), _time, -1f);
        Matrix3f orientation = point.Orientation;
        int target = (int)Number(shots[point.Current].Camera, "Target task ID", -1);
        if (target >= 0 && document.EnumerateTasks().FirstOrDefault(t => t.Id == target) is { } watched && TaskPresentation.PositionMetres(watched) is { } there)
        {
            orientation = CutsceneCameraMath.LookAt(point.Position, there * WorldUnits.PerMetre);
        }

        Vector3f forward = CutsceneCameraMath.ToCameraOrientation(orientation).Row3;
        Vector3d eye = point.Position / WorldUnits.PerMetre;
        Place(view, eye, eye + (new Vector3d(forward.X, forward.Y, forward.Z) * 20.0));
    }

    // The viewport's own placing, which ends a glide that would otherwise carry the view off again.
    private void Place(OpenIGI.Presentation.Cameras.FreeCamera view, Vector3d eye, Vector3d target)
    {
        if (PlaceCamera is { } place)
        {
            place(eye, target);
            return;
        }

        Vector3d look = target - eye;
        view.MoveTo(eye, (float)Math.Atan2(look.X, look.Y), (float)Math.Asin(Math.Clamp(look.Z / look.Length, -1, 1)));
    }

    // A flag as the levels spell it, TRUE or FALSE, or as a number.
    private static bool Flag(MissionTask task, string parameter) =>
        task.FindParameter(parameter) is { Arguments: [var only] } && ScriptValues.TryGetBool(only, out bool value) && value;

    private static double Number(MissionTask task, string parameter, double fallback) =>
        task.FindParameter(parameter) is { } found && found.TryGetNumbers(out double[] values) && values.Length > 0 ? values[0] : fallback;
}
