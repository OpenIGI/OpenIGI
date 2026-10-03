using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// Holds the fall to being a fall and not a printed texture: drops must lean by different amounts and
/// in every direction, and the difference must be big enough to see.
/// </summary>
/// <remarks>
/// <para>
/// The defect these were written against is that every streak on screen was parallel to every other.
/// Two causes, and the first hides the second. The sideways drift was drawn independently on each
/// axis, which puts more drops on the diagonals than on the axes and gives the whole field a grain;
/// and it was handed over as a one-off velocity, so every drop leaned by the same tiny angle for its
/// whole life however long it had been falling.
/// </para>
/// <para>
/// The spread is now a <b>force</b> — a random range drawn at spawn and left in the drop's physics
/// force for the solver to integrate on every update after. So a drop's lean grows with its age, and
/// because the air holds drops of every age at once, the fall carries a range of angles rather than
/// one. That is the property being pinned here, and it is a property rather than a number: the exact
/// magnitude is a setting, and these tests are written so that tuning it cannot make them vacuous.
/// </para>
/// </remarks>
public sealed class StormDropSpreadTests
{
    /// <summary>A fall speed in the middle of the settings' range, for turning drifts into angles.</summary>
    private const float Fall = 11.5f;

    /// <summary>How long a drop that started at the top of the column has been falling when seen.</summary>
    private const float TypicalAge = 1.5f;

    /// <summary>The spread the game actually ships with, so tuning it cannot make these vacuous.</summary>
    private static float Spread => new PrecipitationSettings().SpreadMetresPerSecondSquared;

    [Fact]
    public void Drift_LeansInEveryDirectionRatherThanAlongTheDiagonals()
    {
        // Sixteen compass sectors. A drift drawn per-axis lands in the four diagonal sectors far more
        // often than in the four axial ones; one drawn as an angle fills them evenly.
        int[] sectors = new int[16];
        const int Samples = 4096;

        Random random = new(20260907);
        for (int i = 0; i < Samples; i++)
        {
            (float x, float y, float _) = StormRenderer.SpawnDrift(
                Spread, random.NextSingle(), random.NextSingle(), random.NextSingle());

            if (MathF.Abs(x) + MathF.Abs(y) < 1e-6f)
            {
                continue;
            }

            double bearing = Math.Atan2(y, x);
            int sector = (int)Math.Floor(((bearing + Math.Tau) % Math.Tau) / (Math.Tau / sectors.Length));
            sectors[Math.Clamp(sector, 0, sectors.Length - 1)]++;
        }

        int fewest = sectors.Min();
        int most = sectors.Max();
        Assert.True(fewest > 0, "some directions never occur at all");
        Assert.True(
            most < fewest * 1.25,
            $"the drift has a grain: the busiest of sixteen directions took {most} of the samples and "
                + $"the quietest {fewest}, which is what drawing each axis on its own produces");
    }

    [Fact]
    public void Drift_GrowsWithAgeSoThatDropsOfDifferentAgesLeanDifferently()
    {
        // The spread stays in the physics force, so the solver keeps applying it. A drop
        // one second old must therefore lean measurably further than one a tenth of a second old.
        (float x, float y, float z) = StormRenderer.SpawnDrift(Spread, 1f, 0f, 0.5f);

        float young = Lean(StormRenderer.DriftAt(0f, 0f, x, y, 0.1f), Fall);
        float old = Lean(StormRenderer.DriftAt(0f, 0f, x, y, 2.0f), Fall);

        Assert.True(
            old > young * 4f,
            $"a two-second-old drop leans {old:0.00}° against a young one's {young:0.00}°, so the "
                + "spread is not being integrated and every drop leans the same however long it falls");
    }

    [Fact]
    public void Drift_SpreadsTheFallOverAVisibleRangeOfAngles()
    {
        // The whole point. Across the drops the air holds at any moment — every magnitude, every
        // direction, every age up to a typical fall — the lean angles must cover a range the eye can
        // see. A degree is not visible; this asks for several.
        float widest = 0f;
        float narrowest = float.MaxValue;

        // The push's strength, the drop's size and its age are three INDEPENDENT draws in the emitter,
        // so they are swept independently here. Tying the push to the size hides the widest lean there
        // is -- a small slow drop that happened to get a strong push.
        for (int m = 0; m <= 8; m++)
        {
            for (int a = 0; a <= 8; a++)
            {
                for (int size = 0; size <= 8; size++)
                {
                    for (int t = 1; t <= 6; t++)
                    {
                        (float dx, float dy, float _) = StormRenderer.SpawnDrift(Spread, m / 8f, a / 8f, 0.5f);
                        float terminal = StormRenderer.TerminalFor(Fall, 0.3f + ((size / 8f) * 0.7f));
                        float lean = Lean(StormRenderer.DriftAt(0f, 0f, dx, dy, TypicalAge * t / 6f), terminal);
                        widest = MathF.Max(widest, lean);
                        narrowest = MathF.Min(narrowest, lean);
                    }
                }
            }
        }

        Assert.True(
            widest - narrowest > 10f,
            $"the fall spans {widest - narrowest:0.00}° from the most leaning drop to the least, which "
                + "reads as a sheet of parallel lines rather than as weather");
    }

    [Fact]
    public void Drift_StillFollowsTheWindItWasBornIn()
    {
        // The spread is a scatter about the wind, not a replacement for it: a strong wind must still
        // carry every drop the same way, or a gust stops being visible as a gust.
        const float Wind = 6f;
        float against = float.MaxValue;

        for (int m = 0; m <= 8; m++)
        {
            for (int a = 0; a <= 8; a++)
            {
                (float dx, float dy, float _) = StormRenderer.SpawnDrift(Spread, m / 8f, a / 8f, 0.5f);
                (float x, float _) = StormRenderer.DriftAt(Wind, 0f, dx, dy, TypicalAge);
                against = MathF.Min(against, x);
            }
        }

        Assert.True(against > 0f, $"a drop was blown upwind ({against:0.00} m/s against a {Wind} m/s wind)");
    }

    [Fact]
    public void Drift_IsZeroWhenTheSpreadIsTurnedOff()
    {
        (float x, float y, float z) = StormRenderer.SpawnDrift(0f, 1f, 0.37f, 0.8f);
        Assert.Equal(0f, x, 6);
        Assert.Equal(0f, y, 6);
        Assert.Equal(0f, z, 6);

        (float vx, float vy) = StormRenderer.DriftAt(2f, -1f, x, y, 3f);
        Assert.Equal(2f, vx, 6);
        Assert.Equal(-1f, vy, 6);
    }

    [Fact]
    public void Drift_ReachesEveryDirectionInThreeDimensionsAndNotJustTheHorizontal()
    {
        // The air pushes a drop about in three dimensions, not two. Every octant must be reached, and
        // the vertical component must be a real share of the push rather than a rounding error — a
        // drift confined to the horizontal plane leaves the fall looking the same from above as from
        // the side, which is what "it looks the same at every viewing angle" means.
        int[] octants = new int[8];
        float steepest = 0f;
        const int Samples = 4096;

        Random random = new(20260907);
        for (int i = 0; i < Samples; i++)
        {
            (float x, float y, float z) = StormRenderer.SpawnDrift(
                Spread, random.NextSingle(), random.NextSingle(), random.NextSingle());

            int octant = (x >= 0 ? 1 : 0) | (y >= 0 ? 2 : 0) | (z >= 0 ? 4 : 0);
            octants[octant]++;

            float horizontal = MathF.Sqrt((x * x) + (y * y));
            if (horizontal > 1e-5f)
            {
                steepest = MathF.Max(steepest, MathF.Abs(z) / horizontal);
            }
        }

        Assert.True(octants.Min() > 0, "the drift never reaches some octants, so it is not three-dimensional");
        Assert.True(
            octants.Max() < octants.Min() * 1.4,
            $"the drift is lopsided: busiest octant {octants.Max()}, quietest {octants.Min()}");
        Assert.True(
            steepest > 0.3f,
            $"the steepest push is only {steepest:0.000} of its own horizontal, so the vertical share is "
                + "decorative rather than real");
    }

    [Fact]
    public void Terminal_FallsSlowerForASmallerDrop()
    {
        // A raindrop's terminal speed follows its size — drizzle drifts down, a big drop drives. The
        // emitter spawns sizes over more than a threefold range, so the fall speeds must spread too,
        // and it is that spread which turns one sideways push into many different angles.
        float small = StormRenderer.TerminalFor(Fall, 0.3f);
        float large = StormRenderer.TerminalFor(Fall, 1.0f);

        Assert.True(small < large, "small drops must not fall as fast as large ones");
        Assert.True(
            large > small * 1.6f,
            $"the fall speeds barely differ ({small:0.00} against {large:0.00} m/s), so every streak is "
                + "the same length and leans the same way");
        Assert.Equal(Fall, large, 3);
        Assert.True(small > 0.5f, "a drop must still fall");
    }

    /// <summary>How far off vertical a drop with this sideways velocity and fall speed falls, in degrees.</summary>
    private static float Lean((float X, float Y) drift, float terminal) =>
        MathF.Atan2(MathF.Sqrt((drift.X * drift.X) + (drift.Y * drift.Y)), terminal) * 180f / MathF.PI;
}
