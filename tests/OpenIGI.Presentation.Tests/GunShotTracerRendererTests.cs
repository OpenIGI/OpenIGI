using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Weapons;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>
/// The tracer draw: a round's pass, filed in world units, drawn from a camera the scene places in metres.
/// </summary>
/// <remarks>
/// Every tracer the port drew before this was placed kilometres from the eye: the renderer subtracted the camera's
/// position in metres from the round's in units, and the result lay far behind the near plane. A test that built its
/// tracer from the camera's position made the same mistake and passed, so these build theirs in units, as the march
/// files them, from a camera far from the origin, as a level puts it.
/// </remarks>
public sealed class GunShotTracerRendererTests
{
    private const double Unit = 4096.0;

    // Mission 1's start, roughly: far enough from the origin that metres and units cannot be confused.
    private static readonly Vector3d EyeMetres = new(6000.6, -13635.9, 42583.0);

    private static readonly AmmoType Rifle = new()
    {
        CalibreId = 762,
        DrawsTracer = true,
        NearColour = new TracerColour(250, 200, 0),
        FarColour = new TracerColour(255, 255, 0),
    };

    private static readonly Matrix4f Projection = Matrix4f.CreateProjection(800f, 800f, 640f, 360f, 1280, 720, 0.1f, 2000f);

    // Looking along +Y.
    private static readonly Camera3D Camera = new(EyeMetres, Camera3D.LookRotation(0f, 0f));

    [Fact]
    public void AGuardsRoundInFrontOfTheEyeIsDrawnBesideIt()
    {
        using RecordingGraphicsDevice device = new();
        using GunShotTracerRenderer renderer = new(device);

        // From a muzzle thirty metres ahead to a point a metre in front of the eye, in world units.
        GunShotTracer tracer = Round(new Vector3d(0.0, 30.0, -0.3), new Vector3d(0.2, 1.0, -0.5));

        renderer.Draw([tracer], Camera, Projection, 720, TracerStyle.Retail);

        Assert.Equal(1, renderer.LastDrawCount);
        Assert.Equal(0, tracer.Life);

        // The strip lies where the round is, relative to the eye, in metres: not kilometres off.
        Vertex3D[] strip = device.Meshes[device.MeshDraws[^1].Mesh.Id].Vertices[..4];
        Assert.All(strip, v => Assert.InRange(v.Position.Y, 0.05f, 30.5f));
        Assert.All(strip, v => Assert.InRange(MathF.Abs(v.Position.X), 0f, 1f));
    }

    [Fact]
    public void ARoundBehindTheEyeDrawsNothing()
    {
        using RecordingGraphicsDevice device = new();
        using GunShotTracerRenderer renderer = new(device);

        renderer.Draw([Round(new Vector3d(0.0, -5.0, 0.0), new Vector3d(1.0, -30.0, 0.0))], Camera, Projection, 720, TracerStyle.Retail);

        Assert.Equal(0, renderer.LastDrawCount);
    }

    [Fact]
    public void TheRemasteredStreakFliesTowardsTheEye()
    {
        using RecordingGraphicsDevice device = new();
        using GunShotTracerRenderer renderer = new(device);
        GunShotTracer tracer = Round(new Vector3d(0.0, 30.0, -0.3), new Vector3d(0.2, 1.0, -0.5));

        // The first sight starts the streak at the muzzle; a moment later its head is on its way in.
        renderer.Draw([tracer], Camera, Projection, 720, TracerStyle.Remastered);
        Assert.Equal(1, renderer.StreaksInFlight);
        Thread.Sleep(25);
        renderer.Draw([], Camera, Projection, 720, TracerStyle.Remastered);

        Assert.Equal(1, renderer.LastDrawCount);
    }

    [Fact]
    public void ARoundComingStraightAtTheEyeIsStillDrawn()
    {
        using RecordingGraphicsDevice device = new();
        using GunShotTracerRenderer renderer = new(device);

        // Seen end-on, a line has no width of its own; it must still be drawn, as the point it is.
        renderer.Draw([Round(new Vector3d(0.0, 25.0, 0.0), new Vector3d(0.0, 1.0, 0.0))], Camera, Projection, 720, TracerStyle.Retail);

        Assert.Equal(1, renderer.LastDrawCount);
    }

    [Fact]
    public void ALongFrameStretchesTheStreakToTheDistanceItMoved()
    {
        using RecordingGraphicsDevice device = new();
        using GunShotTracerRenderer renderer = new(device);

        // A round coming from forty metres ahead, passing a hand's width off the eye. Forty milliseconds between frames is fifteen metres of flight, three
        // times the unstretched trail: the streak covers all of it, so the frames' streaks meet end to end.
        renderer.Draw([Round(new Vector3d(0.3, 40.0, -0.2), new Vector3d(0.3, 1.0, -0.2))], Camera, Projection, 720, TracerStyle.Remastered);
        Thread.Sleep(40);
        renderer.Draw([], Camera, Projection, 720, TracerStyle.Remastered);

        Assert.Equal(1, renderer.LastDrawCount);
        Vertex3D[] strip = device.Meshes[device.MeshDraws[^1].Mesh.Id].Vertices[..4];
        float length = MathF.Abs(strip[0].Position.Y - strip[2].Position.Y);
        Assert.True(length > 2f * GunShotTracerRenderer.StreakLengthMetres, $"the streak is {length:0.0} m long");
    }

    // A pass from one camera-relative point to another, both in metres, filed in world units.
    private static GunShotTracer Round(Vector3d fromMetres, Vector3d toMetres)
    {
        Vector3d start = (EyeMetres + fromMetres) * Unit;
        Vector3d end = (EyeMetres + toMetres) * Unit;
        return new GunShotTracer
        {
            Start = start,
            End = end,
            StartDistance = 0f,
            EndDistance = (float)(end - start).Length,
            Range = 300f * (float)Unit,
            Ammunition = Rifle,
            Life = GunShotBallistics.TracerLifetimeTicks,
        };
    }
}
