using OpenIGI.Core.Configuration;

namespace MyMod;

/// <summary>The mod's settings: named mod_..., so the settings screen lists them on its Mods page and keeps them while it is off.</summary>
[CVarContainer]
public static class Settings
{
    /// <summary>Whether the vignette runs: the pass's Toggle in the project.</summary>
    public static readonly BoolCVar Vignette = CVar.Bool(
        "mod_MOD_SETTING_vignette",
        true,
        CVarOptions.Archive,
        "Darkens the picture toward its edges.");

    /// <summary>How dark the corners go, from not at all to black: the pass's first value (p.values[0].x).</summary>
    public static readonly NumericCVar<float> VignetteAmount = CVar.Float(
        "mod_MOD_SETTING_vignette_amount",
        0.5f,
        0f,
        1f,
        CVarOptions.Archive,
        "How dark the vignette makes the corners.");
}
