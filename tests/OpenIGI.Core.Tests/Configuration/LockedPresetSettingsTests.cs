using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>What a build locked to one graphics preset writes to its settings file, and reads back.</summary>
public sealed class LockedPresetSettingsTests
{
    /// <summary>
    /// The browser build: the preset is applied at every start, and the file holds what the player changed from it -- a
    /// change back to a variable's own default included, which the file used to leave out and the next start's lock undid.
    /// </summary>
    [Fact]
    public void A_change_from_the_locked_preset_is_written_even_when_it_is_the_variables_default_and_survives_the_next_start()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openigi-locked-{Guid.NewGuid():N}.cfg");
        try
        {
            (ConsoleSystem console, GraphicsPresetRegistry presets, BoolCVar stays) = Start();
            presets.Lock(GraphicsPreset.Vanilla);
            Assert.False(stays.Value);

            // Untouched, the locked value is not the player's and is not written.
            Assert.DoesNotContain("t_bodies_stay", SettingsFile.Render(console, presets), StringComparison.Ordinal);

            stays.Value = true;
            Assert.Contains("t_bodies_stay", SettingsFile.Render(console, presets), StringComparison.Ordinal);
            Assert.True(SettingsFile.Save(console, path, presets));

            // The next visit: the lock, then the file.
            (ConsoleSystem again, GraphicsPresetRegistry lockedAgain, BoolCVar restored) = Start();
            lockedAgain.Lock(GraphicsPreset.Vanilla);
            Assert.True(SettingsFile.Load(again, path));
            Assert.True(restored.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Nothing locked: a variable at its default is not written, as before.</summary>
    [Fact]
    public void Without_a_lock_only_what_differs_from_the_default_is_written()
    {
        (ConsoleSystem console, GraphicsPresetRegistry presets, BoolCVar stays) = Start();
        Assert.DoesNotContain("t_bodies_stay", SettingsFile.Render(console, presets), StringComparison.Ordinal);

        stays.Value = false;
        Assert.Contains("t_bodies_stay", SettingsFile.Render(console, presets), StringComparison.Ordinal);
    }

    private static (ConsoleSystem Console, GraphicsPresetRegistry Presets, BoolCVar Stays) Start()
    {
        CVarRegistry variables = new();
        GraphicsPresetRegistry presets = new();
        BoolCVar stays = presets.Define(
            CVar.Bool("t_bodies_stay", true, CVarOptions.Archive, registry: variables), "false", "true", "true", "true");
        return (new ConsoleSystem(variables), presets, stays);
    }
}
