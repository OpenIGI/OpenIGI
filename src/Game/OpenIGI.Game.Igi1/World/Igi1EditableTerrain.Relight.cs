using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Relighting shaped ground: the terrain lightmap texels over moved ground lit again for their new slope and shadow.</summary>
/// <remarks>
/// <para>
/// A lightmap texel is the sun's share of the light (the ramp is <c>sun × texel + ambient</c>), placed by world position over
/// the cell its <c>TerrainLightMap</c> task names, and the original reads the pages as they are (<c>0x4C6160</c>). How the
/// shipped pages were baked is not known, so a texel is not recomputed from nothing: it moves by the change in what the sun
/// reaches there (<c>max(0, N·L)</c>, nought where the ground towards the sun hides it), scaled the way the page's own
/// unchanged texels relate to that same measure. Shadows baked from buildings and trees stay where they were.
/// </para>
/// </remarks>
public sealed partial class Igi1EditableTerrain
{
    // Below this share of sun a texel says little about how the page was scaled.
    private const double CalibrationFloor = 0.3;

    // The pages whose texels changed, by page index; empty when the level has no lightmaps or sun to relight by.
    private Dictionary<int, byte[]> Relight(List<Grid> moved)
    {
        Dictionary<int, byte[]> pages = [];
        if (_surface.Terrain.Lightmaps is not { } lightmaps || _surface.SunDirection is not { } sun || sun.Z <= 0f)
        {
            return pages;
        }

        Vector3d towards = new Vector3d(sun.X, sun.Y, sun.Z).Normalized();
        foreach (TerrainLightBindings.Entry entry in _surface.Lights.Entries)
        {
            TerrainLightmapPage page = entry.Page < lightmaps.Pages.Count ? lightmaps.Pages[entry.Page] : default;
            if (page.IsEmpty)
            {
                continue;
            }

            foreach (Grid grid in moved)
            {
                (double minX, double minY, double maxX, double maxY) = grid.Square;
                double span = entry.Span;
                if (entry.Binding.MinX > maxX || entry.Binding.MinX + span < minX || entry.Binding.MinY > maxY || entry.Binding.MinY + span < minY)
                {
                    continue;
                }

                byte[] texels = pages.TryGetValue(entry.Page, out byte[]? done) ? done : page.Texels.ToArray();
                if (RelightPage(grid, entry, page.Size, texels, towards))
                {
                    pages[entry.Page] = texels;
                }
            }
        }

        return pages;
    }

    // One page's texels over one grid; whether any changed.
    private static bool RelightPage(Grid grid, TerrainLightBindings.Entry entry, int size, byte[] texels, Vector3d towards)
    {
        double step = entry.Span / (double)size;
        List<(int Index, double Before, double After)> changed = [];
        List<double> scales = [];
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                int index = (j * size) + i;
                double x = entry.Binding.MinX + (i * step);
                double y = entry.Binding.MinY + (j * step);
                (double column, double row) = grid.GridOf(x, y);
                if (column < 1.0 || row < 1.0 || column > grid.Width - 2 || row > grid.Height - 2 || texels[index] == 0)
                {
                    continue;
                }

                double before = SunShare(grid, column, row, towards, now: false);
                double after = SunShare(grid, column, row, towards, now: true);
                if (Math.Abs(after - before) * 255.0 >= 0.5)
                {
                    changed.Add((index, before, after));
                }
                else if (before > CalibrationFloor)
                {
                    scales.Add(texels[index] / (255.0 * before));
                }
            }
        }

        if (changed.Count == 0)
        {
            return false;
        }

        scales.Sort();
        double scale = scales.Count >= 16 ? scales[scales.Count / 2] : 1.0;
        foreach ((int index, double before, double after) in changed)
        {
            texels[index] = (byte)Math.Clamp((int)Math.Round(texels[index] + (255.0 * scale * (after - before))), 1, 255);
        }

        return true;
    }

    // How much of the sun reaches the ground at a point of a grid: the cosine of its slope to the sun, nought where the
    // grid's own ground towards the sun stands in the way.
    private static double SunShare(Grid grid, double column, double row, Vector3d towards, bool now)
    {
        double spacing = grid.SpacingMetres;
        double slopeX = (grid.HeightAt(column + 1.0, row, now) - grid.HeightAt(column - 1.0, row, now)) / (2.0 * spacing);
        double slopeY = (grid.HeightAt(column, row + 1.0, now) - grid.HeightAt(column, row - 1.0, now)) / (2.0 * spacing);
        Vector3d normal = new Vector3d(-slopeX, -slopeY, 1.0).Normalized();
        double facing = Vector3d.Dot(normal, towards);
        if (facing <= 0.0)
        {
            return 0.0;
        }

        // March towards the sun a sample at a time, as far as the grid reaches.
        double horizontal = Math.Sqrt((towards.X * towards.X) + (towards.Y * towards.Y));
        if (horizontal > 1e-6)
        {
            double rise = towards.Z / horizontal * spacing;
            double dx = towards.X / horizontal;
            double dy = towards.Y / horizontal;
            double height = grid.HeightAt(column, row, now) + 0.05;
            for (int n = 1; n < grid.Width; n++)
            {
                double c = column + (dx * n);
                double r = row + (dy * n);
                if (c < 0.0 || r < 0.0 || c > grid.Width - 1 || r > grid.Height - 1)
                {
                    break;
                }

                if (grid.HeightAt(c, r, now) > height + (rise * n))
                {
                    return 0.0;
                }
            }
        }

        return facing;
    }
}
