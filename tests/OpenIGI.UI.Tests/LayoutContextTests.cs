using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>
/// Covers the reference-resolution mapping — the piece that has to be right from 4:3 to 32:9, where an
/// aspect-ratio bug is invisible at the 16:9 everyone develops against.
/// </summary>
public sealed class LayoutContextTests
{
    [Fact]
    public void Scale_at_the_reference_resolution_is_one()
    {
        var context = new LayoutContext(1920f, 1080f);
        Assert.Equal(1f, context.Scale, 4);
        Assert.Equal(1920f, context.DesignRect.Width, 3);
        Assert.Equal(1080f, context.DesignRect.Height, 3);
    }

    [Fact]
    public void Height_priority_scale_follows_the_display_height()
    {
        // Match 1 (the default) ties the scale to height, so a 1440p display is exactly 4/3 larger.
        var context = new LayoutContext(2560f, 1440f);
        Assert.Equal(1440f / 1080f, context.Scale, 4);
    }

    [Theory]
    [InlineData(1440f, 1080f)]  // 4:3
    [InlineData(1920f, 1080f)]  // 16:9
    [InlineData(2560f, 1080f)]  // 21:9
    [InlineData(3840f, 1080f)]  // 32:9
    public void Design_box_is_centred_and_never_larger_than_the_viewport(float width, float height)
    {
        var context = new LayoutContext(width, height);

        // The centred reference box is symmetric within the safe rect at every aspect ratio.
        float leftMargin = context.DesignRect.X - context.SafeRect.X;
        float rightMargin = context.SafeRect.Right - context.DesignRect.Right;
        Assert.Equal(leftMargin, rightMargin, 2);

        float topMargin = context.DesignRect.Y - context.SafeRect.Y;
        float bottomMargin = context.SafeRect.Bottom - context.DesignRect.Bottom;
        Assert.Equal(topMargin, bottomMargin, 2);
    }

    [Fact]
    public void Ultrawide_leaves_horizontal_margin_but_fills_height()
    {
        var context = new LayoutContext(3840f, 1080f);

        // Height-matched: the design box is as tall as the viewport and narrower than it, so a centred
        // menu fills top-to-bottom while the sides are free for a full-bleed background.
        Assert.Equal(1080f, context.DesignRect.Height, 2);
        Assert.True(context.DesignRect.Width < context.Viewport.X);
        Assert.True(context.DesignRect.X > 0f);
    }

    [Fact]
    public void Safe_area_insets_the_usable_rectangle()
    {
        var context = new LayoutContext(1920f, 1080f, safeArea: new Thickness(40f, 30f, 40f, 30f));
        Assert.Equal(40f, context.SafeRect.X, 3);
        Assert.Equal(30f, context.SafeRect.Y, 3);
        Assert.Equal(1920f - 80f, context.SafeRect.Width, 3);
        Assert.Equal(1080f - 60f, context.SafeRect.Height, 3);
    }

    [Fact]
    public void User_scale_multiplies_the_reference_scale()
    {
        var plain = new LayoutContext(1920f, 1080f);
        var enlarged = new LayoutContext(1920f, 1080f, userScale: 1.25f);
        Assert.Equal(plain.Scale * 1.25f, enlarged.Scale, 4);
    }

    [Fact]
    public void Match_zero_follows_width_instead_of_height()
    {
        var context = new LayoutContext(3840f, 1080f, match: 0f);
        Assert.Equal(3840f / 1920f, context.Scale, 4);
    }
}
