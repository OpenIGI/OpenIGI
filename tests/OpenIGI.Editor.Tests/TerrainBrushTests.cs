using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Terrain;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>The terrain brush and its strokes, over a flat grid of one-metre samples held in memory.</summary>
public sealed class TerrainBrushTests
{
    /// <summary>Raising lifts the middle most, fades to nothing at the radius, and tells the terrain what changed.</summary>
    [Fact]
    public void Raising_lifts_the_middle_and_leaves_the_edge()
    {
        FlatTerrain terrain = new(64, 64);
        TerrainBrush brush = new() { Mode = TerrainBrushMode.Raise, RadiusMetres = 8.0, Strength = 2.0 };
        TerrainStroke stroke = new(terrain);

        brush.Apply(terrain, new Vector3d(32, 32, 0), 1.0, 0.0, stroke);

        Assert.Equal(2.0, terrain.Grid.GetHeight(32, 32), 6);
        Assert.InRange(terrain.Grid.GetHeight(36, 32), 0.1, 1.9);
        Assert.Equal(0.0, terrain.Grid.GetHeight(40, 32));
        Assert.Equal(0.0, terrain.Grid.GetHeight(10, 10));
        Assert.NotEmpty(terrain.Refreshed);
        Assert.False(stroke.IsEmpty);
    }

    /// <summary>A stroke undoes to the ground before it and redoes to the ground after it.</summary>
    [Fact]
    public void A_stroke_undoes_and_redoes()
    {
        FlatTerrain terrain = new(32, 32);
        TerrainBrush brush = new() { Mode = TerrainBrushMode.Lower, RadiusMetres = 5.0, Strength = 1.0 };
        TerrainStroke stroke = new(terrain);
        brush.Apply(terrain, new Vector3d(16, 16, 0), 0.5, 0.0, stroke);
        brush.Apply(terrain, new Vector3d(17, 16, 0), 0.5, 0.0, stroke);
        stroke.Finish();
        double after = terrain.Grid.GetHeight(16, 16);
        Assert.True(after < 0.0);

        stroke.Undo();
        Assert.Equal(0.0, terrain.Grid.GetHeight(16, 16));
        stroke.Apply();
        Assert.Equal(after, terrain.Grid.GetHeight(16, 16));
    }

    /// <summary>Smoothing pulls a spike toward its neighbours; flattening pulls the ground toward the height asked for.</summary>
    [Fact]
    public void Smoothing_and_flattening_pull_toward_their_targets()
    {
        FlatTerrain terrain = new(16, 16);
        terrain.Grid.SetHeight(8, 8, 9.0);
        TerrainBrush brush = new() { Mode = TerrainBrushMode.Smooth, RadiusMetres = 3.0, Strength = 4.0 };
        brush.Apply(terrain, new Vector3d(8, 8, 0), 0.5, 0.0, new TerrainStroke(terrain));
        Assert.InRange(terrain.Grid.GetHeight(8, 8), 0.5, 8.9);

        brush.Mode = TerrainBrushMode.Flatten;
        for (int i = 0; i < 20; i++)
        {
            brush.Apply(terrain, new Vector3d(8, 8, 0), 0.5, 3.0, new TerrainStroke(terrain));
        }

        Assert.Equal(3.0, terrain.Grid.GetHeight(8, 8), 2);
    }

    /// <summary>A ray from above meets flat ground where it crosses its height, and between samples the ground is interpolated.</summary>
    [Fact]
    public void A_ray_finds_the_ground()
    {
        FlatTerrain terrain = new(64, 64);
        terrain.Grid.SetHeight(20, 20, 2.0);
        terrain.Grid.SetHeight(21, 20, 4.0);
        Assert.True(terrain.Grid.TrySample(20.5, 20.0, out double between));
        Assert.Equal(3.0, between, 6);

        Assert.True(terrain.TryRaycast(new Vector3d(10, 10, 50), new Vector3d(1, 1, -2), 500, out Vector3d hit));
        Assert.Equal(0.0, hit.Z, 6);
        Assert.Equal(35.0, hit.X, 1);
        Assert.False(terrain.TryRaycast(new Vector3d(10, 10, 50), new Vector3d(0, 0, 1), 500, out _));
    }

    private sealed class FlatTerrain(int width, int height) : IEditableTerrain
    {
        public Grid Grid { get; } = new(width, height);

        public List<(int Left, int Top, int Right, int Bottom)> Refreshed { get; } = [];

        public IReadOnlyList<ITerrainHeightGrid> Grids => [Grid];

        public bool TryGetHeight(double xMetres, double yMetres, out double zMetres) => Grid.TrySample(xMetres, yMetres, out zMetres);

        public void Refresh(ITerrainHeightGrid grid, int left, int top, int right, int bottom) => Refreshed.Add((left, top, right, bottom));

        public IReadOnlyList<(string MissionPath, byte[] Bytes)> ChangedFiles(Func<string, ReadOnlyMemory<byte>?> read) => [];

        public void MarkSaved()
        {
        }
    }

    // Samples one metre apart, sample (x, y) standing at (x, y).
    private sealed class Grid(int width, int height) : ITerrainHeightGrid
    {
        private readonly double[] _heights = new double[width * height];

        public string Name => "flat";

        public int Width => width;

        public int Height => height;

        public bool IsChanged => _heights.Any(static h => h != 0.0);

        public (double X, double Y) SampleMetres(int x, int y) => (x, y);

        public (double Column, double Row) ToGrid(double xMetres, double yMetres) => (xMetres, yMetres);

        public double GetHeight(int x, int y) => _heights[(y * width) + x];

        public void SetHeight(int x, int y, double zMetres) => _heights[(y * width) + x] = zMetres;
    }
}
