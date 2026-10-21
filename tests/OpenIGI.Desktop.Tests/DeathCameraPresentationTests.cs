using OpenIGI.Presentation.Cameras;
using OpenIGI.Core.Numerics;
using OpenIGI.Desktop;
using Xunit;

namespace OpenIGI.Desktop.Tests;

public sealed class DeathCameraPresentationTests
{
    [Fact]
    public void FollowingACorpsePresentsBetweenItsTickPositions()
    {
        DeathCamera camera = new();
        camera.Begin(default, gamma: 0f);
        Vector3d previous = camera.Camera.Position;

        camera.Follow(new Vector3d(DeathCamera.UnitsPerMetre, 0.0, DeathCamera.UnitsPerMetre));
        camera.Present(1f);
        Vector3d current = camera.Camera.Position;
        camera.Present(0.5f);
        Vector3d halfway = camera.Camera.Position;

        Assert.Equal((previous.X + current.X) * 0.5, halfway.X, 6);
        Assert.Equal((previous.Y + current.Y) * 0.5, halfway.Y, 6);
        Assert.Equal((previous.Z + current.Z) * 0.5, halfway.Z, 6);
    }
}
