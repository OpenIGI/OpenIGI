using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Tests for <see cref="LampPhotocells"/>.
/// </summary>
/// <remarks>
/// <para>
/// What is pinned is the shape of a dusk, not the hour it lands on: the lamps come on as the light goes
/// and off as it returns, not all at once, not chattering in the dead band, and not visibly at all when a
/// level merely opens in the dark.
/// </para>
/// <para>
/// The light handed in is the sine of the sun's elevation, so a sweep from 0.3 to -0.3 is a dusk from
/// some seventeen degrees up to the same under.
/// </para>
/// </remarks>
public sealed class LampPhotocellsTests
{
    private const float Frame = 1f / 60f;

    private static LightFixture[] Yard(int count, double spacing = 4.0)
    {
        LightFixture[] fixtures = new LightFixture[count];
        for (int i = 0; i < count; i++)
        {
            fixtures[i] = new("lamp", new Vector3d(i * spacing, 0, 3), 8f, Vector3f.One, 1f, 0.2f, 4);
        }

        return fixtures;
    }

    private static (LampSwitchboard Board, LampPhotocells Cells) Wire(LightFixture[] fixtures, float opening)
    {
        var board = new LampSwitchboard(fixtures, seed: 5);
        var cells = new LampPhotocells(board, fixtures, seed: 5);
        cells.Update(Frame, opening);
        return (board, cells);
    }

    /// <summary>Runs the light from one level to another over a span of seconds, frame by frame.</summary>
    private static void Sweep(LampSwitchboard board, LampPhotocells cells, float from, float to, float seconds)
    {
        int frames = (int)(seconds / Frame);
        for (int f = 0; f <= frames; f++)
        {
            cells.Update(Frame, from + ((to - from) * f / frames));
            board.Update(Frame);
        }
    }

    /// <summary>A level opening at noon has its lamps dark; one opening at midnight, lit. Silently, at once.</summary>
    [Fact]
    public void ALevelOpensWithItsLampsAlreadyRight()
    {
        (LampSwitchboard day, _) = Wire(Yard(12), opening: 0.8f);
        (LampSwitchboard night, _) = Wire(Yard(12), opening: -0.8f);

        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(0f, day.LevelOf(i));
            Assert.Equal(1f, night.LevelOf(i));
        }

        var events = new List<LampEvent>();
        day.DrainEvents(events);
        night.DrainEvents(events);
        Assert.Empty(events);
    }

    /// <summary>Dusk puts every lamp on, and dawn every lamp off.</summary>
    [Fact]
    public void DuskLightsTheYardAndDawnPutsItOut()
    {
        LightFixture[] fixtures = Yard(30);
        (LampSwitchboard board, LampPhotocells cells) = Wire(fixtures, opening: 0.3f);

        Sweep(board, cells, 0.3f, -0.3f, 60f);
        Sweep(board, cells, -0.3f, -0.3f, 30f);

        for (int i = 0; i < fixtures.Length; i++)
        {
            Assert.True(board.IsSupplied(i), $"Lamp {i} is still dark after dusk.");
            Assert.Equal(1f, board.LevelOf(i));
        }

        Sweep(board, cells, -0.3f, 0.3f, 60f);
        Sweep(board, cells, 0.3f, 0.3f, 30f);

        for (int i = 0; i < fixtures.Length; i++)
        {
            Assert.False(board.IsSupplied(i), $"Lamp {i} is still lit after dawn.");
            Assert.Equal(0f, board.LevelOf(i));
        }
    }

    /// <summary>A dusk is ragged: the lamps get their supply across a stretch of it, not at one moment.</summary>
    [Fact]
    public void TheLampsDoNotComeOnTogether()
    {
        LightFixture[] fixtures = Yard(40, spacing: 15.0);
        (LampSwitchboard board, LampPhotocells cells) = Wire(fixtures, opening: 0.3f);
        float[] on = new float[fixtures.Length];
        Array.Fill(on, -1f);

        const float seconds = 60f;
        int frames = (int)(seconds / Frame);
        for (int f = 0; f <= frames; f++)
        {
            cells.Update(Frame, 0.3f - (0.6f * f / frames));
            board.Update(Frame);

            for (int i = 0; i < fixtures.Length; i++)
            {
                if (on[i] < 0f && board.IsSupplied(i))
                {
                    on[i] = f * Frame;
                }
            }
        }

        Assert.All(on, t => Assert.True(t >= 0f));
        Assert.True(on.Max() - on.Min() > 5f, $"Every lamp came on within {on.Max() - on.Min():0.00} s of the first.");
        Assert.True(on.Distinct().Count() > fixtures.Length / 2, "Most lamps came on in step with another.");
    }

    /// <summary>A light hovering at a threshold does not flick the lamps off and on.</summary>
    /// <remarks>The dead band, which is the reason a real photocell has one: a cloud at dusk is not a dawn.</remarks>
    [Fact]
    public void ALightHoveringAtTheThresholdDoesNotChatter()
    {
        LightFixture[] fixtures = Yard(20);
        (LampSwitchboard board, LampPhotocells cells) = Wire(fixtures, opening: 0.3f);
        int[] changes = new int[fixtures.Length];
        bool[] last = new bool[fixtures.Length];

        for (int f = 0; f < 60 * 120; f++)
        {
            float t = f * Frame;

            // Down through dusk, then a wobble of a degree around wherever each lamp tripped.
            float light = t < 30f ? 0.3f - (0.35f * t / 30f) : -0.05f + (0.012f * MathF.Sin(t * 3f));
            cells.Update(Frame, light);
            board.Update(Frame);

            for (int i = 0; i < fixtures.Length; i++)
            {
                bool supplied = board.IsSupplied(i);
                if (f > 0 && supplied != last[i])
                {
                    changes[i]++;
                }

                last[i] = supplied;
            }
        }

        Assert.All(changes, count => Assert.InRange(count, 0, 1));
    }

    /// <summary>A moment's dark -- a shadow passing the cell -- is over before any lamp reacts to it.</summary>
    [Fact]
    public void AMomentsDarkIsIgnored()
    {
        LightFixture[] fixtures = Yard(20);
        (LampSwitchboard board, LampPhotocells cells) = Wire(fixtures, opening: 0.5f);

        Sweep(board, cells, -0.5f, -0.5f, 0.1f);
        Sweep(board, cells, 0.5f, 0.5f, 20f);

        for (int i = 0; i < fixtures.Length; i++)
        {
            Assert.False(board.IsSupplied(i));
            Assert.Equal(0f, board.LevelOf(i));
        }
    }

    /// <summary>A lamp off the daylight is kept on, noon or not.</summary>
    [Fact]
    public void ALampOffTheDaylightStaysLit()
    {
        LightFixture[] fixtures = Yard(4);
        var board = new LampSwitchboard(fixtures, seed: 5);
        var cells = new LampPhotocells(board, fixtures, seed: 5);
        cells.SetFollows(2, false);

        cells.Update(Frame, 0.8f);
        Sweep(board, cells, 0.8f, 0.8f, 5f);

        Assert.True(board.IsSupplied(2));
        Assert.Equal(1f, board.LevelOf(2));
        Assert.False(board.IsSupplied(0));

        // Put back on the daylight at noon, it goes out the way a lamp does -- after its delay.
        cells.SetFollows(2, true);
        Sweep(board, cells, 0.8f, 0.8f, 20f);
        Assert.Equal(0f, board.LevelOf(2));
    }

    /// <summary>A lamp somebody switched off at the wall stays off through dusk.</summary>
    [Fact]
    public void ALampSwitchedOffStaysOffAtDusk()
    {
        LightFixture[] fixtures = Yard(6);
        (LampSwitchboard board, LampPhotocells cells) = Wire(fixtures, opening: 0.3f);
        board.Switch(3);

        Sweep(board, cells, 0.3f, -0.3f, 20f);
        Sweep(board, cells, -0.3f, -0.3f, 30f);

        Assert.Equal(0f, board.LevelOf(3));
        Assert.Equal(1f, board.LevelOf(2));
    }

    /// <summary>Lamps side by side mostly share a circuit; lamps a kilometre apart never do.</summary>
    [Fact]
    public void NeighboursShareACircuitAndStrangersDoNot()
    {
        LightFixture[] near = Yard(20, spacing: 1.0);
        var cells = new LampPhotocells(new LampSwitchboard(near, seed: 3), near, seed: 3);
        Assert.True(cells.CircuitCount < near.Length, "Twenty lamps in twenty metres had twenty photocells.");

        LightFixture[] far = Yard(10, spacing: 1000.0);
        var apart = new LampPhotocells(new LampSwitchboard(far, seed: 3), far, seed: 3);
        Assert.Equal(far.Length, Enumerable.Range(0, far.Length).Select(apart.CircuitOf).Distinct().Count());
    }

    /// <summary>A photocell switches on lower than it switches off, so the morning trips later in the sun.</summary>
    [Fact]
    public void EveryCellHasADeadBand()
    {
        LightFixture[] fixtures = Yard(30, spacing: 50.0);
        var cells = new LampPhotocells(new LampSwitchboard(fixtures, seed: 9), fixtures, seed: 9);

        for (int i = 0; i < fixtures.Length; i++)
        {
            (float on, float off) = cells.ThresholdsOf(i);
            Assert.True(off > on, $"Lamp {i}'s cell switches off at {off} and on at {on}.");
            Assert.InRange(on, -0.1f, 0.15f);
        }
    }
}
