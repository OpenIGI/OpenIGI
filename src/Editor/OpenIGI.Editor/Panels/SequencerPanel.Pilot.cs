using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Piloting a camera key, as editors pilot a camera: the viewport goes to the key -- where it is seen, its lens, a moment after it
/// starts -- and flying the viewport flies the key, every move written back into it through the space it is seen in
/// (<see cref="CutsceneShotSpace"/>: a riding key relative to where its ride stood as the level loaded), its angles the nearest
/// to its own (<see cref="CameraAngles"/>) and its roll kept, the editor's camera having none.
/// </summary>
/// <remarks>
/// <para>
/// The whole pilot is one step in the history. While it lasts the viewport shows the editor's camera, not the scene's, so the
/// running level is given the change when the pilot ends rather than at every frame of it.
/// </para>
/// <para>A key that watches a target is turned by the look-at whatever its own turn says: the banner says so.</para>
/// </remarks>
internal sealed partial class SequencerPanel
{
    private const double PilotLookAhead = 20.0;
    private const double LensStep = 0.04;
    private const double PilotInto = 0.05;

    private PilotState? _pilot;

    /// <summary>Gets whether a key is being piloted.</summary>
    public bool Piloting => _pilot is not null;

    /// <summary>Gets whether a key can be piloted or keyed now: a scene with keys open, and a viewport to fly.</summary>
    public bool CanPilot => _scene is { } scene && _host.Camera is not null && CutsceneModel.Shots(scene).Count > 0;

    /// <summary>Gets what the viewport's banner says while a key is piloted, or null.</summary>
    public string? PilotBanner
    {
        get
        {
            if (_pilot is not { } pilot || _scene is not { } scene)
            {
                return null;
            }

            IReadOnlyList<CutsceneShot> shots = CutsceneEasing.OwnKeys(scene);
            int index = IndexOfShot(shots, pilot.Camera);
            int link = (int)Number(pilot.Camera, "Link task ID", -1);
            int target = (int)Number(pilot.Camera, "Target task ID", -1);
            double degrees = 2 * Math.Atan(Number(pilot.Camera, "FOV", 1)) * 180 / Math.PI;
            string label = pilot.Camera.Label.Length > 0 ? pilot.Camera.Label : "Shot";
            string rides = link >= 0 ? string.Create(CultureInfo.InvariantCulture, $"  ·  rides #{link}") : string.Empty;
            string watches = target >= 0 ? string.Create(CultureInfo.InvariantCulture, $"  ·  watches #{target}: its turn follows the target") : string.Empty;
            return string.Create(CultureInfo.InvariantCulture,
                $"Piloting {index + 1}. {label}  ·  key {index + 1} of {shots.Count}{rides}{watches}  ·  lens {degrees:0}°  ·  Esc stop  ·  [ ] keys  ·  Alt+wheel lens");
        }
    }

    /// <summary>Gets the share of the picture's height the scene leaves between its letterbox bars, while a key is piloted: its frame.</summary>
    public float? PilotLetterbox =>
        _pilot is not null && _scene is { } scene ? (float)Math.Clamp(Number(scene, "Viewport height factor", 1), 0, 1) : null;

    /// <summary>Starts or stops piloting the key selected, or the one at the playhead.</summary>
    public void TogglePilot()
    {
        if (_pilot is not null)
        {
            StopPilot();
        }
        else
        {
            StartPilot(SelectedShot()?.Camera);
        }
    }

    /// <summary>Pilots the open scene's nth key (from one), the selected one with none given, or stops a pilot that is going.</summary>
    /// <param name="number">The key's number, or null.</param>
    public void PilotKey(int? number)
    {
        if (_pilot is not null && number is null)
        {
            StopPilot();
            return;
        }

        StopPilot();
        MissionTask? camera = number is { } n && _scene is { } scene ? CutsceneEasing.OwnKeys(scene).ElementAtOrDefault(n - 1)?.Camera : SelectedShot()?.Camera;
        StartPilot(camera);
    }

    /// <summary>Pilots a key of any scene: the scene opened first when it is not the one open.</summary>
    /// <param name="camera">The key's <c>EditCamera</c>.</param>
    public void Pilot(MissionTask camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        StopPilot();
        MissionTask? scene = camera.Parent;
        while (scene is not null && scene.TypeName != CutsceneModel.SceneType)
        {
            scene = scene.Parent;
        }

        if (scene is not null && !ReferenceEquals(scene, _scene))
        {
            Refresh();
            int index = IndexOf(_sceneList, scene);
            if (index < 0)
            {
                return;
            }

            EndLive();
            _scenes.SelectedIndex = index;
            _scene = scene;
        }

        StartPilot(camera);
    }

    /// <summary>Stops piloting.</summary>
    public void StopPilot()
    {
        if (_pilot is not { } pilot)
        {
            return;
        }

        _pilot = null;
        _host.LensTangent = null;
        _workspace.History.BreakMerge();

        // The eases next to the key follow where it was flown to, once, as the pilot ends.
        if (_scene is { } scene && _workspace.Document is { } document && document.Contains(pilot.Camera)
            && CutsceneEasing.Following(document, scene, pilot.Camera) is { } follow)
        {
            _workspace.Execute(follow);
        }
    }

    /// <summary>Pilots the key before or after the one piloted.</summary>
    /// <param name="step">-1 for the one before, 1 for the one after.</param>
    public void PilotNext(int step)
    {
        if (_pilot is not { } pilot || _scene is not { } scene)
        {
            return;
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneEasing.OwnKeys(scene);
        int index = IndexOfShot(shots, pilot.Camera) + step;
        if (index >= 0 && index < shots.Count)
        {
            StopPilot();
            StartPilot(shots[index].Camera);
        }
    }

    /// <summary>Widens or narrows the piloted key's lens.</summary>
    /// <param name="notches">Wheel notches: up narrows, as zooming in does.</param>
    /// <returns>Whether a key is piloted, so the wheel was taken.</returns>
    public bool PilotLens(float notches)
    {
        if (_pilot is not { } pilot || _workspace.Document is not { } document)
        {
            return false;
        }

        double tangent = Math.Clamp(Number(pilot.Camera, "FOV", 1) * Math.Pow(1 - LensStep, notches), 0.05, 3.0);
        _workspace.Execute(SetParameterCommand.Numbers(document, pilot.Camera, "FOV", [Math.Round(tangent, 4)]));
        _host.LensTangent = (float)tangent;
        return true;
    }

    /// <summary>Writes the viewport into the key selected, or adds a key from it at the playhead (a key from the view).</summary>
    public void KeyFromView()
    {
        if (SelectedShot() is not null)
        {
            SetShotToView();
        }
        else
        {
            AddShotFromView();
        }
    }

    // The viewport sent to a key, at the moment it starts, through the space it is seen in there.
    private void StartPilot(MissionTask? camera)
    {
        if (_scene is not { } scene || _workspace.Document is null || _host.Camera is not { } view)
        {
            return;
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        CutsceneShot? shot = camera is not null ? shots.FirstOrDefault(s => ReferenceEquals(s.Camera, camera)) : ShotAt(shots, _time);
        if (shot is null)
        {
            return;
        }

        int index = IndexOfShot(shots, shot.Camera);
        _workspace.Selection.Set(shot.Camera);
        // A moment into the key: at its very start the game's frame is still the key before's.
        _time = Into(shot.Start, shot.Length);
        CutsceneShotSpace space = CutsceneShotSpace.World;
        if (LiveActive)
        {
            Live!.IsPlaying = false;
            _wantPlay = false;
            Live.Seek(_time);
            if (Live.ShotSpace is { } shown && shown.Key == index)
            {
                space = shown.Space;
            }
        }

        CutsceneKey key = CutsceneMoves.KeyOf(shot.Camera);
        (Vector3d at, Matrix3f turn) = space.ToWorld(key.Position, Matrix3f.FromEngineEulerAngles(key.Alpha, key.Beta, key.Gamma));
        Matrix3f seen = CutsceneCameraMath.ToCameraOrientation(turn);
        Vector3d eye = at / WorldUnits.PerMetre;
        Vector3f forward = seen.Row3;

        // A key that watches is turned to its target whatever its own turn says: the view looks where the game looks -- live, from
        // the frame shown once it is on the key; else at the target where it is written.
        int target = (int)Number(shot.Camera, "Target task ID", -1);
        bool aimed = target < 0 || !LiveActive;
        Vector3d look = target >= 0 && !LiveActive && _workspace.Document!.EnumerateTasks().FirstOrDefault(t => t.Id == target) is { } watched && TaskPresentation.PositionMetres(watched) is { } there
            ? there
            : eye + (new Vector3d(forward.X, forward.Y, forward.Z) * PilotLookAhead);
        Place(view, eye, look);

        _workspace.History.BreakMerge();
        _host.LensTangent = key.FieldOfView;
        _pilot = new PilotState(shot.Camera, space, CameraAngles.Roll(seen, view.Camera.Orientation), view.Position, view.Camera.Orientation, aimed);
    }

    // Each frame of a pilot: the viewport, when it moved, written into the key.
    private void TickPilot()
    {
        if (_pilot is not { } pilot)
        {
            return;
        }

        if (_workspace.Document is not { } document || !document.Contains(pilot.Camera) || _host.Camera is not { } view)
        {
            StopPilot();
            return;
        }

        // A watching key, live: the view put where the game's frame stands once the scene is on the key, nothing written till then.
        if (!pilot.Aimed)
        {
            if (LiveActive && !Live!.IsPreparing && Live.ShotSpace is { } shown && _scene is { } scene && shown.Key == IndexOfShot(CutsceneModel.Shots(scene), pilot.Camera)
                && Live.CameraShown is { } frame)
            {
                Vector3f ahead = frame.Orientation.Row3;
                Place(view, frame.Position, frame.Position + (new Vector3d(ahead.X, ahead.Y, ahead.Z) * PilotLookAhead));
                _pilot = pilot with { Eye = view.Position, Turn = view.Camera.Orientation, Aimed = true };
            }

            return;
        }

        Matrix3f level = view.Camera.Orientation;
        if (view.Position == pilot.Eye && level.Row1 == pilot.Turn.Row1 && level.Row2 == pilot.Turn.Row2 && level.Row3 == pilot.Turn.Row3)
        {
            return;
        }

        Matrix3f turn = CutsceneCameraMath.FromCameraOrientation(CameraAngles.WithRoll(level, pilot.Roll));
        (Vector3d at, Matrix3f written) = pilot.Space.ToAuthored(view.Position * WorldUnits.PerMetre, turn);
        Vector3f angles = CameraAngles.Closest(written, AnglesOf(pilot.Camera));
        _workspace.Execute(CutsceneModel.SetView(document, pilot.Camera, (at.X, at.Y, at.Z), (angles.X, angles.Y, angles.Z)));
        _pilot = pilot with { Eye = view.Position, Turn = level };
    }

    /// <summary>The viewport's camera, the frame the live scene shows and the key it is on, for a scripted check.</summary>
    /// <returns>A line of text.</returns>
    public string CameraReport()
    {
        string view = _host.Camera is { } camera ? string.Create(CultureInfo.InvariantCulture, $"view {camera.Position} looking {camera.Camera.Orientation.Row3}") : "no view";
        string shown = LiveActive && Live!.CameraShown is { } frame
            ? string.Create(CultureInfo.InvariantCulture, $"frame {frame.Position} looking {frame.Orientation.Row3} on key {Live.ShotSpace?.Key}, preparing {Live.IsPreparing}, at {Live.Seconds:0.##} s")
            : "no frame shown";
        string pilot = _pilot is { } p ? $"piloting {p.Camera.Label}, aimed {p.Aimed}" : "not piloting";
        return $"{view}; {shown}; {pilot}";
    }

    // A moment just into a key, where the game's frame is the key's own (at its very start it is still the key before's).
    private static double Into(double start, double length) => start + Math.Min(PilotInto, Math.Max(0, length) / 2);

    private static int IndexOfShot(IReadOnlyList<CutsceneShot> shots, MissionTask camera)
    {
        for (int i = 0; i < shots.Count; i++)
        {
            if (ReferenceEquals(shots[i].Camera, camera))
            {
                return i;
            }
        }

        return -1;
    }

    // A key being piloted: the space it is written back through, the roll it keeps, and the view last written.
    private sealed record PilotState(MissionTask Camera, CutsceneShotSpace Space, float Roll, Vector3d Eye, Matrix3f Turn, bool Aimed);
}
