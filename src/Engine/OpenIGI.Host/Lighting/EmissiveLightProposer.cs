using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats;
using OpenIGI.Game.World;

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

    /// <summary>How near a glow must be to a fitting found by shape to be the same lamp, in metres.</summary>
    /// <remarks>
    /// A glow hangs just off its fitting's glass, a hand's breadth to a third of a metre in the retail
    /// models, while two separate lamps are rarely nearer than a metre and a half. A fitting longer than
    /// this claims the glows along its own length instead.
    /// </remarks>
    public const double SameLampDistance = 0.75;

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
    /// <param name="formats">
    /// The title's asset formats, which the level's lightmaps are read by; <see langword="null"/> leaves the
    /// bake unasked, and the lamps only it knows of unfound.
    /// </param>
    /// <returns>The lamps to place and the surfaces they come out of.</returns>
    public static Proposal Propose(
        WorldScene scene,
        ResourceRegistry resources,
        LampManifest manifest,
        int missionId,
        string titleId,
        IOpenIgiLogger logger,
        AssetFormatProfile? formats = null)
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
            IReadOnlyList<LampManifest.Entry> named = manifest.For(titleId);
            LampDetector.Lamps lamps = LampDetector.Detect(scene, resources, named, logger);

            // What the models themselves declare. The sequel's carry a glow for every lamp the artists
            // put on them, with its place and its colour; Project I.G.I.'s carry none, so this is empty
            // there and the level is the detector's alone.
            GlowLamps.Result glows = GlowLamps.Propose(scene, resources, named, logger);

            // The lamps that carry no glow but are skinned and sized like the ones that do join the
            // detector's, BEFORE the geometry is read for lights: they have no glow to take a light from.
            lamps = With(lamps, glows.Skinned, glows.SkinnedTextures);

            // ...and the lamps with neither a glow nor a glowing twin, which only the bake knows of. Asked
            // only where the level declares lamps at all; see BakedLamps.
            if (formats is not null && (glows.Fixtures.Count > 0 || glows.SubMeshes.Count > 0))
            {
                Dictionary<string, float> baked = new(StringComparer.OrdinalIgnoreCase);
                HashSet<string> bakedTextures = new(StringComparer.OrdinalIgnoreCase);
                LampDetector.Lamps known = lamps;

                BakedLamps.Find(
                    scene,
                    resources,
                    formats,
                    (instance, key) => known.SubMeshes?.ContainsKey(key) == true
                        || glows.SubMeshes.ContainsKey(key)
                        || IsNamed(named, instance),
                    baked,
                    bakedTextures,
                    logger);

                lamps = With(lamps, baked, bakedTextures);
            }

            if (lamps.Count == 0 && glows.Fixtures.Count == 0 && glows.SubMeshes.Count == 0)
            {
                LightFixtureStore.Save(fixturesPath, [], lamps, logger);
                return Proposal.None;
            }

            // One set of textures for both halves: the geometry that glows IS the geometry the lights come out
            // of. They were briefly two -- everything the artists baked bright glowed, and only the
            // fixture-shaped ones cast light -- and the extra set was all false positives. Emission means
            // something strong here: an emissive surface is UNLIT, so a bake-bright office cabinet did not glow
            // gently, it turned into a flat shadeless panel.
            IReadOnlyList<LightFixture> fixtures = lamps.Count > 0
                ? LightFixtureExtractor.ProposeFromScene(scene, lamps, LampDetector.Tint(resources), logger: logger)
                : [];

            // The lamps found by a glowing twin's page take their reach, strength and hanging from it.
            if (glows.Skinned is { Count: > 0 })
            {
                LightFixture[] twinned = [.. fixtures];

                for (int i = 0; i < twinned.Length; i++)
                {
                    twinned[i] = glows.Twin(twinned[i]);
                }

                fixtures = twinned;
            }

            // The glows' lights beside the geometry's, and the glows' glass beside the detector's. The
            // glass is joined AFTER the geometry was read for lights, so a sub-mesh a glow lights up is
            // not also clustered into a second light on top of the glow's own.
            (fixtures, lamps) = Join(fixtures, lamps, glows);

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

    /// <summary>Whether a person has said something about a model, either way.</summary>
    private static bool IsNamed(IReadOnlyList<LampManifest.Entry> named, string instance)
    {
        foreach (LampManifest.Entry entry in named)
        {
            if (string.Equals(entry.Model, instance, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds more lamp sub-meshes to the ones already found; what was found already keeps its strength.</summary>
    private static LampDetector.Lamps With(
        LampDetector.Lamps lamps, IReadOnlyDictionary<string, float>? more, IReadOnlySet<string>? moreTextures)
    {
        if (more is not { Count: > 0 } || moreTextures is null)
        {
            return lamps;
        }

        HashSet<string> textures = new(lamps.Textures ?? moreTextures, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, float> subMeshes = new(more, StringComparer.OrdinalIgnoreCase);

        if (lamps.SubMeshes is not null)
        {
            foreach ((string key, float strength) in lamps.SubMeshes)
            {
                subMeshes[key] = strength;
            }
        }

        textures.UnionWith(moreTextures);

        return new LampDetector.Lamps(textures, subMeshes);
    }

    /// <summary>Adds what a level's glows declare to what was named and inferred.</summary>
    /// <remarks>
    /// A sub-mesh already judged keeps that judgement: it was named by hand, and a person's word is not
    /// overruled by a rule of thumb about glass.
    /// </remarks>
    private static (IReadOnlyList<LightFixture> Fixtures, LampDetector.Lamps Lamps) Join(
        IReadOnlyList<LightFixture> fixtures, LampDetector.Lamps lamps, GlowLamps.Result glows)
    {
        if (glows.Fixtures.Count == 0 && glows.SubMeshes.Count == 0)
        {
            return (fixtures, lamps);
        }

        // A glow and a fitting found by shape are often the same lamp. The one read from geometry
        // keeps its place: it has a measured face and a facing, which a glow's point has not, and two
        // lights on one lamp would double it.
        List<LightFixture> all = [.. fixtures];

        foreach (LightFixture glow in glows.Fixtures)
        {
            bool taken = false;

            foreach (LightFixture found in fixtures)
            {
                double reach = Math.Max(SameLampDistance, found.Extent);

                if (Vector3d.DistanceSquared(glow.Position, found.Position) <= reach * reach)
                {
                    taken = true;
                    break;
                }
            }

            if (!taken)
            {
                all.Add(glow);
            }
        }

        // Strongest first, as each list already is and as the readers of the joined one expect.
        all.Sort(static (left, right) => right.Intensity.CompareTo(left.Intensity));

        HashSet<string> textures = new(lamps.Textures ?? glows.Textures, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, float> subMeshes = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string key, float strength) in glows.SubMeshes)
        {
            subMeshes[key] = strength;
        }

        if (lamps.SubMeshes is not null)
        {
            foreach ((string key, float strength) in lamps.SubMeshes)
            {
                subMeshes[key] = strength;
            }
        }

        textures.UnionWith(glows.Textures);

        return (all, new LampDetector.Lamps(textures, subMeshes));
    }
}
