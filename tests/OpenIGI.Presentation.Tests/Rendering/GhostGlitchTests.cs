using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests.Rendering;

/// <summary>
/// A race's ghost is steady most of the time and fails now and then: bursts of a fraction of a second a few seconds apart, their
/// pattern changing within them, never past full strength, and the same every race.
/// </summary>
public sealed class GhostGlitchTests
{
    [Fact]
    public void A_ghost_glitches_in_short_bursts_a_few_seconds_apart()
    {
        List<GhostLook> minute = Watch(new GhostGlitch(), seconds: 60);

        int glitching = minute.Count(static l => l.Glitch > 0f);
        Assert.InRange(glitching, minute.Count / 100, minute.Count / 8);
        Assert.All(minute, static l => Assert.InRange(l.Glitch, 0f, 1f));

        // Bursts: runs of glitching frames, none longer than half a second, at least six of them in the minute.
        List<int> bursts = Runs(minute);
        Assert.True(bursts.Count >= 6, $"Only {bursts.Count} bursts in a minute.");
        Assert.All(bursts, static frames => Assert.InRange(frames, 1, 60 / 2));
    }

    [Fact]
    public void A_bursts_pattern_changes_within_it()
    {
        List<GhostLook> minute = Watch(new GhostGlitch(), seconds: 60);
        int seeds = minute.Where(static l => l.Glitch > 0f).Select(static l => l.GlitchSeed).Distinct().Count();
        Assert.True(seeds >= 12, $"Only {seeds} patterns in a minute of bursts.");
    }

    [Fact]
    public void Every_race_glitches_the_same()
    {
        Assert.Equal(Watch(new GhostGlitch(), seconds: 30), Watch(new GhostGlitch(), seconds: 30));
    }

    // A ghost watched at sixty frames a second.
    private static List<GhostLook> Watch(GhostGlitch glitch, int seconds) =>
        [.. Enumerable.Range(0, seconds * 60).Select(frame => glitch.Look(frame / 60.0))];

    private static List<int> Runs(List<GhostLook> looks)
    {
        List<int> runs = [];
        int run = 0;
        foreach (GhostLook look in looks)
        {
            if (look.Glitch > 0f)
            {
                run++;
            }
            else if (run > 0)
            {
                runs.Add(run);
                run = 0;
            }
        }

        return runs;
    }
}
