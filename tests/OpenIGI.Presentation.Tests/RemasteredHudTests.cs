using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Igi1.Hud;
using OpenIGI.Presentation.Text;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>The remastered display's shared kit: what it does about change, where it places its parts, and how it is drawn.</summary>
public sealed class RemasteredHudTests
{
    [Fact]
    public void A_hit_shakes_the_display_and_leaves_a_trail_that_falls_behind_the_health()
    {
        HudReactiveState state = new();
        HudModel model = new() { Show = HudParts.All, Health = 1f };
        state.Update(model, 0f);
        Assert.Equal(0f, state.Shake);

        model.Health = 0.5f;
        state.Update(model, 1f);
        Assert.True(state.Shake > 0f);
        Assert.True(state.HealthTrail > state.Health);

        // The shake has run its course; the trail has caught up.
        for (float t = 1.05f; t < 4f; t += 0.05f)
        {
            state.Update(model, t);
        }

        Assert.Equal(0f, state.Shake);
        Assert.InRange(state.HealthTrail - state.Health, 0f, 0.01f);
    }

    [Fact]
    public void A_new_weapon_slides_in_and_its_count_is_shown_as_it_is_then_rolls_to_each_new_count()
    {
        HudReactiveState state = new();
        HudModel model = new() { Show = HudParts.All };
        model.Weapon = Weapon("WEAPON_ID_MP5", 30);
        state.Update(model, 0f);
        Assert.Equal(1f, state.Switch);
        Assert.Equal(30f, state.Rounds);

        model.Weapon = Weapon("WEAPON_ID_MP5", 29);
        state.Update(model, 0.016f);
        Assert.InRange(state.Rounds, 29f, 30f);

        model.Weapon = Weapon("WEAPON_ID_GLOCK", 17);
        state.Update(model, 1f);
        Assert.Equal(17f, state.Rounds);
        Assert.Equal(0f, state.Switch);
        state.Update(model, 1f + HudReactiveState.SwitchSeconds);
        Assert.Equal(1f, state.Switch);
    }

    [Fact]
    public void Visibility_past_the_warning_pulses_and_the_display_flickers_in_after_being_hidden()
    {
        HudReactiveState state = new();
        HudModel model = new() { Show = HudParts.None, Visibility = 0.2f };
        state.Update(model, 0f);
        model.Show = HudParts.All;
        state.Update(model, 1f);
        Assert.Equal(0f, state.Appear);
        state.Update(model, 1f + HudReactiveState.AppearSeconds);
        Assert.Equal(1f, state.Appear);

        Assert.Equal(0f, state.AlertPulse);
        model.Visibility = 0.9f;
        state.Update(model, 2f);
        Assert.Equal(1f, state.AlertPulse);
    }

    [Fact]
    public void Contextual_density_dims_an_idle_weapon_and_brings_it_back_on_a_change_or_when_low()
    {
        HudReactiveState state = new();
        HudModel model = new() { Show = HudParts.All, Weapon = Weapon("WEAPON_ID_MP5", 30) };
        for (float t = 0f; t <= 6f; t += 0.1f)
        {
            state.Update(model, t);
        }

        Assert.Equal(1f, state.Presence(HudParts.Weapon, HudDensity.Full, model));
        Assert.Equal(0.4f, state.Presence(HudParts.Weapon, HudDensity.Contextual, model), 2);
        Assert.Equal(0f, state.Presence(HudParts.Weapon, HudDensity.Minimal, model), 2);

        // A shot: the count changes and the weapon comes back.
        model.Weapon = Weapon("WEAPON_ID_MP5", 29);
        state.Update(model, 6.1f);
        Assert.Equal(1f, state.Presence(HudParts.Weapon, HudDensity.Minimal, model));

        // Low: it stays.
        model.Weapon = Weapon("WEAPON_ID_MP5", 3);
        for (float t = 6.2f; t <= 15f; t += 0.1f)
        {
            state.Update(model, t);
        }

        Assert.Equal(1f, state.Presence(HudParts.Weapon, HudDensity.Minimal, model));
    }

    [Fact]
    public void The_inventory_never_dims_at_any_density()
    {
        HudReactiveState state = new();
        HudModel model = new() { Show = HudParts.All, Weapon = Weapon("WEAPON_ID_MP5", 30) };
        for (float t = 0f; t <= 15f; t += 0.1f)
        {
            state.Update(model, t);
        }

        Assert.Equal(1f, state.Presence(HudParts.Inventory, HudDensity.Contextual, model));
        Assert.Equal(1f, state.Presence(HudParts.Inventory, HudDensity.Minimal, model));
    }

    [Fact]
    public void A_point_ahead_projects_to_the_middle_and_one_to_the_right_lands_right()
    {
        HudProjector view = HudProjector.FromYawPitch(default, 0f, 0f, 1f, 1920f, 1080f);
        Assert.True(view.TryProject(new OpenIGI.Core.Numerics.Vector3d(0, 4096, 0), out OpenIGI.UI.Layout.UiVec2 ahead));
        Assert.Equal(960f, ahead.X, 1);
        Assert.Equal(540f, ahead.Y, 1);
        Assert.True(view.TryProject(new OpenIGI.Core.Numerics.Vector3d(2048, 4096, 0), out OpenIGI.UI.Layout.UiVec2 right));
        Assert.Equal(960f + 480f, right.X, 1);
        Assert.False(view.TryProject(new OpenIGI.Core.Numerics.Vector3d(0, -4096, 0), out _));
    }

    [Theory]
    [InlineData(640, 480, 1)]
    [InlineData(1920, 1080, 2)]
    [InlineData(2560, 1440, 3)]
    [InlineData(3840, 2160, 4)]
    public void A_themes_parts_stand_where_its_anchors_say_at_the_shared_interface_scale(int width, int height, int scale)
    {
        HudTheme theme = Igi1HudTheme.Theme;
        Assert.Equal(scale, theme.ScaleFor(UiScale.Automatic, width, height));
        (float x, float y) = theme.Place(HudElement.Weapon, width, height, scale)!.Value;
        Assert.Equal(width - (10f * scale), x);
        Assert.Equal(height - (10f * scale), y);
        Assert.Null(theme.Place(HudElement.Inventory, width, height, scale));
    }

    [Fact]
    public void The_display_is_drawn_in_its_own_layer_and_laid_over_through_the_visor_only_with_the_effects_on()
    {
        Assert.True(UiFontLibrary.TypographyAvailable);
        using RecordingGraphicsDevice device = new();
        using RemasteredHud hud = new(device, NullLogger.Instance);
        Assert.True(hud.Load());
        HudModel model = new() { Show = HudParts.All, Health = 0.7f, Weapon = Weapon("WEAPON_ID_MP5", 12) };
        SpriteBatch batch = new(device);

        batch.Begin();
        hud.Draw(batch, model, Igi1HudTheme.Theme, NoIcons.Instance, new HudFrame(1920, 1080, UiScale.Automatic, 1f, Effects: true, Intensity: 0.8f, CrosshairSpread: 8f));
        batch.End();
        Assert.Collection(
            device.HudLayerEvents,
            begin => Assert.False(begin.Composed),
            compose =>
            {
                Assert.True(compose.Composed);
                Assert.True(compose.DrawsBefore > device.HudLayerEvents[0].DrawsBefore);
                Assert.Equal(0.8f, compose.Fx.Intensity);
                Assert.Equal(Igi1HudTheme.Theme.Fx.Curvature, compose.Fx.Curvature);
            });

        // Off, and on a device without the layer, it is drawn straight over the frame.
        device.HudLayerEvents.Clear();
        batch.Begin();
        hud.Draw(batch, model, Igi1HudTheme.Theme, NoIcons.Instance, new HudFrame(1920, 1080, UiScale.Automatic, 2f, Effects: false, Intensity: 1f, CrosshairSpread: 8f));
        batch.End();
        device.SupportsHudLayer = false;
        batch.Begin();
        hud.Draw(batch, model, Igi1HudTheme.Theme, NoIcons.Instance, new HudFrame(1920, 1080, UiScale.Automatic, 3f, Effects: true, Intensity: 1f, CrosshairSpread: 8f));
        batch.End();
        Assert.Empty(device.HudLayerEvents);
    }

    private static HudWeaponView Weapon(string key, int loaded) =>
        new(key, key, string.Empty, HudIcon.None, loaded, 30, 90, null, HudGauge.Clip, HudWeaponState.Ready);

    private sealed class NoIcons : IHudIconSource
    {
        public static NoIcons Instance { get; } = new();

        public bool TryMeasure(HudIcon icon, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            return false;
        }

        public void Draw(SpriteBatch batch, HudIcon icon, float x, float y, float scale, Bgra32 colour)
        {
        }
    }
}
