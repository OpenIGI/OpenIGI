using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;

namespace OpenIGI.Host.Lighting;

/// <summary>
/// Works out which lights a level has, given that it declares none, and which of its surfaces are the
/// lamps those lights come out of.
/// </summary>
/// <remarks>
/// <para>
/// Two steps. <see cref="LampDetector"/> works out which of the level's textures are light fixtures,
/// from the shape and placement of the geometry that draws them; <see cref="LightFixtureExtractor"/>
/// then turns every place one of those is drawn into a placed light with a position, a reach and a
/// facing.
/// </para>
/// <para>
/// Both halves of the conclusion are used and both are written down. The fixtures become real lights in
/// the deferred pass and real emitters in the surface cache's bounce; the emitter strengths go to the
/// material library so that the lamp geometry ITSELF glows. Doing only the first is what made a room
/// look lit by nothing — light on the floor with a grey panel above it.
/// </para>
/// <para>
/// A per-mission fixtures file, once written, is the truth: the detection ran once to propose the list
/// and a human has had the chance to correct it since. Reading it also keeps the whole-level geometry
/// walk and every texture decode off the load path entirely.
/// </para>
/// </remarks>
public static class EmissiveLightProposer
{
    /// <summary>What a level's own geometry says it is lit by.</summary>
    /// <param name="Fixtures">The placed lights, which may be empty.</param>
    /// <param name="Lamps">The geometry those lights come out of, which glows.</param>
    public readonly record struct Proposal(
        IReadOnlyList<LightFixture> Fixtures,
        LampDetector.Lamps Lamps)
    {
        /// <summary>Gets the proposal for a level that has nothing of the kind.</summary>
        public static Proposal None { get; } = new([], LampDetector.Lamps.None);
    }

    /// <summary>The title whose fixture files keep the bare <c>mission-N.json</c> name.</summary>
    /// <remarks>
    /// Project I.G.I.'s, because those files exist and some have been corrected by hand. A second title
    /// writing into the same names would have overwritten them, so the prefix goes on the newcomer.
    /// </remarks>
    public const string DefaultTitleId = "igi1";

    /// <summary>Proposes a level's lamps, from an authored file or a fresh survey.</summary>
    /// <param name="scene">The built world scene whose placements and textures are surveyed.</param>
    /// <param name="resources">The resource registry the emitter textures are decoded through.</param>
    /// <param name="manifest">The lamps a person named by hand, which win over anything inferred.</param>
    /// <param name="missionId">The mission the fixtures belong to, which names the per-mission file.</param>
    /// <param name="titleId">
    /// Which game the mission belongs to, as <c>GameTitle.Id</c> gives it. Two titles number their
    /// missions independently and the ranges OVERLAP, so this is what keeps them apart.
    /// </param>
    /// <param name="logger">The log the proposal is reported to.</param>
    /// <returns>The lamps to place and the surfaces they come out of.</returns>
    public static Proposal Propose(
        WorldScene scene,
        ResourceRegistry resources,
        LampManifest manifest,
        int missionId,
        string titleId,
        IOpenIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        // Beside the other authored data, one file per mission. When it exists and its version matches
        // it is the truth: the detection ran once to propose the list and a human has had the chance to
        // correct it since, exactly the way authored material detail wins over the detail derived from
        // a texture.
        //
        // NAMED BY TITLE, because the numbers collide. Project I.G.I. has missions 1 to 17 and I.G.I. 2
        // numbers its own location*10 + level, which starts at 11 -- so the sequel's first seven
        // missions sat exactly on top of the first game's last seven. The sequel loaded Project I.G.I.'s
        // fixture list, which names that game's textures and matches none of its own, and because a
        // loaded list WINS over detection it never surveyed the level at all. That is why the sequel's
        // lamps were dark.
        //
        // The first game keeps the unprefixed name it has always written, so every list already
        // corrected by hand stays valid; anything else is prefixed.
        string fixturesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "fixtures",
            string.Equals(titleId, DefaultTitleId, StringComparison.Ordinal)
                ? $"mission-{missionId}.json"
                : $"{titleId}-mission-{missionId}.json");

        if (LightFixtureStore.TryLoad(
                fixturesPath, out IReadOnlyList<LightFixture> saved, out LampDetector.Lamps stored, logger))
        {
            return new Proposal(saved, stored);
        }

        try
        {
            // Only this game's hand-named lamps. The two titles' model numbering overlaps, and a
            // name that is a lamp in one is ordinary furniture in the other.
            LampDetector.Lamps lamps = LampDetector.Detect(scene, resources, manifest.For(titleId), logger);

            if (lamps.Count == 0)
            {
                LightFixtureStore.Save(fixturesPath, [], lamps, logger);
                return Proposal.None;
            }

            // One set of textures for both halves: the geometry that glows IS the geometry the lights
            // come out of. They were briefly two -- everything the artists baked bright glowed, and only
            // the fixture-shaped ones cast light, which is what the reference exporter does -- and the extra
            // set was all false positives. Emission means something stronger here than it does there: an
            // emissive surface is UNLIT, so a bake-bright office cabinet did not glow gently, it turned
            // into a flat shadeless panel.
            IReadOnlyList<LightFixture> fixtures = LightFixtureExtractor.ProposeFromScene(
                scene, lamps, LampDetector.Tint(resources), logger: logger);

            // Written so the proposal can be reviewed and corrected, and so the next load reads it back
            // rather than walking the whole level again.
            LightFixtureStore.Save(fixturesPath, fixtures, lamps, logger);

            return new Proposal(fixtures, lamps);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            logger.Warning("Emissive", $"The lights could not be proposed: {error.Message}");
            return Proposal.None;
        }
    }
}
