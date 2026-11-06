using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Project I.G.I.'s ground materials painted through the <c>terrain.bit</c> masks its <c>TextureModifier</c>s read.</summary>
public sealed class Igi1TerrainPaintTests
{
    /// <summary>
    /// Painting a material at a point makes the renderer's coverage test say that material covers it, undoing puts it back,
    /// and saving writes a <c>terrain.bit</c> that reads back with the paint.
    /// </summary>
    [RequiresGameDataFact]
    public void Paint_covers_the_ground_undoes_and_saves()
    {
        (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(1);
        TerrainSurface surface = TerrainSurface.Build(terrain, root);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(surface);
        Assert.Equal(surface.Layers.Length, editable.PaintLayers.Count);
        Assert.NotEmpty(editable.PaintLayers);

        TerrainPaintLayer layer = editable.PaintLayers[0];
        TerrainTileLayer tile = surface.Layers[layer.Index];
        double x = (layer.MinX + layer.MaxX) / 2, y = (layer.MinY + layer.MaxY) / 2;
        double units = WorldScene.UnitsPerMetre;
        bool before = tile.Covers(x * units, y * units);
        List<TerrainPaintCell> changed = [];
        int painted = 0;
        editable.Painted += (_, _, _, _) => painted++;

        editable.Paint(layer.Index, x, y, layer.CellMetres * 3, !before, changed);
        Assert.NotEmpty(changed);
        Assert.Equal(1, painted);
        Assert.Equal(!before, tile.Covers(x * units, y * units));

        IReadOnlyList<(string Path, byte[] Bytes)> files = editable.ChangedFiles(_ => null);
        (string path, byte[] bytes) = Assert.Single(files);
        Assert.Equal(Igi1EditableTerrain.MaskMissionPath, path);
        Assert.True(TerrainVisibility.Read(bytes).TryGetGrid(tile.BitmapId, out TerrainVisibilityGrid saved));
        Assert.Equal(!before, saved.IsSet(changed[0].X, changed[0].Y));
        Assert.All(changed, c => Assert.Equal(layer.Index, c.Grid));

        editable.SetPaint(changed, after: false);
        Assert.Equal(before, tile.Covers(x * units, y * units));

        editable.MarkSaved();
        Assert.Empty(editable.ChangedFiles(_ => null));
    }
}
