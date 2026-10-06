using System.Numerics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Reconstruction;
using OpenIGI.Rendering.Rhi;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// Pins the camera an NVIDIA reconstruction is told about: the reverse-Z projection the depth buffer holds,
/// the previous-frame transform, and the motion and jitter conventions.
/// </summary>
/// <remarks>
/// None of these fails loudly when wrong. A sign error on the jitter only softens the picture, a projection
/// describing the wrong depth range only makes the reconstruction trust the wrong edges, and a previous-frame
/// transform off by a translation only smears frame generation along the camera's path.
/// </remarks>
public sealed class ReconstructionCameraTests
{
    private static readonly DepthUnprojection Lens = new(XScale: 0.75f, YScale: 1.333f, Near: 0.1f, Far: 100000f);

    [Fact]
    public void Projection_maps_the_near_plane_to_one_and_the_far_plane_to_nought()
    {
        Matrix4x4 projection = ReconstructionCamera.Projection(Lens);

        Vector4 near = Vector4.Transform(new Vector4(0f, 0f, -Lens.Near, 1f), projection);
        Vector4 far = Vector4.Transform(new Vector4(0f, 0f, -Lens.Far, 1f), projection);

        Assert.Equal(1f, near.Z / near.W, 4);
        Assert.Equal(0f, far.Z / far.W, 4);
    }

    [Fact]
    public void Inverse_projection_undoes_the_projection()
    {
        Matrix4x4 round = ReconstructionCamera.Projection(Lens) * ReconstructionCamera.InverseProjection(Lens);

        Vector4 point = new(3f, -2f, -17f, 1f);
        Vector4 back = Vector4.Transform(point, round);
        Assert.Equal(point.X, back.X / back.W, 3);
        Assert.Equal(point.Y, back.Y / back.W, 3);
        Assert.Equal(point.Z, back.Z / back.W, 3);
    }

    [Fact]
    public void A_still_camera_maps_clip_space_onto_itself()
    {
        RhiStreamlineConstants constants = ReconstructionCamera.Build(Frame(new Vector3d(5000, 20, -9000), new Vector3d(5000, 20, -9000)));

        Vector4 clip = Vector4.Transform(new Vector4(0.3f, -0.2f, 0.5f, 1f), constants.ClipToPrevClip);
        Assert.Equal(0.3f, clip.X / clip.W, 4);
        Assert.Equal(-0.2f, clip.Y / clip.W, 4);
        Assert.Equal(0.5f, clip.Z / clip.W, 4);
    }

    [Fact]
    public void A_camera_that_moved_sideways_finds_a_point_where_it_was()
    {
        // The camera moved one metre along world X (its own right), far from the origin: the precision the
        // camera-relative build is there for. A point ten metres ahead was one metre further right before.
        Vector3d now = new(42000.0, 10.0, -36000.0);
        Vector3d before = new(41999.0, 10.0, -36000.0);
        RhiStreamlineConstants constants = ReconstructionCamera.Build(Frame(now, before));

        Matrix4x4 projection = ReconstructionCamera.Projection(Lens);
        Vector4 current = Vector4.Transform(new Vector4(0f, 0f, -10f, 1f), projection);
        Vector4 previous = Vector4.Transform(current, constants.ClipToPrevClip);
        Vector4 expected = Vector4.Transform(new Vector4(1f, 0f, -10f, 1f), projection);

        Assert.Equal(expected.X / expected.W, previous.X / previous.W, 4);
        Assert.Equal(expected.Z / expected.W, previous.Z / previous.W, 4);

        Vector4 forward = Vector4.Transform(previous, constants.PrevClipToClip);
        Assert.Equal(current.X / current.W, forward.X / forward.W, 4);
    }

    [Fact]
    public void Motion_and_jitter_conventions_are_the_ones_the_textures_hold()
    {
        RhiStreamlineConstants constants = ReconstructionCamera.Build(Frame(default, default) with { JitterX = 0.25f, JitterY = 0.125f });

        // The texture holds current minus previous in UV; Streamline wants previous minus current.
        Assert.Equal(new Vector2(-1f, -1f), constants.MotionVectorScale);

        // The game's jitter is in OpenGL clip space, Y up; the image runs Y down.
        Assert.Equal(new Vector2(0.25f, -0.125f), constants.Jitter);
        Assert.True(constants.DepthInverted);
        Assert.True(constants.CameraMotionIncluded);
        Assert.False(constants.MotionVectorsJittered);
    }

    [Fact]
    public void Basis_follows_the_rotation_and_the_camera_looks_down_minus_z()
    {
        RhiStreamlineConstants constants = ReconstructionCamera.Build(Frame(default, default));

        Assert.Equal(new Vector3(1f, 0f, 0f), constants.CameraRight);
        Assert.Equal(new Vector3(0f, 1f, 0f), constants.CameraUp);
        Assert.Equal(new Vector3(0f, 0f, -1f), constants.CameraForward);
    }

    private static ReconstructionFrame Frame(Vector3d position, Vector3d previous) => new(
        Lens,
        Lens,
        Matrix3f.Identity,
        Matrix3f.Identity,
        position,
        previous,
        0f,
        0f,
        Reset: false);
}
