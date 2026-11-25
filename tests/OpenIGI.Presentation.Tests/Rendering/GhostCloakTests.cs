using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests.Rendering;

/// <summary>
/// A race's ghost's cloak: it materialises when first shown, holds steady with only a rare weak flicker, fails when something
/// happens to the run -- weakly with a shot, hard with a hurt, once at an overtake -- closes again after each failure, and
/// dissolves once its runner has fallen or finished. The same every race.
/// </summary>
public sealed class GhostCloakTests
{
    private const int Rate = 60;

    [Fact]
    public void A_ghost_materialises_as_it_is_first_shown_and_its_cloak_catches_as_it_does()
    {
        List<GhostLook> first = Watch(new GhostCloak(), seconds: 2);

        Assert.Equal(0f, first[0].Presence);
        Assert.InRange(first[Frame(0.4)].Presence, 0.2f, 0.8f);
        Assert.Equal(1f, first[Frame(0.85)].Presence);
        Assert.Contains(first[Frame(0.5)..Frame(0.8)], static l => l.Glitch >= 0.3f);
    }

    [Fact]
    public void Left_alone_the_cloak_holds_with_only_rare_weak_flickers()
    {
        List<GhostLook> minute = Watch(new GhostCloak(), seconds: 61)[Frame(1.0)..];

        List<int> bursts = Runs(minute);
        Assert.InRange(bursts.Count, 1, 7);
        Assert.All(bursts, static frames => Assert.InRange(frames, 1, Frame(0.15)));
        Assert.All(minute, static l => Assert.InRange(l.Glitch, 0f, 0.4f));
        Assert.All(minute, static l => Assert.Equal(1f, l.Presence));
    }

    [Fact]
    public void A_shot_makes_the_cloak_shudder_weakly()
    {
        List<GhostLook> looks = Watch(new GhostCloak(), seconds: 6, (Frame(5.0), GhostEvents.Fired));

        float peak = looks[Frame(5.0)..Frame(5.2)].Max(static l => l.Glitch);
        Assert.InRange(peak, 0.01f, 0.25f);
        Assert.Equal(0f, looks[Frame(5.2)].Glitch);
    }

    [Fact]
    public void A_hurt_fails_the_cloak_hard_and_it_closes_again_after()
    {
        List<GhostLook> looks = Watch(new GhostCloak(), seconds: 8, (Frame(5.0), GhostEvents.Hurt));

        Assert.True(looks[Frame(5.0)..Frame(5.3)].Max(static l => l.Glitch) >= 0.5f);

        // Over within a second, aftershock and all, and the cloak closing again from nought to whole.
        GhostLook settled = looks[Frame(6.5)];
        Assert.Equal(0f, settled.Glitch);
        Assert.Equal(1f, settled.Recloak);
        Assert.Contains(looks[Frame(5.2)..Frame(6.5)], static l => l.Glitch == 0f && l.Recloak is > 0f and < 1f);
    }

    [Fact]
    public void An_overtake_fails_the_cloak_once()
    {
        List<GhostLook> looks = Watch(new GhostCloak(), seconds: 6, (Frame(5.0), GhostEvents.Overtaken));

        List<int> bursts = Runs(looks[Frame(4.9)..Frame(5.5)]);
        Assert.Single(bursts);
        Assert.True(looks[Frame(5.0)..Frame(5.3)].Max(static l => l.Glitch) >= 0.3f);
    }

    [Fact]
    public void A_fallen_ghost_lies_a_moment_then_dissolves()
    {
        List<GhostLook> looks = Watch(new GhostCloak(), seconds: 9, (Frame(5.0), GhostEvents.Died));

        Assert.Equal(1f, looks[Frame(6.4)].Presence);
        Assert.InRange(looks[Frame(7.1)].Presence, 0.05f, 0.95f);
        Assert.Equal(0f, looks[Frame(7.8)].Presence);
        Assert.Equal(0f, looks[^1].Presence);
    }

    [Fact]
    public void A_finished_ghost_dissolves()
    {
        List<GhostLook> looks = Watch(new GhostCloak(), seconds: 8, (Frame(5.0), GhostEvents.Finished));

        Assert.Equal(1f, looks[Frame(5.3)].Presence);
        Assert.Equal(0f, looks[Frame(6.7)].Presence);
    }

    [Fact]
    public void Every_race_looks_the_same()
    {
        (int, GhostEvents)[] events = [(Frame(3.0), GhostEvents.Fired), (Frame(7.0), GhostEvents.Hurt), (Frame(12.0), GhostEvents.Overtaken)];

        Assert.Equal(Watch(new GhostCloak(), 30, events), Watch(new GhostCloak(), 30, events));
    }

    private static int Frame(double seconds) => (int)Math.Round(seconds * Rate);

    // A ghost watched at sixty frames a second, each event noticed just before its frame's look.
    private static List<GhostLook> Watch(GhostCloak cloak, int seconds, params (int Frame, GhostEvents Events)[] events)
    {
        List<GhostLook> looks = [];
        for (int frame = 0; frame < seconds * Rate; frame++)
        {
            foreach ((int at, GhostEvents happened) in events)
            {
                if (at == frame)
                {
                    cloak.Notice(happened);
                }
            }

            looks.Add(cloak.Look(frame / (double)Rate));
        }

        return looks;
    }

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

        if (run > 0)
        {
            runs.Add(run);
        }

        return runs;
    }
}
