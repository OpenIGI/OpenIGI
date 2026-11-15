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
        Assert.True(last < 0.025 * mean, $"arriving at {last / mean:P1} of its mean speed");

        CutsceneEasing.Set(document, scene, span.Camera, CutsceneEase.None)!.Apply();
        Assert.Equal(CutsceneEase.None, CutsceneEasing.Of(scene, span.Camera));
        Assert.Equal(before, document.ToImage());
    }

    private static CutsceneKey[] Keys(MissionTask scene) => [.. CutsceneModel.Shots(scene).Select(static s => CutsceneMoves.KeyOf(s.Camera))];
}
