using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The I.G.I. 2 player watched over the shoulder: its rounds turn onto the crosshair's ray, and in first person nothing about them
/// changes at all.
/// </summary>
public sealed class Igi2OverShoulderTests
{
    private static Igi2TitlePlayer Player() => Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
        Igi2HumanPlayerGeneral.Unset with { MaxDamageScale = 1f }, default, 0.3f, Game.Ai.AiDifficulty.Normal);

    private static void Tick(Igi2TitlePlayer player, TitleAim? aim = null) =>
        player.Tick(new TitlePlayerInput(0f, 0f, default, default, 0.4f, false, Aim: aim));

    private static Vector3f Column(float[] o, int c) => new(o[c], o[3 + c], o[6 + c]);

    [Fact]
    public void In_first_person_the_fire_frame_is_the_views_own()
    {
        Igi2TitlePlayer player = Player();
        ((ITitleOverShoulder)player).PresentOverShoulder(false);
        Tick(player);

        Matrix3f v = player.Human.View.Orientation;
        Igi2FireFrameAssert.Equal([v.M11, v.M12, v.M13, v.M21, v.M22, v.M23, v.M31, v.M32, v.M33], player.Human.GetFireFrame(addInaccuracy: false).Orientation);
        Assert.False(player.Human.IsOverShoulder);
    }

    [Fact]
    public void Over_the_shoulder_the_round_turns_onto_the_crosshairs_ray_and_keeps_its_hand()
    {
        Igi2TitlePlayer player = Player();
        Tick(player);
        Matrix3f v = player.Human.View.Orientation;
        float[] view = [v.M11, v.M12, v.M13, v.M21, v.M22, v.M23, v.M31, v.M32, v.M33];
        Vector3f right = Column(view, 0);
        Vector3f forward = Column(view, 1);

        // A camera half a metre right of the eye, looking the way the eye looks: what it is over is that far right, ahead.
        Vector3d eye = player.Human.View.Position;
        Vector3d origin = new(eye.X + (right.X * 2048.0), eye.Y + (right.Y * 2048.0), eye.Z + (right.Z * 2048.0));
        ((ITitleOverShoulder)player).PresentOverShoulder(true);
        Tick(player, new TitleAim(origin - player.EyePosition, forward));

        float[] fired = player.Human.GetFireFrame(addInaccuracy: false).Orientation;
        Vector3f turned = Column(fired, 1);
        Assert.True(player.Human.IsOverShoulder);
        Assert.True(Vector3f.Dot(turned, Column(view, 0)) > 0f, "the round turns toward the camera's side");
        Assert.True(Vector3f.Dot(turned, forward) > 0.999f, "and only a little");

        float handBefore = Vector3f.Dot(Vector3f.Cross(Column(view, 0), Column(view, 1)), Column(view, 2));
        float handAfter = Vector3f.Dot(Vector3f.Cross(Column(fired, 0), Column(fired, 1)), Column(fired, 2));
        Assert.Equal(MathF.Sign(handBefore), MathF.Sign(handAfter));
    }

    private static class Igi2FireFrameAssert
    {
        public static void Equal(float[] expected, float[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(BitConverter.SingleToInt32Bits(expected[i]), BitConverter.SingleToInt32Bits(actual[i]));
            }
        }
    }
}
