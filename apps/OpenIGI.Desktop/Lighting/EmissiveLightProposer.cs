using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;

namespace OpenIGI.Desktop;

/// <summary>
/// Works out which lights a level has, given that it declares none, and which of its surfaces are the
/// lamps those lights come out of.
/// </summary>
/// <remarks>
/// <para>
/// Three steps, each narrowing the one before. <see cref="EmissiveTextureSurvey"/> reads the artists'
/// own bakes and ranks every texture by how consistently it is painted like a source;
/// <see cref="LampDetector"/> checks that ranking against the SHAPE of the geometry that draws it, so a
/// small flat thing above head height is a lamp and a bright sign is not; and
/// <see cref="LightFixtureExtractor"/> turns every place a confirmed emitter is drawn into a placed
/// light with a position, a reach and a facing.
/// </para>
/// <para>
/// Both halves of the conclusion are used and both are written down. The fixtures become real lights in
/// the deferred pass and real emitters in the surface cache's bounce; the emitter strengths go to the
/// material library so that the lamp geometry ITSELF glows. Doing only the first is what made a room
/// look lit by nothing — light on the floor with a grey panel above it.
/// </para>
/// <para>
/// A per-mission fixtures file, once written, is the truth: the survey ran once to propose the list and
/// a human has had the chance to correct it since. Reading it also keeps the lightmap decode and the
/// whole-level geometry walk off the load path entirely.
/// </para>
/// </remarks>
internal static class EmissiveLightProposer
{
    /// <summary>What a level's own geometry says it is lit by.</summary>
    /// <param name="Fixtures">The placed lights, which may be empty.</param>
    /// <param name="Emitters">Which textures glow, and how strongly, as a multiple of their albedo.</param>
    public readonly record struct Proposal(
        IReadOnlyList<LightFixture> Fixtures,
        IReadOnlyDictionary<string, float> Emitters)
    {
        /// <summary>Gets the proposal for a level that has nothing of the kind.</summary>
        public static Proposal None { get; } = new([], new Dictionary<string, float>());
    }

    /// <summary>Proposes a level's lamps, from an authored file or a fresh survey.</summary>
    /// <param name="scene">The built world scene whose placements and textures are surveyed.</param>
    /// <param name="resources">The resource registry the emitter textures are decoded through.</param>
    /// <param name="missionId">The mission the fixtures belong to, which names the per-mission file.</param>
    /// <param name="logger">The log the proposal is reported to.</param>
    /// <returns>The lamps to place and the surfaces they come out of.</returns>
    public static Proposal Propose(
        WorldScene scene, ResourceRegistry resources, int missionId, IIgiLogger logger)
    {
        // Beside the other authored data, one file per mission. When it exists and its version matches
        // it is the truth: the survey ran once to propose the list and a human has had the chance to
        // correct it since, exactly the way authored material detail wins over the detail derived from a
        // texture.
        string fixturesPath = Path.Combine(
            AppContext.BaseDirectory, "Assets", "fixtures", $"mission-{missionId}.json");

        if (LightFixtureStore.TryLoad(
                fixturesPath,
                out IReadOnlyList<LightFixture> saved,
                out IReadOnlyDictionary<string, float> savedEmitters,
                logger))
        {
            return new Proposal(saved, savedEmitters);
        }

        try
        {
            LampDetector.Result detected = LampDetector.Detect(
                scene, resources, EmissiveTextureSurvey.Survey(scene, resources, logger), logger);

            if (detected.Strengths.Count == 0)
            {
                logger.Information("Emissive", "Nothing in this level looks enough like a light source.");
                LightFixtureStore.Save(fixturesPath, [], detected.Strengths, logger);
                return Proposal.None;
            }

            // Lights only where the geometry is SHAPED like a lamp -- the emitters the bake alone brought
            // forward still glow, and that is all they do. A vending machine's lit panel and a white sign
            // at the end of a corridor read bright for the same reason a tube does, because the artist
            // painted them so; making them glow is right and hanging a light fixture off them is not.
            // This is the split the reference exporter arrived at, and it is why the two sets are separate.
            IReadOnlyList<LightFixture> fixtures = LightFixtureExtractor.ProposeFromScene(
                scene, detected.Confirmed, LampDetector.Tint(resources), logger: logger);

            // Written so the proposal can be reviewed and corrected, and so the next load reads it back
            // rather than surveying and walking the whole level again.
            LightFixtureStore.Save(fixturesPath, fixtures, detected.Strengths, logger);

            return new Proposal(fixtures, detected.Strengths);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            logger.Warning("Emissive", $"The lights could not be proposed: {error.Message}");
            return Proposal.None;
        }
    }
}
