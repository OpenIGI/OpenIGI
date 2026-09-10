using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Textures;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The sky every I.G.I. 2 level declares.</summary>
public sealed class RetailIgi2SkyTests
{
    /// <summary>Every level declares a sky, and it parses into the class rather than a placeholder.</summary>
    /// <remarks>
    /// All 24 shipped levels declare <c>FlatSky</c>, so registering the class puts its parameter order
    /// under the loader's own arity check across the whole corpus — a transposed pair would fail the
    /// level load rather than merely produce an odd colour.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_level_declares_a_sky()
    {
        int levels = 0;
        int skies = 0;
        int layers = 0;
        int keyframes = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            levels++;
            foreach (GameTask task in Walk(level.Root))
            {
                skies += task is FlatSkyTask ? 1 : 0;
                layers += task is FlatSkyLayerTask ? 1 : 0;
                keyframes += task is FlatSkyKeyframeTask ? 1 : 0;
            }
        }

        Assert.Equal(24, levels);
        Assert.Equal(24, skies);
        Assert.Equal(33, layers);

        // One level animates its sky's colour over the mission, with two keyframes.
        Assert.Equal(2, keyframes);
    }

    /// <summary>A sky layer's texture is rebased, and the file is where the rebasing says.</summary>
    /// <remarks>
    /// <para>
    /// <c>FlatSkyLayer_FixFilename</c> (<c>igi2ed 0x653890</c>) throws away whatever directory the level
    /// authored and re-prefixes the bare name with <c>MISSION:textures/</c>, keeping the extension. So
    /// the authored string is not the path, and a port that trusted it would work on the shipped data
    /// and break on the first mod that authored one.
    /// </para>
    /// <para>
    /// Checked against the tree: every rebased name is a file beside its level, and it decodes.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_sky_layers_texture_is_rebased_onto_the_mission_textures_directory()
    {
        int checkedLayers = 0;
        int missing = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (GameTask task in Walk(level.Root))
            {
                if (task is not FlatSkyLayerTask layer || string.IsNullOrWhiteSpace(layer.TextureFileName))
                {
                    continue;
                }

                checkedLayers++;
                Assert.StartsWith(FlatSkyLayerTask.TextureDirectory, layer.TextureResource, StringComparison.Ordinal);
                Assert.DoesNotContain("/", layer.TextureResource[FlatSkyLayerTask.TextureDirectory.Length..], StringComparison.Ordinal);

                string bare = layer.TextureResource[FlatSkyLayerTask.TextureDirectory.Length..];
                string path = Path.Combine(level.Directory, "textures", bare);
                if (!File.Exists(path))
                {
                    missing++;
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(path);
                if (TgaImage.IsTga(bytes))
                {
                    TextureImage image = TgaImage.Read(bytes);
                    Assert.True(image.Width > 0 && image.Height > 0);
                }
            }
        }

        Assert.Equal(33, checkedLayers);

        // Every rebased name is a file beside its level. That is the rebasing being right: the authored
        // strings and the tree agree only because the directory is thrown away and replaced.
        Assert.Equal(0, missing);
    }

    /// <summary>The dome's colours are colours, and the engine does not clamp them.</summary>
    /// <remarks>
    /// <para>
    /// Each is an <c>RGB</c> carried as three floats. The corpus spans <b>0 to 1.0385</b> — so a level
    /// may author a slightly over-bright sky and the engine will take it, which is worth knowing before
    /// a renderer clamps on its way in and quietly darkens one level's horizon.
    /// </para>
    /// <para>
    /// The bound still catches the failure it is for: a parameter read from the wrong slot lands a
    /// distance or an angle in a colour, and those are nowhere near this range.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_domes_colours_are_in_range()
    {
        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (GameTask task in Walk(level.Root))
            {
                if (task is not FlatSkyTask sky)
                {
                    continue;
                }

                foreach (Vector3f colour in new[]
                         {
                             sky.TopColour,
                             sky.MiddleColour1,
                             sky.MiddleColour2,
                             sky.BottomColour1,
                             sky.BottomColour2,
                             sky.FogColour,
                         })
                {
                    Assert.InRange(colour.X, 0f, 1.05f);
                    Assert.InRange(colour.Y, 0f, 1.05f);
                    Assert.InRange(colour.Z, 0f, 1.05f);
                    Hi = Math.Max(Hi, Math.Max(colour.X, Math.Max(colour.Y, colour.Z)));
                }
            }
        }

        // At least one authored colour really is above one, so the tolerance is measurement not slack.
        Assert.True(Hi > 1f, $"The brightest colour was {Hi}, so nothing exceeds one after all.");
    }

    private static float Hi = float.MinValue;

    private static IEnumerable<GameTask> Walk(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            yield return child;

            foreach (GameTask descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
