using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The light an unlightmapped I.G.I. 2 object is drawn under, and the premise that makes it matter.
/// </summary>
/// <remarks>
/// <para>
/// The port hands every MEF vertex a white colour and the forward shader multiplies the texture by it,
/// so an object with no bake was drawn at its texture alone: a storage tank that the game ships as a
/// blue-grey shape under a night sun came out white. <see cref="Igi2FixedFunctionSun"/> is what the
/// original lit it with instead, and these pin both halves — that the premise holds in the data the
/// sequel ships, and that the reading of the sun matches the binary.
/// </para>
/// </remarks>
public sealed class Igi2FixedFunctionSunTests
{
    /// <summary>The keyframe's angles turn into the vector <c>0x6573C0</c> computes.</summary>
    /// <remarks>
    /// Three sightings that pin the convention: the X axis at both angles zero, straight up at a
    /// quarter turn of <c>Beta</c>, and <c>−Y</c> at a quarter turn of <c>Gamma</c> — the last is the
    /// sign the original's <c>2π − Gamma</c> puts on the azimuth.
    /// </remarks>
    [Theory]
    [InlineData(0f, 0f, 1f, 0f, 0f)]
    [InlineData(MathF.PI / 2f, 0f, 0f, 0f, 1f)]
    [InlineData(0f, MathF.PI / 2f, 0f, -1f, 0f)]
    [InlineData(MathF.PI / 4f, MathF.PI / 4f, 0.5f, -0.5f, 0.70710677f)]
    public void The_direction_is_the_binary_s_rotation_of_the_x_axis(float beta, float gamma, float x, float y, float z)
    {
        Vector3f direction = Igi2FixedFunctionSun.DirectionFrom(beta, gamma);

        Assert.Equal(x, direction.X, 5);
        Assert.Equal(y, direction.Y, 5);
        Assert.Equal(z, direction.Z, 5);
        Assert.Equal(1f, direction.Length, 5);
    }

    /// <summary>A sun authored below the horizon by the shared reading is above it by the binary's.</summary>
    /// <remarks>
    /// location1/level1's <c>Beta</c> is <c>1 − 2π</c>. The shared <c>DirectionalLightSettings</c>
    /// reads its elevation as <c>90° − Beta</c>, 32.7°; the original's rotation puts the elevation at
    /// <c>Beta</c> itself, 57.3°. A sequel that lit its objects from the former would light them from
    /// the wrong side of the sky.
    /// </remarks>
    [Fact]
    public void Level_one_s_sun_is_fifty_seven_degrees_up()
    {
        Vector3f direction = Igi2FixedFunctionSun.DirectionFrom(-5.28319f, 0.40944f);

        Assert.Equal(57.3f, MathF.Asin(direction.Z) * 180f / MathF.PI, 1);
        Assert.Equal(0.4956f, direction.X, 3);
        Assert.Equal(-0.2151f, direction.Y, 3);
        Assert.Equal(0.8415f, direction.Z, 3);
    }

    /// <summary>location1/level1 reads as the values its scripts author.</summary>
    /// <remarks>
    /// The ambient is the <c>GlobalLight</c>'s first object category, not the terrain's — the two are
    /// equal in this level, which is why the port's fog never showed the difference, but the renderer
    /// reads the object slot and so does this. The colour is the in-game keyframe's front colour, a
    /// deep blue: the sequel authors night by darkening the sun, not by lowering it.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Level_one_s_objects_are_lit_by_a_dark_blue_sun_over_a_dark_blue_ambient()
    {
        Igi2Level level = LevelOne();

        Assert.True(Igi2FixedFunctionSun.TryRead(level.Root, out Igi2FixedFunctionSun sun));

        Assert.Equal(0.0577f, sun.Ambient.X, 3);
        Assert.Equal(0.1f, sun.Ambient.Y, 3);
        Assert.Equal(0.18f, sun.Ambient.Z, 3);
        Assert.Equal(0.0962f, sun.Colour.X, 3);
        Assert.Equal(0.1538f, sun.Colour.Y, 3);
        Assert.Equal(0.25f, sun.Colour.Z, 3);
        Assert.Equal(0.8415f, sun.Direction.Z, 3);

        ObjectShading? shading = Igi2PresentationProvider.Instance.ReadObjectShading(level.Root);
        Assert.NotNull(shading);
        Assert.Equal(2f, shading.Value.LightmapGain);
        Assert.Equal(sun.Colour, shading.Value.Sun?.Colour);
    }

    /// <summary>Every unlightmapped placement reaches the shader with a white vertex colour.</summary>
    /// <remarks>
    /// Measured rather than assumed before the sun was added: 63 of the level's placements have no
    /// bake, 25 in the colourless 32-byte format and 38 in the 40-byte one with no entry in the
    /// archive, and every vertex of every one of them is <c>#FFFFFFFF</c>. Nothing in the data lights
    /// them; only the sun can.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Level_one_s_unlightmapped_placements_carry_no_lighting_of_their_own()
    {
        Igi2Level level = LevelOne();
        Igi2DrawableSceneResult built = Build(level);

        int unlit = 0;
        foreach (WorldPlacement placement in built.Scene.Placements)
        {
            if (placement.HasLightmap)
            {
                continue;
            }

            unlit++;
            MeshGeometry geometry = Assert.IsType<MeshGeometry>(
                MefMeshBuilder.Build(placement.Model, (float)(1.0 / WorldScene.UnitsPerMetre)));
            Assert.All(geometry.Vertices, vertex => Assert.Equal(Bgra32.White, vertex.Colour));
        }

        Assert.Equal(63, unlit);
    }

    private static Igi2Level LevelOne() =>
        Igi2Level.All().First(level =>
            level.Directory.Replace('\\', '/').EndsWith("location1/level1", StringComparison.OrdinalIgnoreCase));

    private static Igi2DrawableSceneResult Build(Igi2Level level)
    {
        string root = GameDataSource.Igi2.RequireRoot();
        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(root));
        ResourceRegistry resources = new(files);
        List<LoadedPack> packs = [];

        foreach ((string directory, string name) in level.PackSources())
        {
            string prefix = "LOCAL:" + Path.GetRelativePath(root, directory).Replace('\\', '/') + "/";
            foreach ((string key, string path) in new[]
                     {
                         ($"{prefix}{name}.mtp", Path.Combine(directory, $"{name}.mtp")),
                         ($"{prefix}models/{name}.res", Path.Combine(directory, "models", $"{name}.res")),
                         ($"{prefix}textures/{name}.res", Path.Combine(directory, "textures", $"{name}.res")),
                         ($"{prefix}lightmaps/lightmaps.res", Path.Combine(directory, "lightmaps", "lightmaps.res")),
                     })
            {
                if (File.Exists(path))
                {
                    resources.Register(key, File.ReadAllBytes(path));
                }
            }

            packs.Add(LoadedPack.Open(prefix, name, resources, logger: null, AssetFormatProfile.Igi2));
        }

        string levelDirectory = "LOCAL:" + Path.GetRelativePath(root, level.Directory).Replace('\\', '/') + "/";
        LoadedMission mission = new(
            new MissionDescriptor(
                Id: 1,
                Name: "igi2",
                Description: string.Empty,
                Tag: string.Empty,
                NextMissionId: MissionDescriptor.NextMissionUndefined,
                MissionDirectory: levelDirectory,
                CommonPackDirectory: packs[0].Directory,
                LevelPackDirectory: levelDirectory,
                CommonPackName: packs[0].Name,
                LevelPackName: packs[^1].Name,
                BriefingSprite: string.Empty),
            packs[0],
            packs[^1],
            []);

        return Igi2DrawableScene.Build(mission, level.Root, level.Table, level.HasLightmaps ? level.Lightmaps : null, packs);
    }
}
