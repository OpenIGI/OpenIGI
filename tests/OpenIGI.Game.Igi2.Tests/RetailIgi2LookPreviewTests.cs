using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Player;
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
        // The mouse moved down looks down, and up when the profile inverts it.
        Matrix3f down = player.PreviewLook(0f, 10000f, 1f, false);
        Matrix3f up = player.PreviewLook(0f, 10000f, 1f, true);
        Assert.InRange(down.Column2.Z, -1f, -0.99f);
        Assert.InRange(up.Column2.Z, 0.99f, 1f);
        player.Human.View.IsLockOrientation = true;
        Assert.False(player.CanPreviewLook);
        Assert.Equal(player.LookOrientation, player.PreviewLook(255f, 255f, 1f, false));
    }

    /// <summary>Consuming horizontal mouse input does not rewind presentation on a retail building floor.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Grounded_mouse_preview_matches_the_committed_tick()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain");
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world,
            Igi2Session.Start().Require<Igi2PlayerTuning>().General, default, 0f, AiDifficulty.Normal);
        foreach (Igi2RigidObject building in world.Objects.Where(static o => o.Owner is Igi2CollisionOwner { TypeName: "Building", Depth: 0 }))
        {
            Vector3d above = new(building.Position.X, building.Position.Y, building.Position.Z + 32768.0);
            Vector3d feet = above;
            player.Human.GroundCollision(above, ref feet, 65536f);
            if (player.Human.IsGroundRigidObject)
            {
                player.Human.Position = feet;
                player.Human.UpdatePlatformPosition(true, true);
                break;
            }
        }

        Assert.True(player.Human.IsGroundRigidObject);
        for (int tick = 0; tick < 60; tick++)
        {
            Matrix3f predicted = player.PreviewLook(8f, 0f, 0.4f, false);
            player.Tick(new TitlePlayerInput(8f, 0f, default, default, 0.4f, false, UnfilteredMouse: true));
            Vector3f expected = predicted.Column2, actual = player.ViewOrientation.Row3;
            Assert.InRange((actual - expected).Length, 0f, 0.00001f);
            Assert.True(player.Human.IsGroundRigidObject);
        }
    }

    /// <summary>Both axes retain their last rendered aim when a tick consumes small, fast or reversed mouse motion.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Unfiltered_mouse_preview_and_tick_agree_on_both_axes()
    {
        foreach (float fov in new[] { 1f, 0.25f })
        {
            foreach (bool invert in new[] { false, true })
            {
                Igi2TitlePlayer player = Player();
                player.Human.View.FieldOfView = fov;
                foreach (float counts in new[] { 1f, 3f, 12f, -7f, 512f, -512f, 0f, 2f })
                {
                    Matrix3f preview = player.PreviewLook(counts, counts * 0.02f, 0.4f, invert);
                    player.Tick(new TitlePlayerInput(counts, counts * 0.02f, default, default, 0.4f, invert,
                        UnfilteredMouse: true));
                    Assert.InRange((preview.Column2 - player.ViewOrientation.Row3).Length, 0f, 0.00001f);
                    Assert.InRange((preview.Column2 - player.PreviewLook(0f, 0f, 0.4f, invert).Column2).Length, 0f, 0.00001f);
                }
            }
        }
    }

    /// <summary>Stopping the mouse leaves no next-tick pitch tail; fast horizontal movement is not capped at 255 counts.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Unfiltered_mouse_has_no_release_tail_or_tick_speed_cap()
    {
        Igi2TitlePlayer player = Player();
        player.Tick(new TitlePlayerInput(512f, 4f, default, default, 0.4f, false, UnfilteredMouse: true));
        float expected = -MouseLookSettings.FromSensitivity(0.4f, false).YawDelta(512f);
        Assert.InRange(MathF.Abs(player.Human.Gamma - expected), 0f, 0.000001f);
        float pitch = player.Human.View.IdealPitch;
        player.Tick(new TitlePlayerInput(0f, 0f, default, default, 0.4f, false, UnfilteredMouse: true));
        Assert.Equal(pitch, player.Human.View.IdealPitch);
        player.Human.Channels.IsLocked = true;
        Assert.False(player.CanPreviewLook);
        Assert.Equal(player.LookOrientation, player.PreviewLook(100f, 100f, 0.4f, false));

        Igi2TitlePlayer retail = Player();
        retail.Tick(new TitlePlayerInput(512f, 4f, default, default, 0.4f, false));
        Assert.InRange(retail.Human.Gamma, -1.257f, -1.256f);
        float filteredPitch = retail.Human.View.IdealPitch;
        retail.Tick(new TitlePlayerInput(0f, 0f, default, default, 0.4f, false));
        Assert.True(retail.Human.View.IdealPitch < filteredPitch);
    }

    /// <summary>Both titles use the same physical count-to-angle curve and zoom scaling, and look the same way.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Smooth_mouse_matches_igi1_at_every_sensitivity_and_zoom()
    {
        foreach (float sensitivity in new[] { 0f, 0.05f, 0.4f, 1f })
        {
            foreach (float fov in new[] { 0.25f, 1f })
            {
                foreach (bool invert in new[] { false, true })
                {
                    Igi2TitlePlayer player = Player();
                    player.Human.View.FieldOfView = fov;
                    MouseLookSettings igi1 = MouseLookSettings.FromSensitivity(sensitivity, invert);
                    float zoom = MouseLookSettings.ZoomSensitivityScale(fov);
                    player.Tick(new TitlePlayerInput(19.5f, -7.25f, default, default, sensitivity, invert, UnfilteredMouse: true));
                    Assert.InRange(MathF.Abs(player.Human.Gamma + (igi1.YawDelta(19.5f) * zoom)), 0f, 0.000001f);
                    Assert.InRange(MathF.Abs(player.Human.View.IdealPitch - (igi1.PitchDelta(-7.25f) * zoom)), 0f, 0.000001f);
                }
            }
        }
    }

    /// <summary>A reversal at the pitch limit is applied immediately, even before the next simulation tick.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Frame_pitch_clamping_does_not_swallow_a_reversal()
    {
        Igi2TitlePlayer player = Player();
        player.Human.View.IdealPitch = -Humans.Igi2HumanView.PitchLimit + 0.0001f;
        player.GatherLook(0f, 10000f, 0.4f, false);
        player.GatherLook(0f, -1f, 0.4f, false);
        Matrix3f preview = player.PreviewLook(0f, 9999f, 0.4f, false);
        player.Tick(new TitlePlayerInput(0f, 9999f, default, default, 0.4f, false, UnfilteredMouse: true));
        float expected = -Humans.Igi2HumanView.PitchLimit + MouseLookSettings.FromSensitivity(0.4f, false).PitchDelta(-1f);
        Assert.InRange(MathF.Abs(player.Human.View.IdealPitch - expected), 0f, 0.000001f);
        Assert.InRange((preview.Column2 - player.ViewOrientation.Row3).Length, 0f, 0.00001f);
    }

    /// <summary>Fractional counts and reversals survive several render frames per tick, without reapplying consumed motion.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Frame_motion_is_committed_once_without_losing_counts()
    {
        Igi2TitlePlayer player = Player();
        MouseLookSettings igi1 = MouseLookSettings.Default;
        float gamma = 0f, pitch = 0f;
        float[] counts = [0.125f, -0.25f, 1f, -2f, 4f, 0.25f, 0.125f];
        for (int tick = 0; tick < 30; tick++)
        {
            float sum = 0f;
            foreach (float count in counts)
            {
                player.GatherLook(count, count, 0.4f, false);
                gamma -= igi1.YawDelta(count);
                pitch += igi1.PitchDelta(count);
                sum += count;
            }
            Matrix3f preview = player.PreviewLook(sum, sum, 0.4f, false);
            player.Tick(new TitlePlayerInput(sum, sum, default, default, 0.4f, false, UnfilteredMouse: true));
            Assert.InRange(MathF.Abs(player.Human.Gamma - gamma), 0f, 0.00001f);
            Assert.InRange(MathF.Abs(player.Human.View.IdealPitch - pitch), 0f, 0.00001f);
            Assert.InRange((preview.Column2 - player.ViewOrientation.Row3).Length, 0f, 0.00001f);
            float committed = player.Human.Gamma;
            player.Tick(RetailIgi2Mission1.Idle with { UnfilteredMouse = true });
            Assert.Equal(committed, player.Human.Gamma);
        }
    }

    /// <summary>Smoothing preserves the established title direction for both signs and both inversion settings.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Smooth_vertical_direction_matches_retail_input()
    {
        foreach (bool invert in new[] { false, true })
        {
            foreach (float counts in new[] { -8f, 8f })
            {
                Igi2TitlePlayer retail = Player(), smooth = Player();
                retail.Tick(new TitlePlayerInput(0f, counts, default, default, 0.4f, invert));
                smooth.GatherLook(0f, counts, 0.4f, invert);
                Matrix3f preview = smooth.PreviewLook(0f, counts, 0.4f, invert);
                smooth.Tick(new TitlePlayerInput(0f, counts, default, default, 0.4f, invert, UnfilteredMouse: true));
                Assert.NotEqual(0f, retail.ViewOrientation.Row3.Z);
                Assert.Equal(MathF.Sign(retail.ViewOrientation.Row3.Z), MathF.Sign(smooth.ViewOrientation.Row3.Z));
                Assert.Equal(MathF.Sign(retail.ViewOrientation.Row3.Z), MathF.Sign(preview.Column2.Z));
            }
        }
    }

    private static Igi2TitlePlayer Player() => Igi2TitlePlayer.Spawn(new Igi2CollisionWorld([], null),
        Igi2Session.Start().Require<Igi2PlayerTuning>().General, default, 0f, AiDifficulty.Normal);
}
