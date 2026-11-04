using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Game.World;

namespace OpenIGI.Editor.Terrain;

/// <summary>
/// Shapes a blank mission's ground as its wizard asked, as one undoable step: flat, or rolling from a seed, with the player and
/// the exit lifted onto it.
/// </summary>
/// <remarks>
/// A title whose height grids cover the whole map (I.G.I. 2) is shaped everywhere. Project I.G.I.'s ground is an octree its
/// height maps only touch here and there, so a play area round the player is made of mountain areas (512 m squares the
/// octree is rebuilt in, saved into the mission's own <c>terrain/terrain.ctr</c> and <c>.cmd</c>, which the original game
/// loads from the mission's folder), and eases back into the level's ground at its edge.
/// </remarks>
internal static class BlankGroundCommand
{
    /// <summary>How many 512 m squares a side the play area is, centred on the player, on a title shaped in areas.</summary>
    public const int AreasAcross = 1;

    private const double AreaMetres = 512;

    /// <summary>Shapes the ground, then returns the step that does it, applied already, for the mission's history.</summary>
    /// <param name="terrain">The open mission's ground.</param>
    /// <param name="document">The mission, whose player and exit stand on it.</param>
    /// <param name="ground">Flat, or seeded.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The step, or <see langword="null"/> when the ground could not be shaped there.</returns>
    public static IEditorCommand? Shape(IEditableTerrain terrain, MissionDocument document, BlankGround ground, int seed)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(document);
        if (ground == BlankGround.Keep
            || document.EnumerateTasks().FirstOrDefault(static t => t.TypeName == "HumanPlayer") is not { } player
            || Metres(player) is not { } start
            || !terrain.TryGetHeight(start.X, start.Y, out double startHeight))
        {
            return null;
        }

        MissionTask? exit = document.EnumerateTasks().FirstOrDefault(static t => t is { TypeName: "AreaActivate", Label: "Exit" });
        Vector3d? exitAt = exit is null ? null : Metres(exit);
        double exitHeight = exitAt is { } e && terrain.TryGetHeight(e.X, e.Y, out double h) ? h : startHeight;
        List<(double X, double Y)> pads = [(start.X, start.Y)];
        if (exitAt is { } pad)
        {
            pads.Add((pad.X, pad.Y));
        }

        List<IEditorCommand> steps = [];
        IReadOnlyList<ITerrainHeightGrid> grids = terrain.Grids;
        (double Left, double Top, double Right, double Bottom)? region = null;
        if (terrain.IsPartial)
        {
            // A play area of mountain areas round the player, each a 512 m square of the octree rebuilt as shaped.
            HashSet<ITerrainHeightGrid> before = [.. terrain.Grids];
            int half = AreasAcross / 2;
            for (int i = -half; i <= half; i++)
            {
                for (int j = -half; j <= half; j++)
                {
                    AddMountainCommand add = new(terrain, new Vector3d(start.X + (i * AreaMetres), start.Y + (j * AreaMetres), startHeight));
                    if (add.TryApply())
                    {
                        steps.Add(add);
                    }
                }
            }

            grids = [.. terrain.Grids.Where(g => !before.Contains(g))];
            if (grids.Count == 0)
            {
                return null;
            }

            region = Bounds(grids);
        }

        TerrainStroke stroke = new(terrain);
        foreach (ITerrainHeightGrid grid in grids)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    (double wx, double wy) = grid.SampleMetres(x, y);
                    double original = grid.GetHeight(x, y);
                    double edge = region is { } r ? Math.Min(Math.Min(wx - r.Left, r.Right - wx), Math.Min(wy - r.Top, r.Bottom - wy)) : double.PositiveInfinity;
                    double shaped = BlankGroundShape.Height(ground, seed, wx, wy, original, startHeight, pads, edge);
                    if (Math.Abs(shaped - original) > 1e-4)
                    {
                        stroke.Remember(grid, x, y);
                        grid.SetHeight(x, y, shaped);
                    }
                }
            }

            terrain.Refresh(grid, 0, 0, grid.Width - 1, grid.Height - 1);
        }

        terrain.Settle();
        stroke.Finish();
        if (!stroke.IsEmpty)
        {
            steps.Add(stroke);
        }

        // The player and the exit keep their height above the ground, now that the ground has moved under them.
        steps.Add(Lift(document, player, start, startHeight, terrain));
        if (exit is not null && exitAt is { } exitPoint)
        {
            steps.Add(Lift(document, exit, exitPoint, exitHeight, terrain));
        }

        return new CompositeCommand(ground == BlankGround.Flat ? "Flatten Ground" : $"Generate Ground (seed {seed})", steps);
    }

    private static SetParameterCommand Lift(MissionDocument document, MissionTask task, Vector3d at, double groundBefore, IEditableTerrain terrain)
    {
        double after = terrain.TryGetHeight(at.X, at.Y, out double h) ? h : groundBefore;
        double z = (at.Z + (after - groundBefore)) * WorldUnits.PerMetre;
        return SetParameterCommand.Numbers(document, task, "Position", [at.X * WorldUnits.PerMetre, at.Y * WorldUnits.PerMetre, z]);
    }

    private static Vector3d? Metres(MissionTask task) =>
        task.FindParameter("Position") is { } position && position.TryGetNumbers(out double[] values) && values.Length >= 3
            ? new Vector3d(values[0] / WorldUnits.PerMetre, values[1] / WorldUnits.PerMetre, values[2] / WorldUnits.PerMetre)
            : null;

    private static (double Left, double Top, double Right, double Bottom) Bounds(IReadOnlyList<ITerrainHeightGrid> grids)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (ITerrainHeightGrid grid in grids)
        {
            foreach ((int x, int y) in (ReadOnlySpan<(int, int)>)[(0, 0), (grid.Width - 1, grid.Height - 1)])
            {
                (double wx, double wy) = grid.SampleMetres(x, y);
                left = Math.Min(left, wx);
                right = Math.Max(right, wx);
                top = Math.Min(top, wy);
                bottom = Math.Max(bottom, wy);
            }
        }

        return (left, top, right, bottom);
    }
}
