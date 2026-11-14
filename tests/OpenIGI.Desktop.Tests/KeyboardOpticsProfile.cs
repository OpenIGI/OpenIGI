using OpenIGI.Game.Config;

namespace OpenIGI.Desktop.Tests;

/// <summary>A profile whose sight and zoom are on keyboard keys, for tests that press them one key at a time.</summary>
/// <remarks>
/// A fresh profile has the sight on the right mouse button. These tests are of what the sight and the zoom do once
/// pressed, not of which control presses them, so they bind keys of their own rather than follow the defaults.
/// </remarks>
internal static class KeyboardOpticsProfile
{
    /// <summary>The default profile with alternate fire on Backspace and the zoom on Page Up and Page Down.</summary>
    public static PlayerProfile Create()
    {
        PlayerProfile profile = PlayerProfile.CreateDefault();
        Set(profile, "AlternateFire", "KEY_BACKSPACE");
        Set(profile, "ZoomIn", "KEY_PAGEUP");
        Set(profile, "ZoomOut", "KEY_PAGEDOWN");
        return profile;
    }

    private static void Set(PlayerProfile profile, string action, string key)
    {
        int index = profile.Bindings.FindIndex(binding => string.Equals(binding.Action, action, StringComparison.OrdinalIgnoreCase));
        profile.Bindings[index] = new InputBinding(action, InputBindingKeyNames.KeyboardDevice, key);
    }
}
