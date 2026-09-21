using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Where the menu's backdrop camera is put, for a game that places things in the sky.</summary>
/// <remarks>
/// <para>
/// The backdrop orbits whatever the level put in it, picked by triangle count and model radius. Project
/// I.G.I. never showed the flaw in that, because nothing in it stands a kilometre above the ground.
/// </para>
/// <para>
/// I.G.I. 2's first mission is a parachute drop and it places the aircraft: a 14,793-triangle
/// <c>c-130bay</c> at 4,630 m, with drop props at 15,140 m, over a level whose own placements run from
/// about −17 to +39 m. The backdrop orbited the aeroplane, four and a half kilometres up, against an
/// empty sky — a black frame with a dark ridge along the bottom.
/// </para>
/// </remarks>
public sealed class Igi2ShowcasePointTests
{
    /// <summary>How far from the middle of a level its backdrop may be placed.</summary>
    /// <remarks>
    /// Generous: some of these levels have real vertical spread, and one picks a structure 341 m above
    /// the bulk of its placements, which is a hillside and not an aeroplane. What this rejects is the
    /// kilometre.
    /// </remarks>
    private const double ToleranceMetres = 600.0;

    /// <summary>Every level's backdrop stands near the level, not in the sky above it.</summary>
    [RequiresTitleDataFact("igi2")]
    public void No_backdrop_is_placed_in_the_sky()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            var (built, _) = Igi2LevelScene.Build(level);
            WorldScene scene = built.Scene;

            double[] heights = [.. scene.Placements.Select(p => p.Position.Z).Order()];
            Assert.NotEmpty(heights);
            double median = heights[heights.Length / 2];

            IReadOnlyList<ShowcasePoint> points = MissionShowcasePoints.Extract(
                scene, level.Root, 3, MenuCameraSpec.Default);

            Assert.NotEmpty(points);
            Assert.All(
                points,
                point => Assert.True(
                    Math.Abs(point.Centre.Z - median) <= ToleranceMetres,
                    $"'{level.Directory}' puts a backdrop {point.Centre.Z - median:F0} m from the middle "
                        + $"of its own placements, which sit around {median:F0} m."));
        }
    }
}
