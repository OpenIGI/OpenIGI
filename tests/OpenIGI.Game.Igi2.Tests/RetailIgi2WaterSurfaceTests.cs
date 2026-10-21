using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The water a level places, and the waves that animate it.</summary>
public sealed class RetailIgi2WaterSurfaceTests
{
    /// <summary>Every sheet collects its layers, and no sheet exceeds what the engine reads.</summary>
    /// <remarks>
    /// <c>Water_SetVertexFunctionParameters</c> (<c>igi2ed 0x6474A0</c>) walks a sheet's
    /// <c>WaterLayer</c> children and breaks at four. A sheet authored with more would have layers the
    /// engine never evaluates, so this is worth knowing either way: if the corpus stays inside the
    /// limit, a renderer that honours it is exact; if it did not, the extra layers would be data to
    /// ignore deliberately.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_sheet_collects_its_wave_layers()
    {
        int levels = 0;
        int sheets = 0;
        int waves = 0;
        int over = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2WaterSurface water = Igi2WaterSurface.Build(level.Root);
            if (water.IsEmpty)
            {
                continue;
            }

            levels++;
            sheets += water.Sheets.Count;

            foreach (Igi2WaterSheet sheet in water.Sheets)
            {
                waves += sheet.Waves.Count;
                over += sheet.Waves.Count > Igi2WaterSurface.MaximumActiveLayers ? 1 : 0;

                Assert.True(sheet.Radius > 0, $"Sheet {sheet.TaskId} reaches {sheet.Radius}.");
                Assert.NotEqual(0, sheet.TaskId);
            }
        }

        Assert.Equal(19, levels);
        Assert.Equal(23, sheets);
        Assert.Equal(42, waves);

        // Not one sheet authors more layers than the engine reads, so a renderer honouring the limit of
        // four is exact on retail data rather than merely faithful to the code.
        Assert.Equal(0, over);
    }

    /// <summary>A sheet is about as big as the ground it lies on, which is what says the units are right.</summary>
    /// <remarks>
    /// <c>Size</c> is <c>Water_t::vRadius</c> — a radius, not a side — and like every other extent in the
    /// sequel it is authored in metres and multiplied by 4096. Nothing in the parameter says so, so it is
    /// checked against the one thing in the level whose units are already settled: a terrain tile.
    /// A sheet read in raw world units would come out four thousand times too small.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_sheet_is_the_scale_of_the_ground_it_lies_on()
    {
        int compared = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            Igi2WaterSurface water = Igi2WaterSurface.Build(level.Root);
            Igi2TerrainSurface ground = level.Terrain();
            if (water.IsEmpty || ground.IsEmpty)
            {
                continue;
            }

            double tile = ground.Tiles.Max(t => t.Width * t.TexelWidth);
            foreach (Igi2WaterSheet sheet in water.Sheets)
            {
                compared++;

                // Within three orders of magnitude of a terrain tile's own extent. Loose, because a pond
                // and a sea are both water; tight enough that a factor of 4096 could not survive it.
                Assert.InRange(sheet.Radius, tile / 1000.0, tile * 100.0);
            }
        }

        Assert.True(compared > 5, $"Only {compared} sheets could be compared against ground.");
    }

    /// <summary>The wave clock is the simulation's ticks at sixty a second.</summary>
    /// <remarks>
    /// <c>Water_vTime = Timer_nTicks * 0.016666...</c>. Worth pinning because the whole animation is a
    /// function of it, and a phase that ran at the frame rate rather than the tick rate would look
    /// correct on one machine.
    /// </remarks>
    [Fact]
    public void The_wave_clock_runs_at_the_tick_rate()
    {
        Assert.Equal(0.0, Igi2WaterSurface.TimeFor(0));
        Assert.Equal(1.0, Igi2WaterSurface.TimeFor(60));
        Assert.Equal(2.5, Igi2WaterSurface.TimeFor(150));
    }
}
