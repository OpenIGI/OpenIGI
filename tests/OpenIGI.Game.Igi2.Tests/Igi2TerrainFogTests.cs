using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// How I.G.I. 2's ground is fogged: the density's unit, and the colour each of the three passes fogs to.
/// </summary>
/// <remarks>
/// <para>
/// Both were measured against a frame of the retail build standing at location1/level1's spawn. With
/// the density read per metre and every pass fogged to the scene's colour, the tanks a hundred metres
/// off carried a quarter of the original's haze and the hills a kilometre off had sunk to a third of
/// the original's brightness — the fog multiplied into itself across the passes. See
/// <see cref="FogTarget"/> and <see cref="Igi2SceneAtmosphere.AuthoredDensityScale"/> for the state
/// blocks and the unit.
/// </para>
/// </remarks>
public sealed class Igi2TerrainFogTests
{
    /// <summary>The authored density is per Direct3D unit, and a metre is 4.096 of them.</summary>
    [Fact]
    public void The_density_is_per_direct3d_unit()
    {
        Assert.Equal(0.002048f, Igi2SceneAtmosphere.AuthoredDensityScale, 6);

        // location1/level1 authors 0.85: a haze that halves every four hundred metres, not every two
        // and a third kilometres.
        float perMetre = 0.85f * Igi2SceneAtmosphere.AuthoredDensityScale;
        Assert.Equal(398f, MathF.Log(2f) / perMetre, 0);
    }

    /// <summary>The base and overlay passes fog to half the scene's colour; the lightmap pass fogs to white.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Each_ground_pass_fogs_to_its_own_colour()
    {
        Igi2Level level = Igi2Level.All().First(l =>
            l.Directory.Replace(Path.DirectorySeparatorChar, '/').EndsWith("location1/level1", StringComparison.OrdinalIgnoreCase));
        string path = Path.Combine(level.Directory, "heightmaps", "heightmaps.res");
        IresArchive? archive = File.Exists(path) ? IresArchive.Read(File.ReadAllBytes(path)) : null;
        Igi2TerrainSurface surface = Igi2TerrainSurface.Build(level.Root, archive);

        RecordingGraphicsDevice device = new();
        using Igi2TerrainPresenter presenter = new(device, surface, _ => TextureHandle.None);

        // The spawn, looking back over the tank farm at the hills: the retail frame's view.
        Camera3D camera = new(new Vector3d(-19, 419.5, 7.4), Camera3D.LookRotation(3.9794f, 0.05f));
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.1f, 4000f);
        presenter.Draw(new TerrainFrame(camera, projection, MathF.PI / 3f, 16f / 9f, 4000, Batching: false, OcclusionCulling: false));

        Assert.NotEmpty(device.MeshDraws);
        Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Modulate2X);
        Assert.All(device.MeshDraws, draw =>
            Assert.Equal(draw.Blend == BlendMode.Modulate2X ? FogTarget.White : FogTarget.HalfScene, draw.FogTarget));

        // And nothing after the ground inherits it.
        Assert.Equal(FogTarget.Scene, device.FogTarget);
    }
}
