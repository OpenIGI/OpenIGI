using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// <see cref="GraphicsPresetRegistry.Look"/>: the preset the picture is, which the interface's variables do not change -- a player
/// on Vanilla who turned the remastered heads-up display on kept the original's forest, sky and texture storage.
/// </summary>
public sealed class GraphicsPresetLookTests
{
    [Fact]
    public void An_interface_variable_leaves_the_preset_but_not_the_look()
    {
        (GraphicsPresetRegistry presets, BoolCVar picture, BoolCVar hud) = Start();
        presets.Apply(GraphicsPreset.Vanilla);

        hud.Value = true;

        Assert.Equal(GraphicsPreset.Custom, presets.Current);
        Assert.Equal(GraphicsPreset.Vanilla, presets.Look);
        Assert.True(presets.IsVanillaLook);
        Assert.False(picture.Value);
    }

    [Fact]
    public void A_picture_variable_leaves_the_look()
    {
        (GraphicsPresetRegistry presets, BoolCVar picture, _) = Start();
        presets.Apply(GraphicsPreset.Vanilla);

        picture.Value = true;

        // The one picture variable at the other presets' value is their picture.
        Assert.Equal(GraphicsPreset.Enhanced, presets.Look);
        Assert.False(presets.IsVanillaLook);
    }

    [Fact]
    public void A_locked_build_is_its_picture_whatever_is_changed()
    {
        (GraphicsPresetRegistry presets, BoolCVar picture, BoolCVar hud) = Start();
        presets.Lock(GraphicsPreset.Vanilla);

        hud.Value = true;
        picture.Value = true;

        Assert.Equal(GraphicsPreset.Vanilla, presets.Look);
    }

    private static (GraphicsPresetRegistry Presets, BoolCVar Picture, BoolCVar Hud) Start()
    {
        CVarRegistry variables = new();
        GraphicsPresetRegistry presets = new();
        BoolCVar picture = presets.Define(
            CVar.Bool("t_picture", false, CVarOptions.Archive, registry: variables), "false", "true", "true", "true");
        BoolCVar hud = presets.DefineInterface(
            CVar.Bool("t_hud", false, CVarOptions.Archive, registry: variables), "false", "true", "true", "true");
        return (presets, picture, hud);
    }
}
