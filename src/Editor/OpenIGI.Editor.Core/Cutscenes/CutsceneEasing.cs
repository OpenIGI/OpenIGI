using System.Globalization;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Editor.Core.Cutscenes;

/// <summary>How the camera runs from one key to the next.</summary>
public enum CutsceneEase
{
    /// <summary>As the game's curve runs it on its own.</summary>
    None,

    /// <summary>Slow away from the key, gathering speed.</summary>
    In,

    /// <summary>Slowing into the next key.</summary>
    Out,

    /// <summary>Slow away and slowing in.</summary>
    InOut,

    /// <summary>At one speed along the way.</summary>
    Even,
}

/// <summary>
/// Easing between two camera keys, baked into keys the game reads, as an editor bakes a curve whose tangents the format cannot
/// keep: the game's camera path is an automatic cubic through every key (<see cref="CutsceneCameraMath.Hermite"/>), so the way is
/// kept and only its timing changed -- helper keys put along the original path at moments an ease curve spaces, the camera then
/// passing the same places sooner or later.
/// </summary>
/// <remarks>
/// <para>
/// The helpers are knots like any other (the mission still plays in the original game), labelled <c>·ease</c>; the first also
/// carries the ease and the key's own length as written, so taking the ease off restores the scene byte for byte. The editor draws
/// a key's helpers as its ease, not as keys of their own.
/// </para>
/// <para>
/// A cubic ease over eight steps leaves the camera at about two in a hundred of its mean speed where it eases. The speed at a key a
/// move goes on through is the next span's, as with any automatic curve: an ease slows the camera into a key it holds at or cuts
/// from, or out of one it starts from.
/// </para>
/// </remarks>
public static class CutsceneEasing
{
    /// <summary>What a helper key's label starts with.</summary>
    public const string HelperMark = "·ease";

    private const int Steps = 8;
    private const double Tick = 0.04;

    /// <summary>Gets whether a knot is an ease's helper rather than a key of its own.</summary>
    /// <param name="camera">The knot's <c>EditCamera</c>.</param>
    /// <returns><see langword="true"/> for a helper.</returns>
    public static bool IsHelper(MissionTask camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return camera.Label.StartsWith(HelperMark, StringComparison.Ordinal);
    }

    /// <summary>The key whose ease a knot is a helper of; the knot itself when it is a key of its own.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="camera">The knot.</param>
    /// <returns>The key.</returns>
    public static MissionTask OwnerOf(MissionTask scene, MissionTask camera)
    {
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        int index = shots.Select(static s => s.Camera).ToList().IndexOf(camera);
        while (index > 0 && IsHelper(shots[index].Camera))
        {
            index--;
        }

        return index >= 0 ? shots[index].Camera : camera;
    }

    /// <summary>How the camera runs from a key to the next.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="key">The key.</param>
    /// <returns>The ease baked after it, or <see cref="CutsceneEase.None"/>.</returns>
    public static CutsceneEase Of(MissionTask scene, MissionTask key) =>
        Helpers(scene, key) is [var first, ..] && Read(first) is { } baked ? baked.Ease : CutsceneEase.None;

    /// <summary>
    /// The step that makes the camera run from a key to the next with an ease: any ease baked there taken off first (the key's
    /// own length restored as written), then, for one other than none, helpers baked along the path as it then is.
    /// </summary>
    /// <param name="document">The mission.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="key">The key, which must glide on into a next.</param>
    /// <param name="ease">The ease.</param>
    /// <returns>The step; null where the key has no next to run to.</returns>
    public static IEditorCommand? Set(MissionDocument document, MissionTask scene, MissionTask key, CutsceneEase ease)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<IEditorCommand> steps = [];
        Unbake(document, scene, key, steps);
        if (ease != CutsceneEase.None && Bake(document, scene, key, ease, steps) is false)
        {
            Undo(steps);
            return null;
        }

        Undo(steps);
        return steps.Count == 0 ? null : new CompositeCommand(ease == CutsceneEase.None ? "Remove Ease" : "Ease", steps);
    }

    /// <summary>
    /// The steps that bake again the eases next to a key whose values were just changed, so their helpers follow the path as it now
    /// is; applied as they are made.
    /// </summary>
    /// <param name="document">The mission.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="key">The key changed.</param>
    /// <returns>The steps, applied; empty where no ease is next to it.</returns>
    public static IReadOnlyList<IEditorCommand> Follow(MissionDocument document, MissionTask scene, MissionTask key)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<IEditorCommand> steps = [];
        List<MissionTask> keys = [.. CutsceneModel.Shots(scene).Select(static s => s.Camera).Where(static c => !IsHelper(c))];
        int index = keys.IndexOf(key);
        foreach (MissionTask? eased in (MissionTask?[])[index > 0 ? keys[index - 1] : null, index >= 0 ? key : null])
        {
            if (eased is not null && Of(scene, eased) is var ease and not CutsceneEase.None)
            {
                Unbake(document, scene, eased, steps);
                Bake(document, scene, eased, ease, steps);
            }
        }

        return steps;
    }

    // The helpers right after a key, in order.
    private static List<MissionTask> Helpers(MissionTask scene, MissionTask key)
    {
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        int index = shots.Select(static s => s.Camera).ToList().IndexOf(key);
        List<MissionTask> helpers = [];
        for (int i = index + 1; index >= 0 && i < shots.Count && IsHelper(shots[i].Camera); i++)
        {
            helpers.Add(shots[i].Camera);
        }

        return helpers;
    }

    // The ease and the key's length as written, kept on the first helper.
    private static (CutsceneEase Ease, string Length)? Read(MissionTask first)
    {
        string[] parts = first.Label.Split(' ', 3);
        return parts.Length == 3 && Enum.TryParse(parts[1], out CutsceneEase ease) ? (ease, parts[2]) : null;
    }

    // Each step applied as it is made, so the next is made against the document as it then stands.
    private static void Apply(IEditorCommand step, List<IEditorCommand> steps)
    {
        step.Apply();
        steps.Add(step);
    }

    private static void Undo(List<IEditorCommand> steps)
    {
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            steps[i].Undo();
        }
    }

    // The helpers after a key taken out, its length put back exactly as it was written.
    private static void Unbake(MissionDocument document, MissionTask scene, MissionTask key, List<IEditorCommand> steps)
    {
        List<MissionTask> helpers = Helpers(scene, key);
        if (helpers.Count == 0 || Read(helpers[0]) is not { } baked)
        {
            return;
        }

        foreach (MissionTask helper in helpers)
        {
            Apply(new RemoveTaskCommand(document, helper), steps);
        }

        Apply(new SetParameterCommand(document, key, CutsceneModel.DurationParameter, [QscParser.ParseExpression(baked.Length)]), steps);
    }

    // Helpers along the path from a key to the next, at moments the ease spaces; false where there is no next to run to.
    private static bool Bake(MissionDocument document, MissionTask scene, MissionTask key, CutsceneEase ease, List<IEditorCommand> steps)
    {
        IReadOnlyList<CutsceneShot> shots = CutsceneModel.Shots(scene);
        int index = shots.Select(static s => s.Camera).ToList().IndexOf(key);
        if (index < 0 || index + 1 >= shots.Count || !CutsceneMoves.Glides(key) || shots[index].Length <= 0
            || key.FindParameter(CutsceneModel.DurationParameter) is not { Arguments: [var written] })
        {
            return false;
        }

        float scale = (float)CutsceneModel.TimeScale(scene);
        CutsceneKey[] path = [.. shots.Select(static s => CutsceneMoves.KeyOf(s.Camera))];
        double start = shots[index].Start;
        double span = shots[index].Length;
        List<double> shares = Shares(ease, span);
        Apply(CutsceneModel.SetLength(document, scene, key, shares[0] * span), steps);

        MissionTask after = key;
        for (int i = 0; i < shares.Count; i++)
        {
            double along = Shape(ease, shares[i], path, scale, start, span);
            MissionTask helper = CutsceneMoves.KeyAt(key, path, scale, start + (along * span));
            helper.Label = i == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{HelperMark} {ease} {QscPrinter.Print(written)}")
                : HelperMark;
            double next = i + 1 < shares.Count ? shares[i + 1] : 1;
            Apply(CutsceneModel.SetLength(document, scene, helper, (next - shares[i]) * span), steps);
            TaskLocation at = document.Locate(after) with { Index = document.Locate(after).Index + 1 };
            Apply(new InsertTaskCommand(document, helper, at), steps);
            after = helper;
        }

        return true;
    }

    // The shares of the span's time the helpers stand at: even steps, and where the ease comes to rest, one more a tick from the
    // key -- the game's tangent at a key is its neighbours' difference scaled by the near span over both, so a helper a tick away
    // all but stops the camera there whatever the span on the key's other side.
    private static List<double> Shares(CutsceneEase ease, double span)
    {
        List<double> shares = [.. Enumerable.Range(1, Steps - 1).Select(static i => i / (double)Steps)];
        double tick = Math.Min(Tick, span / (Steps * 2)) / span;
        if (ease is CutsceneEase.In or CutsceneEase.InOut)
        {
            shares.Insert(0, tick);
        }

        if (ease is CutsceneEase.Out or CutsceneEase.InOut)
        {
            shares.Add(1 - tick);
        }

        return shares;
    }

    // Where along the original span (0..1 of its time) the camera is at a share of the eased span's time.
    private static double Shape(CutsceneEase ease, double share, CutsceneKey[] path, float scale, double start, double span) => ease switch
    {
        CutsceneEase.In => share * share * share,
        CutsceneEase.Out => 1 - Math.Pow(1 - share, 3),
        CutsceneEase.InOut => share < 0.5 ? 4 * share * share * share : 1 - (Math.Pow((-2 * share) + 2, 3) / 2),
        CutsceneEase.Even => EvenAt(share, path, scale, start, span),
        _ => share,
    };

    // The time along the span at which the original path has covered a share of its way: one speed throughout.
    private static double EvenAt(double share, CutsceneKey[] path, float scale, double start, double span)
    {
        const int Samples = 200;
        double[] covered = new double[Samples + 1];
        OpenIGI.Core.Numerics.Vector3d last = CutsceneCameraMath.Evaluate(path, scale, start, -1f).Position;
        for (int i = 1; i <= Samples; i++)
        {
            OpenIGI.Core.Numerics.Vector3d at = CutsceneCameraMath.Evaluate(path, scale, start + (span * i / Samples), -1f).Position;
            covered[i] = covered[i - 1] + (at - last).Length;
            last = at;
        }

        double wanted = share * covered[Samples];
        for (int i = 1; i <= Samples; i++)
        {
            if (covered[i] >= wanted)
            {
                double part = covered[i] > covered[i - 1] ? (wanted - covered[i - 1]) / (covered[i] - covered[i - 1]) : 0;
                return (i - 1 + part) / Samples;
            }
        }

        return 1;
    }
}
