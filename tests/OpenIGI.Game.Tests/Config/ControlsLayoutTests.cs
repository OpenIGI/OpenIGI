using OpenIGI.Game.Config;
using Xunit;

namespace OpenIGI.Game.Tests.Config;

/// <summary>Today's default controls, and the move onto them from the layouts before.</summary>
public sealed class ControlsLayoutTests
{
    private const string Keyboard = "INPUTPORT_DEVICE_KEYBOARD";
    private const string Mouse = "INPUTPORT_DEVICE_MOUSE";

    [Fact]
    public void NoTwoSharedActionsShareAControlAndEveryActionHasOne()
    {
        IReadOnlyList<InputBinding> defaults = PlayerProfile.DefaultBindings;
        Assert.Equal(PlayerProfile.BindingCount, defaults.Count);
        Assert.All(defaults, binding => Assert.NotEqual(PlayerProfile.UnboundKey, binding.Key));
        var controls = defaults.GroupBy(binding => (binding.Device, binding.Key)).Where(group => group.Count() > 1).ToList();
        Assert.True(controls.Count == 0, $"Shared controls: {string.Join(", ", controls.Select(g => $"{g.Key.Key}={string.Join('/', g.Select(b => b.Action))}"))}");
    }

    [Fact]
    public void TheLayoutIsTheOneShootersWithALeanSettledOn()
    {
        Assert.Equal("MOUSE_BUTTON_2", Key("AlternateFire"));
        Assert.Equal("KEY_R", Key("Reload"));
        Assert.Equal("KEY_F", Key("Activate"));
        Assert.Equal("KEY_Q", Key("LeanLeft"));
        Assert.Equal("KEY_E", Key("LeanRight"));
        Assert.Equal("KEY_LEFT_CTRL", Key("Crouch"));
        Assert.Equal("KEY_L", Key("Flashlight"));
        Assert.Equal("KEY_LEFT_ALT", Key("Peek"));
        Assert.Equal("MOUSE_WHEEL_DOWN", Key("NextWeapon"));
    }

    [Fact]
    public void AProfileHalfOnTheOriginalsMouseAndHalfRearrangedComesOverActionByAction()
    {
        // A real profile from before: its player moved reload, use and crouch by hand, and never touched the original's mouse
        // and peek keys; modern aim's leans arrived unbound, their keys being taken.
        PlayerProfile profile = new();
        profile.Bindings.AddRange(
        [
            new("MoveLeft", Keyboard, "KEY_A"), new("MoveRight", Keyboard, "KEY_D"), new("MoveUp", Keyboard, "KEY_W"),
            new("MoveDown", Keyboard, "KEY_S"), new("Fire", Mouse, "MOUSE_BUTTON_1"), new("Reload", Keyboard, "KEY_R"),
            new("Activate", Keyboard, "KEY_E"), new("Crouch", Keyboard, "KEY_LEFT_CTRL"), new("Jump", Keyboard, "KEY_SPACE"),
            new("NextWeapon", Mouse, "MOUSE_BUTTON_2"), new("PrevWeapon", Keyboard, "KEY_O"),
            new("AlternateFire", Keyboard, "KEY_BACKSPACE"), new("Peek", Keyboard, "KEY_NUMPAD1"), new("Binoculars", Keyboard, "KEY_B"),
            new("MapComputer", Keyboard, "KEY_C"), new("ToggleWalkRun", Keyboard, "KEY_LEFT_SHIFT"),
            new("ZoomIn", Keyboard, "KEY_PAGEUP"), new("ZoomOut", Keyboard, "KEY_PAGEDOWN"), new("Flashlight", Keyboard, "KEY_F"),
            new("WeaponWheel", Mouse, "MOUSE_BUTTON_3"), new("Title_igi2_RaiseStance", Keyboard, "KEY_Q"),
            new("Title_igi2_Thermal", Keyboard, "KEY_T"), new("Title_igi2_LowerStance", Keyboard, "KEY_Z"),
            new("Underbarrel", Keyboard, "KEY_V"), new("LeanLeft", Keyboard, PlayerProfile.UnboundKey),
            new("LeanRight", Keyboard, PlayerProfile.UnboundKey), new("Mount", Keyboard, PlayerProfile.UnboundKey),
        ]);
        GameConfig config = new() { ControlsVersion = 2 };
        config.Add(profile);

        Assert.True(config.MigrateControls());

        Assert.Equal("MOUSE_BUTTON_2", Key(profile, "AlternateFire"));
        Assert.Equal("MOUSE_WHEEL_DOWN", Key(profile, "NextWeapon"));
        Assert.Equal("MOUSE_WHEEL_UP", Key(profile, "PrevWeapon"));
        Assert.Equal("KEY_LEFT_ALT", Key(profile, "Peek"));
        Assert.Equal("KEY_EQUALS", Key(profile, "ZoomIn"));
        Assert.Equal("KEY_F", Key(profile, "Activate"));
        Assert.Equal("KEY_L", Key(profile, "Flashlight"));
        Assert.Equal("KEY_Q", Key(profile, "LeanLeft"));
        Assert.Equal("KEY_E", Key(profile, "LeanRight"));
        Assert.Equal("KEY_Z", Key(profile, "Mount"));
        Assert.Equal("KEY_R", Key(profile, "Reload"));

        // I.G.I. 2's own rows still on the keys the layout before gave them come over too: the goggles off the co-op mark's T,
        // the stance steps off the leans' Q and prone's Z.
        Assert.Equal("KEY_N", Key(profile, "Title_igi2_Thermal"));
        Assert.Equal("KEY_INSERT", Key(profile, "Title_igi2_RaiseStance"));
        Assert.Equal("KEY_DELETE", Key(profile, "Title_igi2_LowerStance"));
        Assert.Equal(3, config.ControlsVersion);
        Assert.False(config.MigrateControls());
    }

    [Fact]
    public void AnActionThePlayerPutOnAKeyOfTheirOwnStaysAndAMoveOntoItsKeyIsDropped()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        Set(profile, "Reload", "KEY_RETURN");
        Set(profile, "Activate", "KEY_E");
        Set(profile, "Flashlight", "KEY_F");
        Set(profile, "LeanRight", "KEY_R");
        Set(profile, "Binoculars", "KEY_L");

        Assert.False(profile.AdoptCurrentLayout());

        // The binoculars were the player's own choice of L, so the flashlight cannot have it and stays on F -- and use, which
        // would have moved onto F, stays on E, and the right lean with it on R, and reload on Return.
        Assert.Equal("KEY_L", Key(profile, "Binoculars"));
        Assert.Equal("KEY_F", Key(profile, "Flashlight"));
        Assert.Equal("KEY_E", Key(profile, "Activate"));
        Assert.Equal("KEY_R", Key(profile, "LeanRight"));
        Assert.Equal("KEY_RETURN", Key(profile, "Reload"));
        Assert.DoesNotContain(
            profile.Bindings.Where(b => !PlayerProfile.IsTitleRow(b) && b.Key != PlayerProfile.UnboundKey).GroupBy(b => (b.Device, b.Key)),
            group => group.Count() > 1);
    }

    [Fact]
    public void APlayerWhoWalksOnTheArrowsKeepsEveryKey()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        Set(profile, "MoveUp", "KEY_UP");
        Set(profile, "Reload", "KEY_RETURN");

        Assert.False(profile.AdoptCurrentLayout());
        Assert.Equal("KEY_RETURN", Key(profile, "Reload"));
    }

    private static string Key(string action) =>
        PlayerProfile.DefaultBindings.Single(binding => binding.Action == action).Key;

    private static string Key(PlayerProfile profile, string action) =>
        profile.Bindings.Single(binding => binding.Action == action).Key;

    private static void Set(PlayerProfile profile, string action, string key)
    {
        int index = profile.Bindings.FindIndex(binding => binding.Action == action);
        profile.Bindings[index] = profile.Bindings[index] with { Key = key };
    }
}
