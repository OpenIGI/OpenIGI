using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1.Cameras;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>Project I.G.I.'s HumanCamera orbit: modes 3 and 5 (<c>camera/humancamera.c</c>).</summary>
public sealed class Igi1HumanCameraTests
{
    private static readonly Vector3d Feet = new(0.0, 0.0, 0.0);
    private static readonly Vector3d Eye = new(0.0, 0.0, 6703.44);

    /// <summary>The camera stands at the target distance from its first frame (<c>0x4834D0</c> reads +276).</summary>
    [Fact]
    public void ItStartsAtTheTargetDistanceBehindTheBody()
    {
        Igi1HumanCamera camera = new();
        camera.Begin(Eye, Feet, facing: 0f, walls: null, death: false);

        // Facing zero looks along +Y; the camera is 2.5 m back, down -Y, level with the eye.
        Vector3d eye = camera.Camera.Position * Igi1HumanCamera.UnitsPerMetre;
        Assert.Equal(0.0, eye.X, 3);
        Assert.Equal(-Igi1HumanCamera.TargetDistanceInUnits, eye.Y, 3);
        Assert.Equal(Eye.Z, eye.Z, 3);
    }

    /// <summary>A wall between the eye and the camera pulls it in only when it hides the whole body (<c>0x483A00</c>).</summary>
    [Fact]
    public void ItPullsInOnlyWhenTheBodyIsHidden()
    {
        // A wall at y = -6000 across everything at the eye's height and above.
        WorldRayCast highWall = (Vector3d from, Vector3d end, out WorldRayHit hit) =>
            Cross(from, end, -6000.0, minZ: 4000.0, out hit);
        Igi1HumanCamera seen = new();
        seen.Begin(Eye, Feet, facing: 0f, highWall, death: false);
        Assert.Equal(-Igi1HumanCamera.TargetDistanceInUnits, seen.Camera.Position.Y * Igi1HumanCamera.UnitsPerMetre, 3);

        // The same wall reaching the ground hides every point around the body: 204.8 short of it.
        WorldRayCast fullWall = (Vector3d from, Vector3d end, out WorldRayHit hit) =>
            Cross(from, end, -6000.0, minZ: double.MinValue, out hit);
        Igi1HumanCamera hidden = new();
        hidden.Begin(Eye, Feet, facing: 0f, fullWall, death: false);
        Assert.Equal(-(6000.0 - Igi1HumanCamera.KeepOffInUnits), hidden.Camera.Position.Y * Igi1HumanCamera.UnitsPerMetre, 3);
    }

    /// <summary>
    /// Mode 5 grows its target back toward 10240 while its four lines are clear, and a facing surface pulls distance and
    /// target to 1.05 times its depth, never under a metre (<c>0x483EC0</c>).
    /// </summary>
    [Fact]
    public void TheDeathCameraCollidesItsFrameAndEasesBackOut()
    {
        Igi1HumanCamera camera = new();
        camera.Begin(Eye, Feet, facing: 0f, walls: null, death: true);
        for (int tick = 0; tick < 40; tick++)
        {
            camera.Tick(Eye, Feet);
        }

        Assert.Equal(Igi1HumanCamera.TargetDistanceInUnits, camera.TargetDistance, 3);
        Assert.Equal(Igi1HumanCamera.TargetDistanceInUnits, camera.Distance, 3);

        // A wall 8000 behind, facing the camera's lines.
        WorldRayCast wall = (Vector3d from, Vector3d end, out WorldRayHit hit) =>
            Cross(from, end, -8000.0, minZ: double.MinValue, out hit);
        Igi1HumanCamera blocked = new();
        blocked.Begin(Eye, Feet, facing: 0f, wall, death: true);
        for (int tick = 0; tick < 40; tick++)
        {
            blocked.Tick(Eye, Feet);
        }

        Assert.True(blocked.TargetDistance < Igi1HumanCamera.TargetDistanceInUnits);
        Assert.True(blocked.TargetDistance >= Igi1HumanCamera.DeathNearestInUnits);
    }

    // A plane y = wallY, facing +Y, blocking a segment that crosses it at a height of at least minZ.
    private static bool Cross(Vector3d from, Vector3d end, double wallY, double minZ, out WorldRayHit hit)
    {
        hit = default;
        if ((from.Y - wallY) * (end.Y - wallY) > 0.0 || from.Y == end.Y)
        {
            return false;
        }

        double t = (wallY - from.Y) / (end.Y - from.Y);
        Vector3d at = from + ((end - from) * t);
        if (at.Z < minZ)
        {
            return false;
        }

        hit = new WorldRayHit(at, new Vector3f(0f, 1f, 0f), Vector3d.Distance(from, at));
        return true;
    }
}
