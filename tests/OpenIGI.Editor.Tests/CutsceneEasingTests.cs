using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Cutscenes;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Editor.Tests;

/// <summary>
/// Easing baked into keys: the camera keeps to the way it went and only its timing changes -- eased out, it comes into the next
/// key all but at rest; the ease reads back; taking it off gives the mission back byte for byte.
/// </summary>
public sealed class CutsceneEasingTests(ITestOutputHelper output)
{
    [RequiresGameDataFact]
    public void An_eased_span_keeps_its_way_slows_into_its_key_and_comes_off_exactly()
    {
        MissionDocument document = MissionDocument.Read(File.ReadAllBytes(GameData.Resolve("missions/location0/level1/objects.qvm")));
        byte[] before = document.ToImage();
        (MissionTask scene, CutsceneShot span) = CutsceneModel.Scenes(document)
            .SelectMany(static s => CutsceneModel.Shots(s).Select((k, i) => (s, k, i, n: CutsceneModel.Shots(s).Count)))
            .Where(static c => c.i + 1 < c.n && c.k.Length >= 3 && CutsceneMoves.Glides(c.k.Camera))
            .Select(static c => (c.s, c.k))
            .First();
        float scale = (float)CutsceneModel.TimeScale(scene);
        CutsceneKey[] was = Keys(scene);
        double start = span.Start, end = span.Start + span.Length;

        CutsceneEasing.Set(document, scene, span.Camera, CutsceneEase.Out)!.Apply();
        Assert.Equal(CutsceneEase.Out, CutsceneEasing.Of(scene, span.Camera));
        CutsceneKey[] eased = Keys(scene);

        // The way kept: every eased moment is on the original way, near enough.
        List<Vector3d> way = [.. Enumerable.Range(0, 601).Select(i => CutsceneCameraMath.Evaluate(was, scale, start + ((end - start) * i / 600), -1f).Position)];
        double off = 0;
        for (double t = start; t <= end; t += 1 / 30.0)
        {
            Vector3d at = CutsceneCameraMath.Evaluate(eased, scale, t, -1f).Position;
            off = Math.Max(off, way.Min(p => (p - at).Length));
        }

        // Slowing in: the speed over the span's last frame against its mean.
        double mean = (way[^1] - way[0]).Length / (end - start);
        double last = (CutsceneCameraMath.Evaluate(eased, scale, end, -1f).Position - CutsceneCameraMath.Evaluate(eased, scale, end - (1 / 30.0), -1f).Position).Length * 30;
        output.WriteLine($"{span.Camera.Label} {start:0.##}-{end:0.##} s: off the way at most {off / 4096:0.###} m; arriving at {100 * last / mean:0.#}% of its mean speed");
        Assert.True(off < 0.25 * 4096, $"{off / 4096} m off the way");
        Assert.True(last < 0.02 * mean, $"arriving at {last / mean:P1} of its mean speed");

        CutsceneEasing.Set(document, scene, span.Camera, CutsceneEase.None)!.Apply();
        Assert.Equal(CutsceneEase.None, CutsceneEasing.Of(scene, span.Camera));
        Assert.Equal(before, document.ToImage());
    }

    [RequiresGameDataFact]
    public void An_eased_spans_helpers_follow_the_key_it_runs_to_when_that_key_moves()
    {
        MissionDocument document = MissionDocument.Read(File.ReadAllBytes(GameData.Resolve("missions/location0/level1/objects.qvm")));
        (MissionTask scene, CutsceneShot span, MissionTask next) = CutsceneModel.Scenes(document)
            .SelectMany(static s => CutsceneModel.Shots(s).Select((k, i) => (s, k, i, all: CutsceneModel.Shots(s))))
            .Where(static c => c.i + 1 < c.all.Count && c.k.Length >= 3 && CutsceneMoves.Glides(c.k.Camera))
            .Select(static c => (c.s, c.k, c.all[c.i + 1].Camera))
            .First();
        CutsceneEasing.Set(document, scene, span.Camera, CutsceneEase.Out)!.Apply();

        double before = CutsceneModel.Shots(scene).Last(static s => CutsceneEasing.IsHelper(s.Camera)).Camera is var last ? CutsceneMoves.KeyOf(last).Position.Z : 0;

        // The key it runs to lifted ten metres, the ease following.
        double[] at = [.. CutsceneMoves.KeyOf(next).Position is var p ? new[] { p.X, p.Y, p.Z + (10 * 4096) } : []];
        CutsceneEasing.WithFollow(document, scene, next, SetParameterCommand.Numbers(document, next, "Position", at)).Apply();
        Assert.Equal(CutsceneEase.Out, CutsceneEasing.Of(scene, span.Camera));

        // A fresh bake on the moved path puts its helpers where the followed ones are.
        double[] followed = [.. CutsceneModel.Shots(scene).Where(static s => CutsceneEasing.IsHelper(s.Camera)).Select(static s => CutsceneMoves.KeyOf(s.Camera).Position.Z)];
        CutsceneEasing.Set(document, scene, span.Camera, CutsceneEase.Out)!.Apply();
        double[] fresh = [.. CutsceneModel.Shots(scene).Where(static s => CutsceneEasing.IsHelper(s.Camera)).Select(static s => CutsceneMoves.KeyOf(s.Camera).Position.Z)];
        Assert.Equal(fresh.Length, followed.Length);
        for (int i = 0; i < fresh.Length; i++)
        {
            Assert.Equal(fresh[i], followed[i], 3);
        }

        Assert.True(followed[^1] - before > 5 * 4096, "the helper next to the key rose with it");
    }

    /// <summary>
    /// The speeding train's half turn round the train (a span that already speeds up, ending on a cut) eased in and out: the same
    /// way in the same time, from rest to rest, at most half again its mean pace -- the ease's own shape, not the curve's times it.
    /// </summary>
    [RequiresGameDataFact]
    public void An_ease_in_and_out_shapes_the_way_covered_whatever_the_curve_did_there()
    {
        MissionDocument document = MissionDocument.Read(File.ReadAllBytes(GameData.Resolve("missions/location0/level1/objects.qvm")));
        MissionTask scene = CutsceneModel.Scenes(document).First(static s => s.Id == 1202);
        CutsceneShot span = CutsceneModel.Shots(scene).First(static s => s.Camera.Label == "Train total" && s.Length > 1);
        float scale = (float)CutsceneModel.TimeScale(scene);
        (double way, double peak) Run(CutsceneKey[] path)
        {
            double covered = 0, fastest = 0;
            Vector3d last = CutsceneCameraMath.Evaluate(path, scale, span.Start, -1f).Position;
            for (int i = 1; i <= 400; i++)
            {
                Vector3d at = CutsceneCameraMath.Evaluate(path, scale, span.Start + Math.Min(span.Length * i / 400, span.Length - 1e-4), -1f).Position;
                double step = (at - last).Length / 4096;
                covered += step;
                fastest = Math.Max(fastest, step / (span.Length / 400));
                last = at;
            }

            return (covered, fastest);
        }

        (double wasWay, double wasPeak) = Run(Keys(scene));
        CutsceneEasing.Set(document, scene, span.Camera, CutsceneEase.InOut)!.Apply();
        (double way, double peak) = Run(Keys(scene));
        double mean = way / span.Length;
        output.WriteLine($"{span.Length:0.#} s: way {wasWay:0.0} -> {way:0.0} m; peak {wasPeak:0.0} -> {peak:0.0} m/s, mean {mean:0.0} ({peak / mean:0.00}x)");
        Assert.Equal(wasWay, way, wasWay * 0.02);
        Assert.InRange(peak / mean, 1.3, 1.75);
    }

    private static CutsceneKey[] Keys(MissionTask scene) => [.. CutsceneModel.Shots(scene).Select(static s => CutsceneMoves.KeyOf(s.Camera))];
}
