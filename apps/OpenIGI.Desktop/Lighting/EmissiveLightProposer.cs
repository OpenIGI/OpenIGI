using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.World;

namespace OpenIGI.Desktop;

/// <summary>
/// Works out which lights a level has, given that it declares none: it ranks a level's textures by how
/// much they behave like light sources and turns the winning ones into placed fixtures.
/// </summary>
/// <remarks>
/// The survey and the extractor both already existed and neither was ever called; what this decides is
/// only where to cut the ranking, and the cut is deliberately generous -- a fixture that should not be
/// there shows up as a glow in the wrong place, which is reviewable, whereas a missing one is invisible
/// and indistinguishable from the stage not working. A per-mission fixtures file, once written, is the
/// truth: the survey ran once to propose the list and a human has had the chance to correct it since.
/// </remarks>
internal static class EmissiveLightProposer
{
    // How light-like a texture must score before it is treated as a source. The score is the share of a
    // texture's uses that are baked bright, weighted by how bright and discounted for textures used only
    // once or twice -- so half means bright across most of its uses and genuinely bright, not merely
    // pale. Level 1's known emitter scores far above this; the cut keeps out light-coloured walls that
    // happen to stand in the sun.
    private const float EmitterScore = 0.5f;

    /// <summary>Proposes the placed light fixtures for a level, from an authored file or a fresh survey.</summary>
    /// <param name="scene">The built world scene whose placements and textures are surveyed.</param>
    /// <param name="resources">The resource registry the emitter textures are decoded through.</param>
    /// <param name="missionId">The mission the fixtures belong to, which names the per-mission file.</param>
    /// <param name="logger">The log the proposal is reported to.</param>
    /// <returns>The fixtures to place, which may be empty.</returns>
    public static IReadOnlyList<LightFixture> Propose(
        WorldScene scene, ResourceRegistry resources, int missionId, IIgiLogger logger)
    {
        // Beside the other authored data, one file per mission. When it exists it is the truth: the
        // survey ran once to propose the list and a human has had the chance to correct it since, exactly
        // the way authored material detail wins over the detail derived from a texture. Reading it also
        // keeps the survey's lightmap decode off the load path entirely.
        string fixturesPath = Path.Combine(
            AppContext.BaseDirectory, "Assets", "fixtures", $"mission-{missionId}.json");

        if (LightFixtureStore.TryLoad(fixturesPath, out IReadOnlyList<LightFixture> saved, logger))
        {
            return saved;
        }

        try
        {
            IReadOnlyList<EmissiveTextureSurvey.Finding> findings =
                EmissiveTextureSurvey.Survey(scene, resources, logger);

            List<string> emitters = [];

            foreach (EmissiveTextureSurvey.Finding finding in findings)
            {
                if (finding.Score >= EmitterScore)
                {
                    emitters.Add(finding.Texture);
                }
            }

            if (emitters.Count == 0)
            {
                logger.Information("Emissive", "No texture in this level looks enough like a light source.");
                LightFixtureStore.Save(fixturesPath, [], logger);
                return [];
            }

            // Named rather than counted. This is inferred from art that was never meant to be read this
            // way, so the list is the first thing to check when a level lights wrongly.
            logger.Information("Emissive", $"Emitter textures: {string.Join(", ", emitters)}.");

            IReadOnlyList<LightFixture> proposed = LightFixtureExtractor.ProposeFromScene(
                scene, emitters, EmitterColour(resources), logger: logger);

            // Written so the proposal can be reviewed and corrected, and so the next load reads it back
            // rather than surveying again.
            LightFixtureStore.Save(fixturesPath, proposed, logger);

            return proposed;
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            logger.Warning("Emissive", $"The lights could not be proposed: {error.Message}");
            return [];
        }
    }

    // Resolves an emitter texture to its mean colour, linear and normalised so its brightest channel is
    // about one, for the light a fixture drawing it is given. Best effort, and cached per name.
    //
    // A texture that resolves as a standalone asset and decodes without a palette gives its own colour.
    // Most emitter textures do neither: they live inside a level's MTP pack, named the way a material
    // references them rather than as a file, and are paletted -- decoding those wants the pack and its
    // palette, which is not wired here yet. Those fall back to white, a tint a reviewer can correct in
    // the fixtures file, whereas the placement the fixture comes with cannot be guessed later. So a miss
    // here costs the tint, never the fixture, and never the load: the whole probe is guarded, because the
    // resource layer treats a bare texture name as an illegal path and throws rather than returning
    // false.
    private static Func<string, Vector3f> EmitterColour(ResourceRegistry resources)
    {
        Dictionary<string, Vector3f> cache = new(StringComparer.OrdinalIgnoreCase);

        return name =>
        {
            if (cache.TryGetValue(name, out Vector3f cached))
            {
                return cached;
            }

            Vector3f colour = new(1f, 1f, 1f);

            foreach (string candidate in new[] { name, name + ".tex", name + ".spr" })
            {
                try
                {
                    if (!resources.TryLoad(candidate, out ReadOnlyMemory<byte> data)
                        || data.Length < 4
                        || !TexFile.IsTexture(data.Span))
                    {
                        continue;
                    }

                    colour = MeanColour(TexFile.Load(data.Span));
                    break;
                }
                catch (Exception)
                {
                    // Illegal-path names, paletted or malformed payloads -- a tint is never worth taking
                    // the load down for. It stays white; the fixtures file lets a reviewer correct it.
                }
            }

            cache[name] = colour;
            return colour;
        };
    }

    // Averages a decoded texture and normalises it so its brightest channel is about one.
    private static Vector3f MeanColour(TextureImage image)
    {
        ReadOnlySpan<Bgra32> pixels = image.Pixels;
        (int width, int height) = image.GetLevelSize(0);
        int count = Math.Min(width * height, pixels.Length);

        if (count <= 0)
        {
            return new Vector3f(1f, 1f, 1f);
        }

        double r = 0.0;
        double g = 0.0;
        double b = 0.0;

        for (int i = 0; i < count; i++)
        {
            Bgra32 pixel = pixels[i];
            r += pixel.R;
            g += pixel.G;
            b += pixel.B;
        }

        float mr = (float)(r / count);
        float mg = (float)(g / count);
        float mb = (float)(b / count);

        // Normalised on the brightest channel, so the fixture's colour is a hue and the intensity is left
        // to the area and the scale the renderer applies. A texture that averaged to black -- a fully
        // transparent or empty page -- stays white rather than becoming a division by nothing.
        float peak = MathF.Max(mr, MathF.Max(mg, mb));

        return peak > 1e-3f
            ? new Vector3f(mr / peak, mg / peak, mb / peak)
            : new Vector3f(1f, 1f, 1f);
    }
}
