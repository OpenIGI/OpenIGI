using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>
/// The look Project I.G.I.'s title player shows between ticks is handed to the shared presenter as local-to-world axes in
/// columns (right, forward, up); read back the presenter's way it is the camera the rig itself would have.
/// </summary>
public sealed class Igi1TitleLookTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.7f, 0.3f)]
    [InlineData(-2.4f, -0.9f)]
    [InlineData(3.0f, 1.2f)]
    public void The_previewed_look_reads_as_the_rigs_camera(float yaw, float pitch)
    {
        Matrix3f look = Igi1TitlePlayer.LookAxes(yaw, pitch);

        // TitlePlayerPresentation.CameraOrientation: rows right, up, forward from the look's columns 1, 3, 2.
        Matrix3f camera = Matrix3f.FromRows(look.Column1, look.Column3, look.Column2);
        Matrix3f expected = Camera3D.LookRotation(yaw, pitch);

        AssertClose(expected.Row1, camera.Row1);
        AssertClose(expected.Row2, camera.Row2);
        AssertClose(expected.Row3, camera.Row3);
    }

    [Fact]
    public void A_turn_between_ticks_turns_the_view_about_the_worlds_up_axis()
    {
        // What the presenter shows between ticks: the last tick's camera, carried from its look to the previewed one.
        const float yaw = 0.4f, pitch = 0.25f, turned = 0.45f;
        Matrix3f eye = Camera3D.LookRotation(yaw, pitch);
        static Matrix3f Read(Matrix3f look) => Matrix3f.FromRows(look.Column1, look.Column3, look.Column2);
        Matrix3f effect = eye * Read(Igi1TitlePlayer.LookAxes(yaw, pitch)).Transposed();
        Matrix3f shown = effect * Read(Igi1TitlePlayer.LookAxes(turned, pitch));

        Matrix3f expected = Camera3D.LookRotation(turned, pitch);
        AssertClose(expected.Row1, shown.Row1);
        AssertClose(expected.Row2, shown.Row2);
        AssertClose(expected.Row3, shown.Row3);
    }

    private static void AssertClose(Vector3f expected, Vector3f actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }
}
