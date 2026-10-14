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
/// <para>
/// The colour is asserted as a pass RESOLVES it, through <see cref="SceneFog.ColourFor(BlendMode,
/// FogTarget)"/> -- the same call the device makes when it fills the push row -- rather than as the
/// enum the pass asked with. Asking was never the part that broke: the ground asked correctly for a
/// long time and nothing read the answer, because the target was wired to a specialization constant
/// no shader declares. A test that stops at the enum cannot tell those two apart.
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

        // A fog that is nobody's half and nobody's white, so a pass that ignored its target would show.
        SceneFog scene = new(new Vector3f(0.30f, 0.44f, 0.62f), 0.0017408f);
        device.SetFog(scene);

        using Igi2TerrainPresenter presenter = new(device, surface, _ => TextureHandle.None);

        // The spawn, looking back over the tank farm at the hills: the retail frame's view.
        Camera3D camera = new(new Vector3d(-19, 419.5, 7.4), Camera3D.LookRotation(3.9794f, 0.05f));
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.1f, 4000f);
        presenter.Draw(new TerrainFrame(camera, projection, MathF.PI / 3f, 16f / 9f, 4000, Batching: false, OcclusionCulling: false));

        Assert.NotEmpty(device.MeshDraws);
        Assert.Contains(device.MeshDraws, draw => draw.Blend == BlendMode.Modulate2X);
        Assert.All(device.MeshDraws, draw =>
        {
            Assert.Equal(draw.Blend == BlendMode.Modulate2X ? FogTarget.White : FogTarget.MaterialWeightedHalfScene, draw.FogTarget);

            // And what it fogs towards, once the target and the blend are resolved together.
            Vector3f towards = draw.Fog.ColourFor(draw.Blend, draw.FogTarget);
            if (draw.Blend == BlendMode.Modulate2X)
            {
                Assert.Equal(Vector3f.One, towards);
            }
            else
            {
                Assert.Equal(scene.Colour.X * SceneFog.HalfTarget, towards.X, 5);
                Assert.Equal(scene.Colour.Y * SceneFog.HalfTarget, towards.Y, 5);
                Assert.Equal(scene.Colour.Z * SceneFog.HalfTarget, towards.Z, 5);

                // Neither the scene's colour, nor the black an additive pass would take on its own.
                Assert.NotEqual(scene.Colour, towards);
                Assert.NotEqual(default, towards);
            }
        });

        // And nothing after the ground inherits it: the next draw is back on the scene's own colour.
        Assert.Equal(FogTarget.Scene, device.FogTarget);
        Assert.Equal(scene.Colour, scene.ColourFor(BlendMode.Opaque, device.FogTarget));
    }
}
