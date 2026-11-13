using OpenIGI.Game.Config;
using Xunit;

namespace OpenIGI.Game.Tests.Config;

/// <summary>The move of a never-rebound profile onto the controls a hand on WASD and one on the mouse can reach.</summary>
public sealed class ControlsMigrationTests
{
    private static string Key(PlayerProfile profile, string action) =>
        profile.Bindings.First(b => string.Equals(b.Action, action, StringComparison.OrdinalIgnoreCase)).Key;

    /// <summary>A profile as a fresh one was before the move: today's defaults with the six old keys.</summary>
    private static PlayerProfile Previous()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        foreach (InputBinding old in PlayerProfile.PreviousDefaultBindings)
        {
            int index = profile.Bindings.FindIndex(b => string.Equals(b.Action, old.Action, StringComparison.OrdinalIgnoreCase));
            profile.Bindings[index] = old;
        }

        return profile;
    }

    [Fact]
    public void AProfileNeverReboundTakesTheSightOnTheRightButtonAndLeaningOnAKeyALaptopHas()
    {
        GameConfig config = new() { ControlsVersion = 1 };
        config.Add(Previous());

        Assert.True(config.MigrateControls());

        PlayerProfile profile = config.Profiles[0];
        Assert.Equal("MOUSE_BUTTON_2", Key(profile, "AlternateFire"));
        Assert.Equal("MOUSE_WHEEL_DOWN", Key(profile, "NextWeapon"));
        Assert.Equal("MOUSE_WHEEL_UP", Key(profile, "PrevWeapon"));
        Assert.Equal("KEY_LEFT_ALT", Key(profile, "Peek"));
        Assert.Equal(GameConfig.CurrentControlsVersion, config.ControlsVersion);
        Assert.False(config.MigrateControls());
    }

    [Fact]
    public void AProfileWithAnyOriginalActionReboundKeepsEveryKey()
    {
        GameConfig config = new() { ControlsVersion = 1 };
        PlayerProfile profile = Previous();
        int jump = profile.Bindings.FindIndex(b => b.Action == "Jump");
        profile.Bindings[jump] = new("Jump", InputBindingKeyNames.KeyboardDevice, "KEY_J");
        config.Add(profile);

        Assert.False(config.MigrateControls());
        Assert.Equal("KEY_BACKSPACE", Key(profile, "AlternateFire"));
        Assert.Equal("KEY_NUMPAD1", Key(profile, "Peek"));
    }

    [Fact]
    public void NoControlDrivesTwoActionsInAFreshProfile()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (InputBinding binding in profile.Bindings.Where(b => b.Key != PlayerProfile.UnboundKey))
        {
            Assert.True(seen.Add($"{binding.Device}/{binding.Key}"), $"{binding.Key} is bound twice.");
        }
    }
}
