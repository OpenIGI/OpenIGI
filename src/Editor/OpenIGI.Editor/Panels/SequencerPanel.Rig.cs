using System.Globalization;
using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// The open scene's camera rig for the viewport, drawn as a camera and its motion trail: each key a camera with its
/// lens, the path flown between them ticked every half second, a line to what a key watches; and the handles that move and turn
/// a key there, written back through the space it is seen in (<see cref="CutsceneShotSpace"/>).
/// </summary>
/// <remarks>
/// Live, the keys that ride what the shown key rides are drawn where they fly now -- relative to the ride at its pose at the
/// playhead, a motion trail in its space -- and the note says so. Not live, a riding key is drawn as written: where its ride stood
/// as the level loaded, which is how the level the editor shows stands.
/// </remarks>
internal sealed partial class SequencerPanel
{
    private const double TickEvery = 0.5;

    /// <summary>The open scene's camera for the viewport to draw: its path on the engine's own curve, its keys, the playhead.</summary>
    /// <returns>The rig; null with no scene open or no key in it.</returns>
    public CameraPathView? Path()
    {
        if (_scene is not { } scene || _workspace.Document is not { } document)
        {
            return null;
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        if (shots.Count == 0)
        {
            return null;
        }

        CutsceneKey[] keys = [.. shots.Select(static s => CutsceneMoves.KeyOf(s.Camera))];
        int[] links = [.. shots.Select(static s => (int)Number(s.Camera, "Link task ID", -1))];
        (int Ride, CutsceneShotSpace Space)? riding = RideShown(shots, links);
        Vector3d Seen(int key, Vector3d at) =>
            riding is { } r && links[key] == r.Ride ? r.Space.ToWorld(at, Matrix3f.Identity).Position : at;

        float scale = (float)CutsceneModel.TimeScale(scene);
        double length = CutsceneModel.Length(scene);
        int samples = Math.Clamp((int)(length * 12), 2, 600);
        // A cut is a jump, not a flight: the line is broken where the key before it does not glide on.
        List<(Vector3d, bool, bool)> line = new(samples + 1);
        int last = -1;
        for (int i = 0; i <= samples; i++)
        {
            CutscenePathPoint point = CutsceneCameraMath.Evaluate(keys, scale, length * i / samples, -1f);
            bool rides = riding is { } r && links[point.Current] == r.Ride;
            bool joined = last < 0 || Enumerable.Range(last, point.Current - last).All(k => CutsceneMoves.Glides(shots[k].Camera));
            line.Add((Seen(point.Current, point.Position) / WorldUnits.PerMetre, rides, joined));
            last = point.Current;
        }

        List<Vector3d> ticks = [];
        for (double t = TickEvery; t < length; t += TickEvery)
        {
            CutscenePathPoint point = CutsceneCameraMath.Evaluate(keys, scale, t, -1f);
            ticks.Add(Seen(point.Current, point.Position) / WorldUnits.PerMetre);
        }

        List<CameraKeyGlyph> glyphs = new(keys.Length);
        int numbered = 0;
        for (int i = 0; i < keys.Length; i++)
        {
            (Vector3d at, Matrix3f turn) = PoseOf(keys[i], riding is { } r && links[i] == r.Ride ? r.Space : CutsceneShotSpace.World);
            int target = (int)Number(shots[i].Camera, "Target task ID", -1);
            Vector3d? watching = target >= 0 && document.EnumerateTasks().FirstOrDefault(t => t.Id == target) is { } watched ? TaskPresentation.PositionMetres(watched) : null;

            // What the shown key rides, watched: where the ride stands now, as the shown frame took it.
            if (riding is { } carried && target == carried.Ride)
            {
                watching = carried.Space.LinkPosition / WorldUnits.PerMetre;
            }
            glyphs.Add(new CameraKeyGlyph(at / WorldUnits.PerMetre, CutsceneCameraMath.ToCameraOrientation(turn), keys[i].FieldOfView, links[i] >= 0, watching, CutsceneEasing.IsHelper(shots[i].Camera) ? 0 : ++numbered));
        }

        int chosen = SelectedShot() is { } selected ? IndexOfShot(shots, CutsceneEasing.OwnerOf(scene, selected.Camera)) : -1;
        CutscenePathPoint head = CutsceneCameraMath.Evaluate(keys, scale, _time, -1f);
        string? note = riding is { } shown
            ? string.Create(CultureInfo.InvariantCulture, $"Teal: relative to {Named(document, shown.Ride)} where it is now")
            : links.Any(static l => l >= 0) ? "Riding keys drawn where their ride stood as the level loaded" : null;
        return new CameraPathView(line, ticks, glyphs, Seen(head.Current, head.Position) / WorldUnits.PerMetre, chosen, note);
    }

    /// <summary>Where a key of the open scene is seen and how it is turned: what the viewport's handles start from.</summary>
    /// <param name="key">The key's <c>EditCamera</c>.</param>
    /// <returns>Its place in world metres and its turn in the engine's storage; null for a task that is not a key of the open scene.</returns>
    public (Vector3d PositionMetres, Matrix3f Orientation)? KeyPose(MissionTask key)
    {
        if (_scene is not { } scene || key.TypeName != CutsceneModel.CameraType)
        {
            return null;
        }

        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        if (IndexOfShot(shots, key) < 0)
        {
            return null;
        }

        (Vector3d at, Matrix3f turn) = PoseOf(CutsceneMoves.KeyOf(key), SpaceOf(shots, key));
        return (at / WorldUnits.PerMetre, turn);
    }

    /// <summary>Moves or turns a key of the open scene from where the viewport's handles have it, written back as seen.</summary>
    /// <param name="key">The key.</param>
    /// <param name="positionMetres">Where it is to be seen, in world metres; null to leave it.</param>
    /// <param name="orientation">How it is to be turned in the world, in the engine's storage; null to leave it.</param>
    public void MoveKey(MissionTask key, Vector3d? positionMetres, Matrix3f? orientation)
    {
        if (_scene is not { } scene || _workspace.Document is not { } document || KeyPose(key) is not { } pose)
        {
            return;
        }

        CutsceneShotSpace space = SpaceOf(CutsceneModel.Shots(scene), key);
        (Vector3d at, Matrix3f written) = space.ToAuthored((positionMetres ?? pose.PositionMetres) * WorldUnits.PerMetre, orientation ?? pose.Orientation);
        Vector3f angles = CameraAngles.Closest(written, AnglesOf(key));
        _workspace.Execute(CutsceneEasing.WithFollow(document, scene, key, CutsceneModel.SetView(document, key, (at.X, at.Y, at.Z), (angles.X, angles.Y, angles.Z))));
    }

    /// <summary>Selects the open scene's nth key (from nought) and puts the playhead at its start, as a click on it in the viewport does.</summary>
    /// <param name="index">The key's index.</param>
    public void PickKey(int index)
    {
        if (_scene is { } scene && CutsceneModel.Shots(scene).ElementAtOrDefault(index) is { } shot)
        {
            _workspace.Selection.Set(shot.Camera);
            Seek(Into(shot.Start, shot.Length));
        }
    }

    /// <summary>Selects the open scene's nth key of its own (from nought), as numbered on the lane, and puts the playhead just into it.</summary>
    /// <param name="index">The key's index among the keys of their own.</param>
    public void PickOwnKey(int index)
    {
        if (_scene is { } scene && CutsceneEasing.OwnKeys(scene).ElementAtOrDefault(index) is { } shot)
        {
            _workspace.Selection.Set(shot.Camera);
            Seek(Into(shot.Start, shot.Length));
        }
    }

    // A key's place and turn in the world through a space.
    private static (Vector3d At, Matrix3f Turn) PoseOf(CutsceneKey key, CutsceneShotSpace space) =>
        space.ToWorld(key.Position, Matrix3f.FromEngineEulerAngles(key.Alpha, key.Beta, key.Gamma));

    // The space a key is seen in: the shown frame's, live, for the key it shows and the keys riding the same; as written otherwise.
    private CutsceneShotSpace SpaceOf(IReadOnlyList<CutsceneShot> shots, MissionTask key)
    {
        int[] links = [.. shots.Select(static s => (int)Number(s.Camera, "Link task ID", -1))];
        int index = IndexOfShot(shots, key);
        return RideShown(shots, links) is { } riding && index >= 0 && links[index] == riding.Ride ? riding.Space : CutsceneShotSpace.World;
    }

    // Live, what the shown key rides and the space it is carried in now; null when nothing shown rides.
    private (int Ride, CutsceneShotSpace Space)? RideShown(IReadOnlyList<CutsceneShot> shots, int[] links)
    {
        if (!LiveActive || Live!.ShotSpace is not { } shown || shown.Key < 0 || shown.Key >= shots.Count || links[shown.Key] < 0)
        {
            return null;
        }

        return (links[shown.Key], shown.Space);
    }

    private static string Named(MissionDocument document, int id) =>
        document.EnumerateTasks().FirstOrDefault(t => t.Id == id) is { } task
            ? string.Create(CultureInfo.InvariantCulture, $"{(task.Label.Length > 0 ? task.Label : task.TypeName)} ({id})")
            : string.Create(CultureInfo.InvariantCulture, $"#{id}");
}
