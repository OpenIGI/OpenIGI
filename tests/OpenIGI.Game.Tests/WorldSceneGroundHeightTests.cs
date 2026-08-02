using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the scene's ground reference against a real level.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a bug it would have caught outright. A level's coordinates carry a large
/// absolute offset — level 1's ground sits some 42 km from the world origin — and the volumetric fog
/// took its base height straight from the sky plane's four metres, as though that were a world Z. The
/// medium was therefore asked for fog forty-two kilometres underground, its height term collapsed to
/// <c>exp(-42000/30)</c>, and the pass ran every frame producing exactly nothing.
/// </para>
/// <para>
/// So the property worth guarding is not that the median is computed correctly — it is that the
/// number it produces is somewhere a player could stand.
/// </para>
/// </remarks>
public sealed class WorldSceneGroundHeightTests
{
    [RequiresGameDataFact]
    public void TheGroundReferenceLandsOnGroundTheTerrainAgrees()
    {
        (TerrainData _, double _, double _, double standableUnits) = RetailLevelOneGround.Find();
        WorldScene scene = RetailLevelOneGround.Scene();

        double referenceUnits = scene.GroundHeight * WorldScene.UnitsPerMetre;
        double apartMetres = Math.Abs(referenceUnits - standableUnits) / WorldScene.UnitsPerMetre;

        // Generous on purpose: the median is an approximation of "the ground" and level 1 is not flat.
        // The failure being guarded against is not a few metres out, it is kilometres out.
        Assert.True(
            apartMetres < 40.0,
            $"ground reference is {apartMetres:F1} m from a point the terrain says is standable");
    }
}
