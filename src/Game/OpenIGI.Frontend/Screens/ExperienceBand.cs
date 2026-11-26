using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// The experience, across the top of the Gameplay and Interface pages: the original game or the remaster's, in one press,
/// over every setting <see cref="ExperiencePresetRegistry"/> manages on both pages.
/// </summary>
/// <remarks>
/// Live, as the rows under it are: choosing writes the values at once. The same band heads both pages, and each reads the
/// variables themselves, so a choice on one shows on the other.
/// </remarks>
internal static class ExperienceBand
{
    // The order shown: the original, the remaster, and the reading that is neither.
    private static readonly ExperiencePreset[] Presets = [ExperiencePreset.Vanilla, ExperiencePreset.Enhanced, ExperiencePreset.Custom];

    /// <summary>Builds the band.</summary>
    /// <param name="rowBuilder">What the page's rows are built with.</param>
    /// <param name="theme">The theme.</param>
    /// <returns>The band's group.</returns>
    public static SettingsGroup Build(SettingsRowBuilder rowBuilder, UiTheme theme)
    {
        ExperiencePresetRegistry registry = ExperiencePresetRegistry.Default;
        return new PresetBand
        {
            Title = "Experience",
            Line = "Original plays and looks as the 2000 release did; Enhanced turns on the remaster's additions. Changing a "
                + "setting below makes it Custom.",
            Help = "Sets the HUD, the crosshair, the markers and optics, and the gameplay additions -- saves, ragdolls, bodies "
                + "and dropped weapons -- to the original game's or to the remaster's, on this page and the other at once. "
                + "Custom is what they read as once you change one yourself: a reading, not a choice.",
            Captions = [.. Presets.Select(static preset => preset == ExperiencePreset.Vanilla ? "Original" : preset.ToString())],
            Choosable = index => Presets[index] != ExperiencePreset.Custom,
            Reading = () => Array.IndexOf(Presets, registry.Detect()),
            Choose = index =>
            {
                ExperiencePreset preset = Presets[index];
                registry.Apply(preset);

                // What the game was last asked for, so the console reads it back; written after, as it applies again.
                if (rowBuilder.Context.Cvars.TryGet("g_preset", out ICVar? asked) && asked is not null)
                {
                    asked.TrySetText(preset.ToString().ToLowerInvariant(), out _);
                }
            },
            Watch = changed =>
            {
                EventHandler<CVarChangedEventArgs> handler = (_, _) => changed();
                ICVar[] watched = [.. registry.Managed];
                foreach (ICVar variable in watched)
                {
                    variable.Changed += handler;
                }

                return new Unsubscribe(() =>
                {
                    foreach (ICVar variable in watched)
                    {
                        variable.Changed -= handler;
                    }
                });
            },
        }.Build(theme, rowBuilder.Bindings);
    }
}
