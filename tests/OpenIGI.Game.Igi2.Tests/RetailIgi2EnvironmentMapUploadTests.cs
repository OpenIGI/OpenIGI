using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Every baked reflection the game ships, put through the device's cube-map seam.</summary>
/// <remarks>
/// The sibling suite checks that the faces decode and that the layout is the right way up. This one
/// checks the step after: that what the loader holds is what a cube texture can be built from — the same
/// edge on all six faces, the right count, the right order — which is the shape of mistake that turns
/// into a driver error or a silently rescaled face rather than a wrong picture.
/// </remarks>
public sealed class RetailIgi2EnvironmentMapUploadTests
{
    /// <summary>Every shipped map uploads as one cube of six faces.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Every_shipped_map_uploads_as_one_cube_of_six_faces()
    {
        int maps = 0;
        int faces = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (WaterTask water in Waters(level.Root))
            {
                string archivePath = Path.Combine(
                    level.Directory,
                    Igi2EnvironmentMap.ArchiveDirectory,
                    Igi2EnvironmentMap.ArchiveName(water.Id));

                if (!File.Exists(archivePath))
                {
                    continue;
                }

                IresArchive archive = IresArchive.Read(File.ReadAllBytes(archivePath));
                RecordingGraphicsDevice device = new();

                foreach (Igi2EnvironmentMap map in Igi2EnvironmentMap.LoadAll(water, archive))
                {
                    TextureHandle cube = map.Upload(device);
                    maps++;
                    faces += Igi2EnvironmentMap.FaceCount;

                    // One cube, at the edge the loader read, and six faces written into it in order.
                    (string Name, int Edge) created = device.CubeTextures[^1];
                    Assert.Equal(map.Edge, created.Edge);

                    List<(TextureHandle Cube, int Face, Bgra32[] Pixels)> uploads =
                        [.. device.CubeFaceUploads.Where(upload => upload.Cube == cube)];

                    Assert.Equal(Igi2EnvironmentMap.FaceCount, uploads.Count);
                    for (int face = 0; face < Igi2EnvironmentMap.FaceCount; face++)
                    {
                        Assert.Equal(face, uploads[face].Face);
                        Assert.Equal(map.Edge * map.Edge, uploads[face].Pixels.Length);
                    }

                    // The faces are different pictures. A loader that read one file six times, or an
                    // upload that handed the same buffer over six times, would pass every count above.
                    Assert.True(
                        uploads.Select(upload => Fingerprint(upload.Pixels)).Distinct().Count() > 1,
                        $"water {map.TaskId} map {map.Index} uploaded six identical faces.");
                }
            }
        }

        // The whole shipped corpus, and the faces they carry between them.
        Assert.Equal(31, maps);
        Assert.Equal(186, faces);
    }

    /// <summary>The device refuses a face that is not one of six, and a cube it did not make.</summary>
    /// <remarks>
    /// The seam's own guards. A cube is addressed by the same <see cref="TextureHandle"/> a 2D texture
    /// is, so nothing in the type system stops one being passed where the other belongs; these are what
    /// turn that into a refusal rather than a write into the wrong resource.
    /// </remarks>
    [Fact]
    public void The_seam_refuses_a_face_that_is_not_one_of_six()
    {
        RecordingGraphicsDevice device = new();
        TextureHandle cube = device.CreateCubeTexture(4, "test");
        Bgra32[] face = new Bgra32[16];

        Assert.Throws<ArgumentOutOfRangeException>(() => device.UpdateCubeFace(cube, -1, face));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.UpdateCubeFace(cube, 6, face));
        Assert.Throws<GraphicsDeviceException>(() => device.UpdateCubeFace(new TextureHandle(1), 0, face));
        Assert.Throws<GraphicsDeviceException>(() => device.UpdateCubeFace(cube, 0, new Bgra32[15]));
        Assert.Throws<GraphicsDeviceException>(() => device.CreateCubeTexture(0, "empty"));

        // And a cube is what a reflective run of draws is told to use.
        Assert.Equal(TextureHandle.None, device.EnvironmentCube);
        device.SetEnvironmentCube(cube);
        Assert.Equal(cube, device.EnvironmentCube);
        device.SetEnvironmentCube(TextureHandle.None);
        Assert.Equal(TextureHandle.None, device.EnvironmentCube);
    }

    /// <summary>A device starts at the threshold the port has always cut out at.</summary>
    [Fact]
    public void A_device_starts_at_the_default_cut_out()
    {
        RecordingGraphicsDevice device = new();
        Assert.Equal(IGraphicsDevice.DefaultAlphaReference, device.AlphaReference);

        // I.G.I. 2's foliage, from ForestRender_SetStates_Foliage.
        device.SetAlphaReference(32);
        Assert.Equal(32, device.AlphaReference);
        Assert.Equal([(byte)32], device.AlphaReferences);
    }

    private static string Fingerprint(Bgra32[] pixels)
    {
        byte[] bytes = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            bytes[(i * 4) + 0] = pixels[i].B;
            bytes[(i * 4) + 1] = pixels[i].G;
            bytes[(i * 4) + 2] = pixels[i].R;
            bytes[(i * 4) + 3] = pixels[i].A;
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
    }

    private static IEnumerable<WaterTask> Waters(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is WaterTask water)
            {
                yield return water;
            }

            foreach (WaterTask descendant in Waters(child))
            {
                yield return descendant;
            }
        }
    }
}
