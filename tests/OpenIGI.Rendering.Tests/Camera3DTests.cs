using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Covers <see cref="Camera3D"/>: the world-to-view convention and the double-precision camera offset
/// that the original's <c>Real64x3</c> camera position exists to provide.
/// </summary>
public sealed class Camera3DTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void ADefaultCameraLooksAlongPositiveY()
    {
        Matrix4f view = Camera3D.Default.GetViewMatrix();

        // Ten units north of the origin should sit ten units straight ahead — view +Z is forward.
        Vector3f ahead = view.TransformPoint(new Vector3f(0f, 10f, 0f));
        Assert.Equal(0f, ahead.X, Tolerance);
        Assert.Equal(0f, ahead.Y, Tolerance);
        Assert.Equal(10f, ahead.Z, Tolerance);
    }

    [Fact]
    public void HeightMapsToViewUp()
    {
        // The world's vertical axis is Z, so raising a point's third component raises it on screen.
        Vector3f raised = Camera3D.Default.GetViewMatrix().TransformPoint(new Vector3f(0f, 10f, 4f));
        Assert.Equal(4f, raised.Y, Tolerance);
    }

    [Fact]
    public void YawTurnsTheCameraTowardsPositiveX()
    {
        Camera3D camera = new(default, Camera3D.LookRotation(MathF.PI / 2f, 0f));
        Vector3f ahead = camera.GetViewMatrix().TransformPoint(new Vector3f(10f, 0f, 0f));

        Assert.Equal(0f, ahead.X, Tolerance);
        Assert.Equal(10f, ahead.Z, Tolerance);
    }

    [Fact]
    public void LookRotationIsOrthonormal()
    {
        Matrix3f rotation = Camera3D.LookRotation(0.7f, -0.3f);

        foreach (Vector3f row in new[] { rotation.Row1, rotation.Row2, rotation.Row3 })
        {
            Assert.Equal(1f, MathF.Sqrt((row.X * row.X) + (row.Y * row.Y) + (row.Z * row.Z)), Tolerance);
        }

        Assert.Equal(0f, Dot(rotation.Row1, rotation.Row2), Tolerance);
        Assert.Equal(0f, Dot(rotation.Row1, rotation.Row3), Tolerance);
        Assert.Equal(0f, Dot(rotation.Row2, rotation.Row3), Tolerance);
    }

    [Fact]
    public void ModelViewPlacesAModelRelativeToTheCamera()
    {
        Camera3D camera = new(new Vector3d(100d, 200d, 30d), Camera3D.LookRotation(0f, 0f));
        Matrix4f modelView = camera.CreateModelView(new Vector3d(100d, 260d, 30d));

        Vector3f origin = modelView.TransformPoint(default);
        Assert.Equal(0f, origin.X, Tolerance);
        Assert.Equal(0f, origin.Y, Tolerance);
        Assert.Equal(60f, origin.Z, Tolerance);
    }

    [Fact]
    public void ModelViewSubtractsInDoublePrecisionBeforeNarrowing()
    {
        // Level coordinates run to thousands of units. Narrowing the camera position first — which is
        // what GetViewMatrix has to do — loses the small offset entirely; CreateModelView keeps it.
        Vector3d cameraPosition = new(8_388_608d, 0d, 0d);
        Vector3d modelPosition = new(8_388_608.5d, 0d, 0d);

        Camera3D camera = new(cameraPosition, Camera3D.LookRotation(0f, 0f));
        float kept = camera.CreateModelView(modelPosition).TransformPoint(default).X;

        Assert.Equal(0.5f, kept, Tolerance);

        float lost = camera.GetViewMatrix().TransformPoint(new Vector3f((float)modelPosition.X, 0f, 0f)).X;
        Assert.Equal(0f, lost);
    }

    [Fact]
    public void ModelOrientationComposesWithTheCameraRotation()
    {
        Camera3D camera = new(default, Camera3D.LookRotation(MathF.PI / 2f, 0f));

        // The model is itself yawed a quarter turn the other way, so the two cancel.
        Matrix3f modelRotation = Matrix3f.FromRows(
            new Vector3f(0f, 1f, 0f),
            new Vector3f(-1f, 0f, 0f),
            new Vector3f(0f, 0f, 1f));

        Vector3f result = camera.CreateModelView(default, modelRotation).TransformPoint(new Vector3f(0f, 10f, 0f));
        Assert.Equal(10f, result.Z, Tolerance);
    }

    private static float Dot(Vector3f a, Vector3f b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
