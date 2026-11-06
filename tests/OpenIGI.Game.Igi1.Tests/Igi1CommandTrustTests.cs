using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>An I.G.I. command as the server reads it: a look and a crosshair it can believe.</summary>
public sealed class Igi1CommandTrustTests
{
    [Fact]
    public void A_crosshair_ray_from_further_off_than_the_camera_reaches_is_drawn_back_to_it()
    {
        float[] channels = Pack(new Vector3d(50 * WorldUnits.PerMetre, 0, 0), new Vector3f(0, 0, 1.5f));

        (Vector3d offset, Vector3f direction) = Assert.NotNull(Igi1Command.UnpackAim(channels));

        Assert.Equal(Igi1Command.MaxAimReach, offset.Length, 3);
        Assert.Equal(1f, MathF.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z)), 4);
    }

    [Fact]
    public void A_crosshair_ray_within_reach_is_kept_as_sent()
    {
        Vector3d over = new(-2.6 * WorldUnits.PerMetre, 0.55 * WorldUnits.PerMetre, 0.25 * WorldUnits.PerMetre);
        float[] channels = Pack(over, new Vector3f(1, 0, 0));

        (Vector3d offset, Vector3f direction) = Assert.NotNull(Igi1Command.UnpackAim(channels));

        Assert.Equal(over.X, offset.X, 0);
        Assert.Equal(1f, direction.X);
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0.01f, 0f, 0f)]
    [InlineData(100f, 0f, 0f)]
    public void A_direction_that_is_no_direction_leaves_the_player_in_first_person(float x, float y, float z) =>
        Assert.Null(Igi1Command.UnpackAim(Pack(default, new Vector3f(x, y, z))));

    [Fact]
    public void A_look_that_is_no_number_is_read_as_looking_ahead_and_the_fly_chord_is_dropped()
    {
        PlayerInputFrame frame = default;
        frame[(int)PlayerInputSlot.DebugFlyPressed] = 1f;
        float[] channels = new float[Igi1Command.ChannelCount];
        Igi1Command.Pack(frame, float.NaN, float.PositiveInfinity, channels);

        PlayerInputFrame read = Igi1Command.Unpack(channels, out float yaw, out float pitch);

        Assert.Equal(0f, yaw);
        Assert.Equal(0f, pitch);
        Assert.False(read.IsSet(PlayerInputSlot.DebugFlyPressed));
    }

    private static float[] Pack(Vector3d offset, Vector3f direction)
    {
        float[] channels = new float[Igi1Command.ChannelCount];
        Igi1Command.Pack(default, 0f, 0f, channels, (offset, direction));
        return channels;
    }
}
