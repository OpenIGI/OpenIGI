using OpenIGI.Formats;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

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
            EmissiveTextureSurvey.Survey(scene, RetailLevelOneGround.Resources(), AssetFormatProfile.Igi1);

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
        // What has to hold is that the surfaces the artists lit like sources are a small, sharply
        // separated minority: on level 1, eleven textures out of 217 score above a quarter and
        // everything past the fifteenth scores nothing at all, because an ordinary wall is bright in a
        // few places and pale in the rest.
        int lit = findings.Count(f => f.Score >= 0.25f);

        Assert.InRange(lit, 1, findings.Count / 8);
        Assert.True(
            findings[^1].Score <= 0.001f,
            $"the weakest of {findings.Count} findings still scores {findings[^1].Score:F3}");
    }

    /// <summary>
    /// Level 1's ceiling lamps are found without a list, and the things shaped like them are not.
    /// </summary>
    /// <remarks>
    /// <c>316_01_1</c> is level 1's <c>LIGHT</c> fitting, and the lightmap survey loses it outright --
    /// it scores exactly nought across all twenty-four of its uses, since the artists lit those rooms
    /// without painting the fitting bright. Finding it anyway is what says the detection reaches a real
    /// fixture rather than only the obvious panels.
    /// </remarks>
    [RequiresGameDataFact]
    public void LevelOnesCeilingLampsAreFoundByTheirShape()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        ResourceRegistry resources = RetailLevelOneGround.Resources();

        LampDetector.Lamps lamps = LampDetector.Detect(scene, resources);

        Assert.Contains("316_01_1", lamps.Textures);

        // Every lamp is normalised to the same emitted radiance rather than to the same multiple, so
        // that how dark the artist drew a housing stops deciding how brightly it burns. What has to hold
        // is the product, not the multiplier.
        Assert.True(
            lamps.TryGetStrength("316_01_1", 0, out float strength),
            "316_01_1's own model does not draw it as a lamp.");
        Assert.Equal(
            LampDetector.LampRadiance,
            strength * LampDetector.Brightness("316_01_1", resources),
            2);

        // And 236_01_1 is NOT one, though it tops the lightmap ranking with a pure-white bake across all
        // thirty of its uses. It skins office cabinets: it is bright because it is LIT. That is the whole
        // reason the bake cannot decide this on its own -- taking its word for it made a cabinet side
        // into a flat glowing panel, because an emissive surface in this renderer is an unlit one.
        // ...on any sub-mesh the detector picks for itself. It IS a lamp on the one model a person named
        // by hand, which is a different claim and the reason the manifest exists.
        Assert.DoesNotContain("236_01_1", lamps.Textures);

        // Every lamp becomes a placed light facing somewhere sensible: down, or at worst sideways. A
        // fixture pointing UP is one lighting the slab it is bolted to.
        IReadOnlyList<LightFixture> fixtures = LightFixtureExtractor.ProposeFromScene(
            scene, lamps, LampDetector.Tint(resources));

        Assert.NotEmpty(fixtures);
        Assert.All(fixtures, fixture => Assert.True(
            fixture.Direction.Z <= 0f,
            $"{fixture.Texture} faces upward at {fixture.Direction.Z:F2}"));

        // At least one lamp has to actually face downward, or the facing is being carried but never
        // derived and every fixture is silently a point light again.
        Assert.Contains(fixtures, fixture => fixture.Direction.Z < -0.5f);
    }

    /// <summary>Things merely SHAPED like a ceiling lamp are not lit as one.</summary>
    /// <remarks>
    /// <para>
    /// Every model here passes the shape test and none of them is a lamp. <c>373_01_1</c> is a
    /// <c>HOLDER</c> — a pale grey mounting plate 1.2 m across and 15 cm thick, hanging six metres up
    /// with its faces summing straight down, which is a fluorescent batten in every measurable respect.
    /// <c>313_02_1</c> is a security camera, <c>490_02_1</c> an antenna, <c>204_01_1</c> a radio. What
    /// keeps them dark is that none of them is SKINNED like a fitting: their pages are their own, and
    /// no model the artists called a light draws from any of them.
    /// </para>
    /// <para>
    /// This is the whole of what the shape test could not do. Before the texture gate it proposed 423
    /// sub-meshes across the fourteen retail levels and 263 were wrong — these four among them, along
    /// with soldiers, chairs, toilets and dropped weapons.
    /// </para>
    /// </remarks>
    [RequiresGameDataFact]
    public void LevelOnesFittingShapedFurnitureIsNotALamp()
    {
        WorldScene scene = RetailLevelOneGround.Scene();
        ResourceRegistry resources = RetailLevelOneGround.Resources();

        LampDetector.Lamps lamps = LampDetector.Detect(scene, resources);

        Assert.NotEmpty(lamps.Textures);

        foreach ((string model, string what) in new[]
        {
            ("373_01_1", "HOLDER, a mounting plate"),
            ("313_02_1", "CAMERA"),
            ("490_02_1", "ANTENA"),
            ("204_01_1", "RADIO"),
        })
        {
            Assert.False(
                LampDetector.IsNamedLightFixture(model),
                $"{model} ({what}) is being read as a light fitting by name.");

            for (int slot = 0; slot < 16; slot++)
            {
                Assert.False(
                    lamps.Contains(model, slot),
                    $"{model} slot {slot} ({what}) is lit as a lamp.");
            }
        }

        // And the gate has not simply switched detection off: level 1's own fittings, and the garage's
        // ceiling strip that is a sub-mesh of the garage shell rather than a lamp model of its own, are
        // still found with nothing named by hand.
        Assert.True(lamps.Contains("316_01_1", 0), "the LIGHT fitting is no longer found.");
        Assert.True(lamps.Contains("410_02_1", 8), "the garage ceiling strip is no longer found.");
    }
}
