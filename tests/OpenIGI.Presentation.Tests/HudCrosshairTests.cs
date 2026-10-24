using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The remastered crosshair's motion and its size on the screen.</summary>
public sealed class HudCrosshairTests
{
    private static PlayerAimFeedback Aim(long shots = 0, int hits = 0, int kills = 0, bool enemy = false, float spread = 0.02f, bool head = false) =>
        new(CrosshairShape.Rifle, spread, enemy, shots, hits, kills, head, head);

    [Fact]
    public void Taking_a_weapon_in_hand_marks_nothing_and_starts_at_its_spread()
    {
        HudCrosshairState state = new();
        state.Update(Aim(shots: 40, hits: 9, kills: 3, spread: 0.03f), 1f, 0f);
        Assert.Equal(0f, state.Hit);
        Assert.Equal(0f, state.Kill);
        Assert.Equal(0.03f, state.Spread, 4);
    }

    [Fact]
    public void A_round_opens_it_and_it_settles_back()
    {
        HudCrosshairState state = new();
        state.Update(Aim(), 0f, 0f);
        state.Update(Aim(shots: 1), 1f / 30f, 1f / 30f);
        float open = state.Spread;
        Assert.True(open > 0.02f + (HudCrosshairState.KickPerShot * 0.5f));
        for (int frame = 2; frame < 60; frame++)
        {
            state.Update(Aim(shots: 1), frame / 30f, 1f / 30f);
        }

        Assert.InRange(state.Spread, 0.02f - 1e-3f, 0.02f + 1e-3f);
    }

    [Fact]
    public void A_hit_marks_briefly_and_a_kill_holds_longer_with_the_head_noted()
    {
        HudCrosshairState state = new();
        state.Update(Aim(), 0f, 0f);
        state.Update(Aim(hits: 1, head: true), 0.1f, 0.1f);
        Assert.Equal(1f, state.Hit);
        Assert.True(state.HitHead);
        state.Update(Aim(hits: 1), 0.1f + HudCrosshairState.HitSeconds, HudCrosshairState.HitSeconds);
        Assert.Equal(0f, state.Hit);

        state.Update(Aim(hits: 2, kills: 1), 1f, 0.1f);
        Assert.Equal(1f, state.Kill);
        state.Update(Aim(hits: 2, kills: 1), 1f + (HudCrosshairState.KillSeconds * 0.5f), 0.1f);
        Assert.InRange(state.Kill, 0.4f, 0.6f);
    }

    [Fact]
    public void Over_an_enemy_it_turns_and_turns_back()
    {
        HudCrosshairState state = new();
        state.Update(Aim(), 0f, 0f);
        for (int frame = 1; frame <= 30; frame++)
        {
            state.Update(Aim(enemy: true), frame / 30f, 1f / 30f);
        }

        Assert.True(state.Enemy > 0.95f);
        for (int frame = 31; frame <= 60; frame++)
        {
            state.Update(Aim(), frame / 30f, 1f / 30f);
        }

        Assert.True(state.Enemy < 0.05f);
    }

    [Theory]
    [InlineData(0.0, 1920, 1.0, 0.0)]
    [InlineData(0.02, 1920, 1.0, 19.2)]
    [InlineData(0.02, 3840, 1.0, 38.4)]
    [InlineData(0.02, 1920, 0.5, 38.4)]
    public void A_spread_lands_as_far_out_as_the_view_sees_it(double spread, int width, double tanX, double pixels)
    {
        HudFrame frame = new(width, width * 9 / 16, 1, 0f, false, 0f, 0f)
        {
            Projector = new HudProjector(default, default, default, default, (float)tanX, width, width * 9 / 16),
        };
        Assert.Equal(pixels, HudCrosshair.SpreadPixels((float)spread, frame), 0.2);
    }
}
