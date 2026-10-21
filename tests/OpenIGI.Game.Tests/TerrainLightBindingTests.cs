using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The terrain lightmap bindings a level's <c>TerrainLightMap</c> tasks publish, and the ramp their texels
/// are read through: the ground's light, which the vanilla picture had gone without.
/// </summary>
/// <remarks>
/// The rules are IGI.exe's (<c>sub_4F8780</c>, <c>sub_4F8EF0</c>, <c>sub_510010</c>, <c>sub_50FAE0</c>); see
/// <see cref="TerrainLightBindings"/>. Without them mission 1's ground at the spawn read 99/94/91 -- its tiles
/// unlit -- where the original shows 74/61/55.
/// </remarks>
public sealed class TerrainLightBindingTests
{
    // Mission 1's player start, in engine units.
    private static readonly Vector3d Spawn = new(24578514.0, -55852816.0, 174419680.0);

    [RequiresGameDataFact]
    public void MissionOneBindsEveryPageItShips()
    {
        RetailMissionData data = RetailMissionData.Get(1);
        TerrainData terrain = data.Mission.Terrain!;
        TerrainLightBindings bindings = TerrainLightBindings.Build(terrain, data.Root);

        // One task per page, every one of them resolvable.
        Assert.Equal(terrain.Lightmaps!.Pages.Count, bindings.Entries.Count);
        Assert.Equal(0, bindings.Skipped);
        Assert.Equal(
            Enumerable.Range(0, bindings.Entries.Count),
            bindings.Entries.Select(e => e.Page).Order());
    }

    [RequiresGameDataFact]
    public void TheSpawnStandsOnTheCompoundsPage()
    {
        RetailMissionData data = RetailMissionData.Get(1);
        TerrainLightBindings bindings = TerrainLightBindings.Build(data.Mission.Terrain!, data.Root);

        // Task 267 (Level 10, Size 128) at (24638438, -56057824): its cell is the 2^21-unit node the
        // position masks down to.
        TerrainLightBinding binding = bindings.Resolve(new TerrainChunk(Spawn, 12, 0, 0));
        Assert.True(binding.IsBound);
        Assert.Equal(267, binding.Page);
        Assert.Equal(23068672L, binding.MinX);
        Assert.Equal(-56623104L, binding.MinY);
        Assert.Equal(1.0 / 2097152.0, binding.InverseSpan);
        Assert.Equal(0.5 / 128.0, binding.HalfTexel);
    }

    [RequiresGameDataFact]
    public void AChunkCoarserThanTheCellIsNotLitByIt()
    {
        RetailMissionData data = RetailMissionData.Get(1);
        TerrainLightBindings bindings = TerrainLightBindings.Build(data.Mission.Terrain!, data.Root);

        // A node above level 10 spans more than the cell, so the descent never enters the cell for it.
        Assert.NotEqual(267, bindings.Resolve(new TerrainChunk(Spawn, 9, 0, 0)).Page);
    }

    [RequiresGameDataFact]
    public void MissionOnesRampIsItsSunOverItsTerrainAmbient()
    {
        RetailMissionData data = RetailMissionData.Get(1);
        TerrainLightBindings bindings = TerrainLightBindings.Build(data.Mission.Terrain!, data.Root);

        Assert.True(DirectionalLightSettings.TryRead(data.Root, out DirectionalLightSettings sun));
        Vector3f ambient = TerrainLightBindings.TerrainAmbient(data.Root);
        Assert.NotEqual(default, ambient);
        Assert.Equal(TerrainLightBindings.RampFrom(sun.Colour, ambient), bindings.Ramp);

        // The ground warms as it brightens: the sun is warmer than the sky's ambient.
        Bgra32 lit = bindings.Ramp[160];
        Assert.True(lit.R > lit.B);
    }

    [Fact]
    public void TheRampIsTheSunTimesTheIndexOverTheAmbientClampedToAByte()
    {
        Bgra32[] ramp = TerrainLightBindings.RampFrom(new Vector3f(1f, 0.5f, 0.25f), new Vector3f(0.1f, 0.2f, 0.3f));

        // min(255, (int)(sun * i + ambient * 255)), alpha opaque but for index zero.
        Assert.Equal(new Bgra32(101, 101, 125, 255), ramp[100]);
        Assert.Equal(new Bgra32(76, 51, 25, 0), ramp[0]);
        Assert.Equal(255, ramp[255].R);
        Assert.Equal(255, ramp[255].A);
    }

    [Fact]
    public void ATexelIsCentredOnTheCellsGrid()
    {
        // A 1024-unit cell of 64 texels: u = (x - min) / cell + 0.5 / size.
        TerrainLightBinding binding = new(5, 1000, 2000, 1.0 / 1024.0, 0.5 / 64.0);

        (double u, double v) = binding.Project(1000 + 512, 2000 + 256);
        Assert.Equal(0.5 + (0.5 / 64.0), u, 12);
        Assert.Equal(0.25 + (0.5 / 64.0), v, 12);

        // Texel (c, r) is centred on min + (c, r) * cell / size.
        (u, v) = binding.Project(1000 + (3 * 16), 2000 + (7 * 16));
        Assert.Equal((3 + 0.5) / 64.0, u, 12);
        Assert.Equal((7 + 0.5) / 64.0, v, 12);
    }
}
