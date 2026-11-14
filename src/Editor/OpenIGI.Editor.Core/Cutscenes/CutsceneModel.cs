using System.Globalization;
using System.Text.RegularExpressions;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Cutscenes;

/// <summary>One camera shot of a cutscene: an <c>EditCamera</c>, and where on the scene's clock it starts and how long it holds.</summary>
/// <param name="Camera">The <c>EditCamera</c> task.</param>
/// <param name="Start">When it starts, in scene seconds (the durations before it, each times the scene's time scale).</param>
/// <param name="Length">How long until the next shot, in scene seconds.</param>
public sealed record CutsceneShot(MissionTask Camera, double Start, double Length);

/// <summary>Something in the mission timed against a cutscene's clock: a task whose condition waits for the scene's tick.</summary>
/// <param name="Task">The task.</param>
/// <param name="Parameter">The condition that names the scene's tick.</param>
/// <param name="Seconds">When, in scene seconds.</param>
/// <param name="InSeconds">Whether the condition writes it as seconds (<c>N*GAME_FREQUENCY</c>) rather than ticks.</param>
public sealed record CutsceneCue(MissionTask Task, string Parameter, double Seconds, bool InSeconds);

/// <summary>
/// A cutscene as the sequencer edits it: a <c>CutScene</c> task, its camera shots (its <c>EditCamera</c> knots, depth first as
/// the game flattens them, <c>0x4F4880</c>), and the mission's tasks cued off its clock (an animation, a message, a sound whose
/// condition waits for <c>CutScene_N.nTick</c>).
/// </summary>
/// <remarks>
/// Nothing here is kept apart from the mission: the shots are its tasks and the cues its conditions, so every change is a
/// step on the document and a level whose scenes are only looked at saves as it was.
/// </remarks>
public static partial class CutsceneModel
{
    /// <summary>The scene task type.</summary>
    public const string SceneType = "CutScene";

    /// <summary>The camera knot task type.</summary>
    public const string CameraType = "EditCamera";

    /// <summary>The game's ticks a second (<c>GAME_FREQUENCY</c>).</summary>
    public const double TicksPerSecond = 30;

    private const string DurationParameter = "Duration";
    private const string TimeScaleParameter = "Time scale";

    /// <summary>The mission's cutscenes, in mission order.</summary>
    /// <param name="document">The mission.</param>
    /// <returns>The <c>CutScene</c> tasks.</returns>
    public static IReadOnlyList<MissionTask> Scenes(MissionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return [.. document.EnumerateTasks().Where(static t => t.TypeName == SceneType)];
    }

    /// <summary>A scene's time scale, which each shot's duration is multiplied by; one where it gives none.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The scale.</returns>
    public static double TimeScale(MissionTask scene) => Number(scene, TimeScaleParameter, 1);

    /// <summary>A scene's shots, in the order the game plays them.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The shots.</returns>
    public static IReadOnlyList<CutsceneShot> Shots(MissionTask scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        double scale = TimeScale(scene);
        List<CutsceneShot> shots = [];
        double at = 0;
        foreach (MissionTask camera in scene.SelfAndSubtree().Where(static t => t.TypeName == CameraType))
        {
            double length = Math.Max(0, Number(camera, DurationParameter, 0) * scale);
            shots.Add(new CutsceneShot(camera, at, length));
            at += length;
        }

        return shots;
    }

    /// <summary>How long a scene runs, in scene seconds.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The length.</returns>
    public static double Length(MissionTask scene) => Shots(scene) is { Count: > 0 } shots ? shots[^1].Start + shots[^1].Length : 0;

    /// <summary>Everything in the mission cued off a scene's clock.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="scene">The scene, which must have an id for anything to name its tick.</param>
    /// <returns>The cues, earliest first.</returns>
    public static IReadOnlyList<CutsceneCue> Cues(MissionDocument document, MissionTask scene)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.Id < 0)
        {
            return [];
        }

        Regex tick = TickOf(scene.Id);
        List<CutsceneCue> cues = [];
        foreach (MissionTask task in document.EnumerateTasks())
        {
            foreach (MissionParameter parameter in task.Parameters)
            {
                if (parameter.Arguments is [QscStringLiteral { Value: { } text }] && tick.Match(text) is { Success: true } match
                    && double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    bool seconds = match.Groups["sec"].Success;
                    cues.Add(new CutsceneCue(task, parameter.Name, seconds ? value : value / TicksPerSecond, seconds));
                }
            }
        }

        return [.. cues.OrderBy(static c => c.Seconds)];
    }

    /// <summary>A new scene, its settings those of the mission's first scene where it has one, in a container of its own.</summary>
    /// <param name="document">The mission, which must declare <c>CutScene</c>.</param>
    /// <param name="name">What the scene is called.</param>
    /// <param name="id">The id it takes, which rules name it by.</param>
    /// <returns>The step that puts it in, and the scene; null when the level does not declare scenes.</returns>
    /// <remarks>
    /// The scene runs while its <c>Run</c> holds, which a rule makes it ("Play the cutscene"); until then it is held. Its letterbox
    /// and fades are the level's own scenes', so a new scene looks like the ones beside it.
    /// </remarks>
    public static (IEditorCommand Step, MissionTask Scene)? AddScene(MissionDocument document, string name, int id)
    {
        ArgumentNullException.ThrowIfNull(document);
        MissionTask scene = new(SceneType, name, id);
        if (!document.ApplyDeclaration(scene))
        {
            return null;
        }

        if (Scenes(document) is [var like, ..])
        {
            foreach (MissionParameter parameter in scene.Parameters)
            {
                if (parameter.Declaration.Type != Engine.Tasks.ScriptParamType.VarString && like.FindParameter(parameter.Name) is { } theirs)
                {
                    parameter.Arguments.Clear();
                    parameter.Arguments.AddRange(theirs.Arguments);
                }
            }
        }
        else
        {
            scene.FindParameter(TimeScaleParameter)?.SetNumbers([1]);
            scene.FindParameter("Viewport height factor")?.SetNumbers([0.75]);
            scene.FindParameter("Viewport height factor fade in time")?.SetNumbers([0.5]);
            scene.FindParameter("Viewport height factor fade out time")?.SetNumbers([0.5]);
            scene.FindParameter("Time of day")?.SetNumbers([-1]);
        }

        scene.FindParameter("Initial run")?.SetNumbers([0]);
        MissionTask holder = new(TaskCatalogue.ContainerType, "Cutscenes made in the editor");
        MissionTask? existing = document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == TaskCatalogue.ContainerType && t.Label == "Cutscenes made in the editor");
        if (existing is not null)
        {
            return (new InsertTaskCommand(document, scene, new TaskLocation(existing, existing.Children.Count)), scene);
        }

        holder.AddChild(scene);
        MissionTask? root = document.RootTasks.LastOrDefault();
        return (new InsertTaskCommand(document, holder, new TaskLocation(root, root?.Children.Count ?? 0)), scene);
    }

    /// <summary>The step that makes a shot last so long.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="scene">The scene, whose time scale the duration is written under.</param>
    /// <param name="camera">The shot's <c>EditCamera</c>.</param>
    /// <param name="seconds">How long, in scene seconds.</param>
    /// <returns>The step.</returns>
    public static IEditorCommand SetLength(MissionDocument document, MissionTask scene, MissionTask camera, double seconds)
    {
        double scale = TimeScale(scene);
        double stored = Math.Round(Math.Max(0, seconds) / (scale > 0 ? scale : 1), 3);
        return SetParameterCommand.Numbers(document, camera, DurationParameter, [stored]);
    }

    /// <summary>The step that moves a cue to another moment of the scene's clock, writing it as its condition wrote it.</summary>
    /// <param name="document">The mission.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="cue">The cue.</param>
    /// <param name="seconds">When, in scene seconds.</param>
    /// <returns>The step, or null when the condition no longer names the scene's tick.</returns>
    public static IEditorCommand? Retime(MissionDocument document, MissionTask scene, CutsceneCue cue, double seconds)
    {
        ArgumentNullException.ThrowIfNull(cue);
        if (cue.Task.FindParameter(cue.Parameter) is not { Arguments: [QscStringLiteral { Value: { } text }] })
        {
            return null;
        }

        Match match = TickOf(scene.Id).Match(text);
        if (!match.Success)
        {
            return null;
        }

        string value = cue.InSeconds
            ? Math.Round(Math.Max(0, seconds), 2).ToString("0.##", CultureInfo.InvariantCulture)
            : Math.Round(Math.Max(0, seconds) * TicksPerSecond).ToString(CultureInfo.InvariantCulture);
        Group number = match.Groups["n"];
        string rewritten = string.Concat(text.AsSpan(0, number.Index), value, text.AsSpan(number.Index + number.Length));
        return new SetParameterCommand(document, cue.Task, cue.Parameter, [new QscStringLiteral(rewritten)]);
    }

    /// <summary>A new shot, its camera where and as it is given, after another shot or last in the scene.</summary>
    /// <param name="document">The mission, which must declare <c>EditCamera</c>.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="after">The shot it follows, or null for the scene's end.</param>
    /// <param name="positionUnits">Where the camera stands, in world units.</param>
    /// <param name="angles">Its turn: alpha, beta, gamma, in radians.</param>
    /// <param name="seconds">How long it holds, in scene seconds.</param>
    /// <returns>The step that puts it in, and the new camera task; null when the level does not declare cameras.</returns>
    public static (IEditorCommand Step, MissionTask Camera)? AddShot(
        MissionDocument document,
        MissionTask scene,
        MissionTask? after,
        (double X, double Y, double Z) positionUnits,
        (double Alpha, double Beta, double Gamma) angles,
        double seconds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        MissionTask camera = new(CameraType, "Shot");
        if (!document.ApplyDeclaration(camera))
        {
            return null;
        }

        camera.FindParameter("Position")?.SetNumbers([positionUnits.X, positionUnits.Y, positionUnits.Z]);
        camera.FindParameter("Alpha")?.SetNumbers([angles.Alpha]);
        camera.FindParameter("Beta")?.SetNumbers([angles.Beta]);
        camera.FindParameter("Gamma")?.SetNumbers([angles.Gamma]);
        camera.FindParameter("FOV")?.SetNumbers([1]);
        camera.FindParameter(DurationParameter)?.SetNumbers([Math.Round(Math.Max(0, seconds) / Math.Max(1e-6, TimeScale(scene)), 3)]);
        camera.FindParameter("Smooth to next")?.SetNumbers([1]);
        camera.FindParameter("Link task ID")?.SetNumbers([-1]);
        camera.FindParameter("Target task ID")?.SetNumbers([-1]);
        // "Time of day (-1 means use default)" as the levels declare it: the scene's own hour.
        camera.Parameters.FirstOrDefault(static p => p.Name.StartsWith("Time of day", StringComparison.Ordinal))?.SetNumbers([-1]);

        TaskLocation at = after is not null && after.Parent is not null
            ? document.Locate(after) with { Index = document.Locate(after).Index + 1 }
            : new TaskLocation(scene, scene.Children.Count);
        return (new InsertTaskCommand(document, camera, at), camera);
    }

    private static double Number(MissionTask task, string parameter, double fallback) =>
        task.FindParameter(parameter) is { } found && found.TryGetNumbers(out double[] values) && values.Length > 0 ? values[0] : fallback;

    // CutScene_<id>.nTick compared with a number, the number perhaps times GAME_FREQUENCY.
    private static Regex TickOf(int id) => new(
        string.Create(CultureInfo.InvariantCulture, $@"CutScene_{id}\.nTick\s*(>=|>|==)\s*(?<n>\d+(\.\d+)?)(?<sec>\s*\*\s*GAME_FREQUENCY)?"),
        RegexOptions.CultureInvariant);
}
