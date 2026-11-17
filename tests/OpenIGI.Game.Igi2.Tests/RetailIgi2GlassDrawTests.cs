using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A pane drawn into the opaque world keeps only its solid texels; its smudges are the glass pass's.</summary>
public sealed class RetailIgi2GlassDrawTests
{
    /// <summary>
    /// Mission 12's dormitory windows, a smudged pane at alpha 64 to 160 named glass, are drawn into the opaque world at the glass
    /// frame's reference, so none of their dirt lands there as solid grey; then the glass pass draws them whole.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_smudged_pane_leaves_its_dirt_out_of_the_opaque_world()
    {
        Igi2Level level = Igi2Level.Find("location1", "level2");
        (Igi2DrawableSceneResult built, ResourceRegistry resources) = Igi2LevelScene.Build(level);
        RecordingGraphicsDevice device = new();
        using WorldRenderer renderer = new(device, resources, AssetFormatProfile.Igi2) { Materials = ShippedMaterials() };

        // Where the report stood: before the dormitory's windows, looking at them.
        Camera3D camera = new(new Vector3d(51.78, 490.83, 183.41), Camera3D.LookRotation(-86.1f * MathF.PI / 180f, -1.7f * MathF.PI / 180f));
        Matrix4f projection = Matrix4f.CreatePerspective(MathF.PI / 3f, 16f / 9f, 0.1f, 400f);
        renderer.Draw(built.Scene, camera, projection);

        Assert.Contains(WorldRenderer.GlassFrameAlphaReference, device.AlphaReferences);
        Assert.Equal(IGraphicsDevice.DefaultAlphaReference, device.AlphaReference);

        device.MeshDraws.Clear();
        renderer.Draw(built.Scene, camera, projection, glassOnly: true, fromEye: true);
        Assert.NotEmpty(device.MeshDraws);
    }

    private static MaterialLibrary ShippedMaterials()
    {
        for (string? directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            string candidate = Path.Combine(directory, "apps", "OpenIGI.Composition", "Assets", "materials.json");
            if (File.Exists(candidate))
            {
                return MaterialLibrary.Load(candidate);
            }
        }

        throw new FileNotFoundException("materials.json");
    }
}
