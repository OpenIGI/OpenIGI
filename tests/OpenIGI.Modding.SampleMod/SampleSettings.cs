using OpenIGI.Core.Configuration;

namespace SampleMod;

/// <summary>The sample mod's settings: named mod_sample_..., so the settings screen lists them under Mods.</summary>
[CVarContainer]
public static class SampleSettings
{
    /// <summary>Whether the greyscale pass runs.</summary>
    public static readonly BoolCVar Greyscale = CVar.Bool(
        "mod_sample_greyscale",
        false,
        CVarOptions.Archive,
        "Drains the scene of its colour.");

    /// <summary>How much of the colour the greyscale pass drains, from none to all.</summary>
    public static readonly NumericCVar<float> GreyscaleAmount = CVar.Float(
        "mod_sample_greyscale_amount",
        1f,
        0f,
        1f,
        CVarOptions.Archive,
        "How much of the scene's colour the greyscale pass drains.");
}
