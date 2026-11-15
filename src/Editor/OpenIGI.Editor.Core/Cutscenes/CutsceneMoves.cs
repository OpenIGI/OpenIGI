using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Cutscenes;

/// <summary>One camera move of a scene: its keys, which glide one into the next, from its start for as long as they run.</summary>
/// <param name="Keys">The keys, in order: each a scene knot with its moment on the scene's clock.</param>
/// <param name="Start">When the move starts, in scene seconds.</param>
/// <param name="Length">How long it runs.</param>
public sealed record CutsceneMove(IReadOnlyList<CutsceneShot> Keys, double Start, double Length)
{
    /// <summary>Gets when the move ends.</summary>
    public double End => Start + Length;
}

/// <summary>
/// A scene's camera as moves and keys, as Unreal's Sequencer shows camera cuts with the keys on them and Unity's Timeline its clips:
/// the game keeps only a list of knots (<c>EditCamera</c>), each gliding into the next or cutting to it; a move is a run of knots
/// gliding on, ended by one that cuts. And the key edits that keep the scene's timing: retiming a key, adding one where the path
/// is, taking one out.
/// </summary>
/// <remarks>
/// Every edit is the scene's knots written as the game reads them (durations, values, a knot inserted or removed), so a scene edited
/// this way still plays in the original game.
/// </remarks>
public static class CutsceneMoves
{
    // A key nearer the next than this (a knot that only ends a glide, as level data writes them) has no span of its own to drag.
    private const double Instant = 1e-3;

    /// <summary>A scene's moves, in the order the game plays them.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The moves; every knot is a key of exactly one.</returns>
    public static IReadOnlyList<CutsceneMove> Of(MissionTask scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        List<CutsceneMove> moves = [];
        List<CutsceneShot> keys = [];
        foreach (CutsceneShot shot in CutsceneModel.Shots(scene))
        {
            keys.Add(shot);

            // A knot that does not glide into the next ends the move: the next is cut to.
            if (!Glides(shot.Camera))
            {
                moves.Add(Close(keys));
                keys = [];
            }
        }

        if (keys.Count > 0)
        {
            moves.Add(Close(keys));
        }

        return moves;

        static CutsceneMove Close(List<CutsceneShot> keys) =>
            new(keys, keys[0].Start, keys[^1].Start + keys[^1].Length - keys[0].Start);
    }

    /// <summary>Gets whether a key glides into the next rather than cutting to it.</summary>
    /// <param name="camera">The key's <c>EditCamera</c>.</param>
    /// <returns><see langword="true"/> when it glides.</returns>
    public static bool Glides(MissionTask camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return camera.FindParameter("Smooth to next") is { Arguments: [var only] } && ScriptValues.TryGetBool(only, out bool smooth) && smooth;
    }

    /// <summary>
    /// The step that moves a key to another moment: the key before gives or takes the time, so the scene's other keys stay where
    /// they are -- or, rippling, every key after moves with it.
    /// </summary>
    /// <param name="document">The mission.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="key">The key, which must not be the scene's first.</param>
    /// <param name="seconds">Its new moment, in scene seconds.</param>
    /// <param name="ripple">Whether the keys after it move with it.</param>
    /// <returns>The step, or null for the first key or a moment that would leave a key no time.</returns>
    public static IEditorCommand? Retime(MissionDocument document, MissionTask scene, MissionTask key, double seconds, bool ripple)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        int index = IndexOf(shots, key);
        if (index <= 0)
        {
            return null;
        }

        CutsceneShot before = shots[index - 1];
        CutsceneShot moved = shots[index];
        double end = moved.Start + moved.Length;
        double at = Math.Clamp(seconds, before.Start, ripple ? double.MaxValue : end);
        List<IEditorCommand> steps = [CutsceneModel.SetLength(document, scene, before.Camera, at - before.Start)];
        if (!ripple)
        {
            steps.Add(CutsceneModel.SetLength(document, scene, moved.Camera, end - at));
        }

        return new CompositeCommand("Retime Key", steps);
    }

    /// <summary>
    /// The step that adds a key where the path is at a moment, as Unreal adds a key at the playhead: the key the moment falls in
    /// is split there, the new key taking the path's own values and everything else of the key it splits.
    /// </summary>
    /// <param name="document">The mission, which must declare <c>EditCamera</c>.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="seconds">The moment, in scene seconds.</param>
    /// <returns>The step and the new key; null where the moment is not inside a key that runs.</returns>
    /// <remarks>
    /// The game's path draws its curve through every key, so a key added on it leaves the camera's way almost as it was: the drift
    /// is bounded by the tests. The new key's values are written as the path holds them (a riding key's relative to its ride).
    /// </remarks>
    public static (IEditorCommand Step, MissionTask Key)? AddKey(MissionDocument document, MissionTask scene, double seconds)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        CutsceneShot? split = shots.LastOrDefault(s => s.Start <= seconds && seconds < s.Start + s.Length - Instant);
        if (split is null || seconds - split.Start < Instant)
        {
            return null;
        }

        // Where the key will stand once its length is written as the scene keeps lengths (to the millisecond, before the time
        // scale), and the path's values there: the path then passes through it exactly.
        double scale = CutsceneModel.TimeScale(scene);
        double unit = scale > 0 ? scale : 1;
        seconds = split.Start + (Math.Round((seconds - split.Start) / unit, 3) * unit);
        CutsceneKey[] keys = [.. shots.Select(static s => KeyOf(s.Camera))];
        MissionTask added = KeyAt(split.Camera, keys, (float)scale, seconds);
        TaskLocation at = document.Locate(split.Camera) with { Index = document.Locate(split.Camera).Index + 1 };
        IEditorCommand step = new CompositeCommand("Add Key", [
            CutsceneModel.SetLength(document, scene, split.Camera, seconds - split.Start),
            CutsceneModel.SetLength(document, scene, added, split.Start + split.Length - seconds),
            new InsertTaskCommand(document, added, at),
        ]);
        return (step, added);
    }

    /// <summary>
    /// The step that takes a key out and keeps the scene's timing: the key before takes its time, so every later key stays at its
    /// moment.
    /// </summary>
    /// <param name="document">The mission.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="key">The key.</param>
    /// <returns>The step.</returns>
    public static IEditorCommand RemoveKey(MissionDocument document, MissionTask scene, MissionTask key)
    {
        ArgumentNullException.ThrowIfNull(document);
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        int index = IndexOf(shots, key);
        List<IEditorCommand> steps = [];
        if (index > 0)
        {
            CutsceneShot before = shots[index - 1];
            steps.Add(CutsceneModel.SetLength(document, scene, before.Camera, before.Length + shots[index].Length));
        }

        steps.Add(new RemoveTaskCommand(document, key));
        return new CompositeCommand("Remove Key", steps);
    }

    /// <summary>A new key like another (everything else of it kept), standing where a path is at a moment: its values as the path holds them.</summary>
    /// <param name="like">The key whose other values it takes.</param>
    /// <param name="path">The scene's keys as the path reads them.</param>
    /// <param name="scale">The scene's time scale.</param>
    /// <param name="seconds">The moment, in scene seconds.</param>
    /// <returns>The key, not yet in the document.</returns>
    public static MissionTask KeyAt(MissionTask like, CutsceneKey[] path, float scale, double seconds)
    {
        ArgumentNullException.ThrowIfNull(like);
        CutscenePathPoint point = CutsceneCameraMath.Evaluate(path, scale, seconds, -1f);
        Vector3fLike angles = AnglesAt(path, scale, seconds);
        MissionTask added = like.Clone();
        added.Label = like.Label;
        added.FindParameter("Position")?.SetNumbers([point.Position.X, point.Position.Y, point.Position.Z]);
        added.FindParameter("Alpha")?.SetNumbers([angles.X]);
        added.FindParameter("Beta")?.SetNumbers([angles.Y]);
        added.FindParameter("Gamma")?.SetNumbers([angles.Z]);
        added.FindParameter("FOV")?.SetNumbers([point.FieldOfView]);
        return added;
    }

    /// <summary>The scene's keys as the game's camera path reads them, their values as written.</summary>
    /// <param name="camera">A key's <c>EditCamera</c>.</param>
    /// <returns>The key.</returns>
    public static CutsceneKey KeyOf(MissionTask camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        double[] at = camera.FindParameter("Position") is { } position && position.TryGetNumbers(out double[] values) && values.Length == 3 ? values : [0, 0, 0];
        return new CutsceneKey(
            new OpenIGI.Core.Numerics.Vector3d(at[0], at[1], at[2]),
            (float)Number(camera, "Alpha", 0), (float)Number(camera, "Beta", 0), (float)Number(camera, "Gamma", 0),
            (float)Math.Clamp(Number(camera, "FOV", 1), 0.001, 3),
            (float)Number(camera, CutsceneModel.DurationParameter, 0),
            Glides(camera),
            (float)(camera.Parameters.FirstOrDefault(static p => p.Name.StartsWith("Time of day", StringComparison.Ordinal)) is { } hour && hour.TryGetNumbers(out double[] h) && h.Length > 0 ? h[0] : -1),
            (float)Number(camera, "Noise", 0),
            (float)Number(camera, "Camera shake", 0));
    }

    // The path's angles at a moment, channel by channel as the game blends them (the path point's matrix would lose the winding).
    private static Vector3fLike AnglesAt(CutsceneKey[] keys, float scale, double seconds)
    {
        CutsceneKey[] alpha = [.. keys.Select(static k => k with { Position = new(k.Alpha, 0, 0) })];
        CutsceneKey[] beta = [.. keys.Select(static k => k with { Position = new(k.Beta, 0, 0) })];
        CutsceneKey[] gamma = [.. keys.Select(static k => k with { Position = new(k.Gamma, 0, 0) })];
        return new Vector3fLike(
            CutsceneCameraMath.Evaluate(alpha, scale, seconds, -1f).Position.X,
            CutsceneCameraMath.Evaluate(beta, scale, seconds, -1f).Position.X,
            CutsceneCameraMath.Evaluate(gamma, scale, seconds, -1f).Position.X);
    }

    private static int IndexOf(IReadOnlyList<CutsceneShot> shots, MissionTask key)
    {
        for (int i = 0; i < shots.Count; i++)
        {
            if (ReferenceEquals(shots[i].Camera, key))
            {
                return i;
            }
        }

        return -1;
    }

    private static double Number(MissionTask task, string parameter, double fallback) =>
        task.FindParameter(parameter) is { } found && found.TryGetNumbers(out double[] values) && values.Length > 0 ? values[0] : fallback;

    private readonly record struct Vector3fLike(double X, double Y, double Z);
}
