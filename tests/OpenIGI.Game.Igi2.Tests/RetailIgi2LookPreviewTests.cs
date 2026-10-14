using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Pending mouse motion can change presentation without running a retail simulation tick.</summary>
public sealed class RetailIgi2LookPreviewTests
{
    /// <summary>Several render frames see distinct aim, while the body's aim and channel history stay unchanged.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Pending_counts_change_the_view_without_changing_the_human()
    {
        Igi2TitlePlayer player = Player();
        float gamma = player.Human.Gamma, pitch = player.Human.View.IdealPitch;
        Vector3d position = player.Position;
        Matrix3f first = player.PreviewLook(1f, 0f, 0.4f, false);
        Matrix3f second = player.PreviewLook(2f, 0f, 0.4f, false);
        Matrix3f third = player.PreviewLook(3f, 0f, 0.4f, false);
        Assert.True(first.Column2.X > 0f);
        Assert.True(second.Column2.X > first.Column2.X);
        Assert.True(third.Column2.X > second.Column2.X);
        Assert.Equal(gamma, player.Human.Gamma);
        Assert.Equal(pitch, player.Human.View.IdealPitch);
        Assert.Equal(position, player.Position);
        Assert.Equal(player.LookOrientation, player.PreviewLook(0f, 0f, 0.4f, false));
    }

    /// <summary>Preview honors pitch bounds, inversion and scripted view locks.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Preview_respects_pitch_bounds_and_view_locks()
    {
        Igi2TitlePlayer player = Player();
        Matrix3f up = player.PreviewLook(0f, 255f, 1f, false);
        Matrix3f down = player.PreviewLook(0f, 255f, 1f, true);
        Assert.InRange(up.Column2.Z, 0.99f, 1f);
        Assert.InRange(down.Column2.Z, -1f, -0.99f);
        player.Human.View.IsLockOrientation = true;
        Assert.False(player.CanPreviewLook);
        Assert.Equal(player.LookOrientation, player.PreviewLook(255f, 255f, 1f, false));
    }

    private static Igi2TitlePlayer Player() => Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
        Igi2Session.Start().Require<Igi2PlayerTuning>().General, default, 0f, AiDifficulty.Normal);
}
