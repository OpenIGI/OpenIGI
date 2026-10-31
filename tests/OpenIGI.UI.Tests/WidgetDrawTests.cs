using OpenIGI.Input;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using OpenIGI.UI;
using OpenIGI.UI.Focus;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.UI.Tests;

/// <summary>
/// Asserts draw output through <see cref="RecordingGraphicsDevice"/> — no GL — and covers the input
/// reader that turns a controller into navigation intents.
/// </summary>
public sealed class WidgetDrawTests
{
    private static (RecordingGraphicsDevice Device, UiTheme Theme) Setup()
    {
        var device = new RecordingGraphicsDevice();
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf");
        UiFont font = UiFont.Upload(device, FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f));
        return (device, new UiTheme(font));
    }

    private static void Render(RecordingGraphicsDevice device, UiElement root)
    {
        var context = new LayoutContext(1920f, 1080f);
        root.Measure(context, context.SafeRect.Size);
        root.Arrange(context.SafeRect);

        var batch = new SpriteBatch(device);
        var painter = UiPainter.Create(batch, context);
        batch.Begin();
        root.Draw(painter);
        batch.End();
    }

    [Fact]
    public void A_label_emits_one_quad_per_visible_glyph()
    {
        (RecordingGraphicsDevice device, UiTheme theme) = Setup();
        var label = new Label(theme.Body, "AB", 32f, theme.Text);
        Render(device, label);

        int quads = 0;
        foreach (RecordingGraphicsDevice.DrawCall call in device.Draws)
        {
            quads += call.QuadCount;
        }

        // "AB" is two inked glyphs.
        Assert.Equal(2, quads);
    }

    [Fact]
    public void A_focused_button_draws_more_than_an_unfocused_one()
    {
        (RecordingGraphicsDevice unfocusedDevice, UiTheme theme1) = Setup();
        var unfocused = new Button(theme1, "Play");
        Render(unfocusedDevice, unfocused);
        int unfocusedQuads = TotalQuads(unfocusedDevice);

        (RecordingGraphicsDevice focusedDevice, UiTheme theme2) = Setup();
        var focused = new Button(theme2, "Play");
        // Lay out first so it has bounds, then focus and settle the animation before drawing.
        var context = new LayoutContext(1920f, 1080f);
        focused.Measure(context, context.SafeRect.Size);
        focused.Arrange(context.SafeRect);
        focused.SetFocused(true);
        for (int i = 0; i < 60; i++)
        {
            focused.Update(1f / 60f);
        }

        var batch = new SpriteBatch(focusedDevice);
        var painter = UiPainter.Create(batch, context);
        batch.Begin();
        focused.Draw(painter);
        batch.End();

        Assert.True(TotalQuads(focusedDevice) > unfocusedQuads,
            "the focus fill and accent bar should add quads");
    }

    [Fact]
    public void A_panel_with_a_background_fills_a_rectangle()
    {
        (RecordingGraphicsDevice device, _) = Setup();
        var panel = new Panel { Background = OpenIGI.Core.Graphics.Bgra32.FromRgba(10, 13, 16, 210), Width = 400f, Height = 200f };
        Render(device, panel);
        Assert.True(TotalQuads(device) >= 1);
    }

    [Fact]
    public void Sdf_text_falls_back_to_a_textured_draw_on_a_device_without_the_capability()
    {
        var device = new RecordingGraphicsDevice(); // not an ISdfTextDevice
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf");
        UiFont sdf = UiFont.Upload(device, FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f, FontAtlasKind.SignedDistanceField));

        var context = new LayoutContext(1920f, 1080f);
        var batch = new SpriteBatch(device);
        var painter = UiPainter.Create(batch, context);
        batch.Begin();
        painter.DrawText(
            sdf,
            "Hi",
            new UiVec2(20f, 20f),
            48f,
            OpenIGI.Core.Graphics.Bgra32.White,
            SdfTextParams.Plain(4f).WithOutline(OpenIGI.Core.Graphics.Bgra32.Black, 0.2f));
        batch.End();

        // The recording device cannot shade a distance field, so the glyphs are still drawn — as ordinary
        // textured quads sampling the atlas — rather than dropped.
        Assert.NotEmpty(device.Draws);
        Assert.Equal(sdf.Texture.ToTextureHandle(), device.Draws[0].Texture);
    }

    private static int TotalQuads(RecordingGraphicsDevice device)
    {
        int quads = 0;
        foreach (RecordingGraphicsDevice.DrawCall call in device.Draws)
        {
            quads += call.QuadCount;
        }

        return quads;
    }

    // ---- Navigation reader ----

    [Fact]
    public void Held_direction_fires_once_then_repeats_after_the_initial_delay()
    {
        var input = new InputState();
        var nav = new UiNavigation();

        input.BeginFrame();
        input.Gamepad.SetButton(GamepadButton.DpadDown, true);
        nav.Update(input, 0f);
        Assert.Equal(NavDirection.Down, nav.Direction); // fires immediately on press

        input.BeginFrame();
        nav.Update(input, 0.05f);
        Assert.Null(nav.Direction); // still within the initial delay

        // Cross the initial delay.
        input.BeginFrame();
        nav.Update(input, 0.5f);
        Assert.Equal(NavDirection.Down, nav.Direction);

        // Releasing stops it.
        input.BeginFrame();
        input.Gamepad.SetButton(GamepadButton.DpadDown, false);
        nav.Update(input, 0.5f);
        Assert.Null(nav.Direction);
    }

    [Fact]
    public void The_left_stick_past_the_dead_zone_reads_as_a_direction()
    {
        var input = new InputState();
        var nav = new UiNavigation();
        input.BeginFrame();
        input.Gamepad.SetLeftStick(0.9f, 0f);
        nav.Update(input, 0f);
        Assert.Equal(NavDirection.Right, nav.Direction);
    }

    [Fact]
    public void Accept_and_back_map_to_the_face_buttons()
    {
        var input = new InputState();
        var nav = new UiNavigation();

        input.BeginFrame();
        input.Gamepad.SetButton(GamepadButton.South, true);
        nav.Update(input, 0f);
        Assert.True(nav.Accept);
        Assert.False(nav.Back);

        input.BeginFrame();
        input.Gamepad.SetButton(GamepadButton.South, false);
        input.Gamepad.SetButton(GamepadButton.East, true);
        nav.Update(input, 0f);
        Assert.True(nav.Back);
    }
}
