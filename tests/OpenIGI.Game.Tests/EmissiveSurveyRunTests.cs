using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Runs the emissive survey over retail level 1.
/// </summary>
/// <remarks>
/// The survey is how a level's light fixtures are found at all, since I.G.I. declares no lights and
/// every lamp is geometry with a texture. This pins that it still separates them from ordinary
/// surfaces: a change that made every sunlit wall score highly would not fail anything else.
/// </remarks>
public sealed class EmissiveSurveyRunTests
{
    [RequiresGameDataFact]
    public void LevelOnesLightFixtureStandsOutFromEverySurfaceAroundIt()
    {
        WorldScene scene = RetailLevelOneGround.Scene();

        IReadOnlyList<EmissiveTextureSurvey.Finding> findings =
            EmissiveTextureSurvey.Survey(scene, RetailLevelOneGround.Resources());

        Assert.NotEmpty(findings);

        // 236_01_1 is lit pure white on all thirty of its uses. Nothing in the level beats it.
        EmissiveTextureSurvey.Finding top = findings[0];

        Assert.Equal("236_01_1", top.Texture);
        Assert.True(top.MeanLuminance > 0.95f, $"the top candidate averages only {top.MeanLuminance:F2}");
        Assert.Equal(top.Uses, top.BrightUses);

        // And the separation has to be from the LEVEL, not from the runner-up. The survey measures each
        // sub-mesh's own corner of its lightmap page rather than the whole page, so the emitters no
        // longer have to beat each other to be found -- a corridor of identical tubes now scores every
        // one of them highly, which is the point, and comparing the top two would call that a failure.
        // What has to hold is that the emitters are a small, sharply separated minority: on level 1,
        // eleven textures out of 217 reach the gate and everything past the fifteenth scores nothing at
        // all, because an ordinary wall is bright in a few places and pale in the rest.
        int atGate = findings.Count(f => f.Score >= LampDetector.MinimumScore);

        Assert.InRange(atGate, 1, findings.Count / 8);
        Assert.True(
            findings[^1].Score <= 0.001f,
            $"the weakest of {findings.Count} findings still scores {findings[^1].Score:F3}");
    }

    /// <summary>
    /// Level 1's ceiling lamps are found by their shape, including the one the exporter had to name by hand.
    /// </summary>
    /// <remarks>
    /// <c>373_01_1</c> is the small bright tube whose lightmap used to average away inside a whole
    /// ceiling model's atlas; the reference exporter carries a hand-written list with that one texture in it
    /// for exactly that reason. Measuring the sub-mesh's own region of the page instead, and confirming
    /// against the shape of the geometry, finds it with no list -- and finding it is the thing that says
    /// the detection reaches a real fixture rather than only the obvious panels.
    /// </remarks>
    [RequiresGameDataFact]
    public void LevelOnesCeilingLampsAreFoundByTheirShape()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        ResourceRegistry resources = RetailLevelOneGround.Resources();

        LampDetector.Result detected = LampDetector.Detect(
            scene, resources, EmissiveTextureSurvey.Survey(scene, resources));

        Assert.Contains("373_01_1", detected.Confirmed);

        // A confirmed lamp glows at the lamp strength; a merely-bright surface does not. 236_01_1 tops
        // the bake's ranking and is NOT a fixture -- it is bright because it is lit -- so the two
        // conclusions have to disagree about it, which is the whole reason the shape test exists.
        Assert.Equal(LampDetector.LampStrength, detected.Strengths["373_01_1"]);
        Assert.DoesNotContain("236_01_1", detected.Confirmed);

        // And every confirmed lamp becomes a placed light that faces somewhere sensible: down, or at
        // worst sideways. A fixture pointing UP is one lighting the slab it is bolted to.
        IReadOnlyList<LightFixture> fixtures = LightFixtureExtractor.ProposeFromScene(
            scene, detected.Confirmed, LampDetector.Tint(resources));

        Assert.NotEmpty(fixtures);
        Assert.All(fixtures, fixture => Assert.True(
            fixture.Direction.Z <= 0f,
            $"{fixture.Texture} faces upward at {fixture.Direction.Z:F2}"));

        // At least one lamp has to actually face downward, or the facing is being carried but never
        // derived and every fixture is silently a point light again.
        Assert.Contains(fixtures, fixture => fixture.Direction.Z < -0.5f);
    }
}
