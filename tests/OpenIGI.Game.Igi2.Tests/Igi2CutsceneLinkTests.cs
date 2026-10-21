using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary><c>EditCamera_TransformRelativeToLinkQTask</c>: a linked camera keeps its place relative to the link as the link moves.</summary>
public sealed class Igi2CutsceneLinkTests
{
    [Fact]
    public void A_linked_camera_moves_and_turns_with_its_link()
    {
        // The link stood at the origin facing along X; it now stands at (1000, 0, 0) turned a quarter about Z.
        Link link = new(new Vector3d(1000.0, 0.0, 0.0), [0f, -1f, 0f, 1f, 0f, 0f, 0f, 0f, 1f]);
        Vector3d position = new(100.0, 0.0, 50.0);
        float[] frame = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

        Igi2CutsceneCameras.TransformRelativeToLink(ref position, frame, default, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], link);

        Assert.Equal(1000.0, position.X, 3);
        Assert.Equal(100.0, position.Y, 3);
        Assert.Equal(50.0, position.Z, 3);
        Assert.Equal(link.CameraTargetOrientation, frame);
    }

    private sealed record Link(Vector3d CameraTargetPosition, IReadOnlyList<float> CameraTargetOrientation) : IIgi2CameraTarget;
}
