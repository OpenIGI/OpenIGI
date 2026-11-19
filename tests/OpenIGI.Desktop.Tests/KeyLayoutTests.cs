using OpenIGI.Game.Config;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.DevTools;
using OpenIGI.Input;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>Out of the box, no key does two things in either game: the shared actions, a title's own, and the host's keys together.</summary>
public sealed class KeyLayoutTests
{
    // The shared actions I.G.I. 2 does not read, or reads only through a row of its own.
    private static readonly string[] NotReadByIgi2 =
        ["Crouch", "ToggleWalkRun", "Flashlight", "WeaponCategory8", "Underbarrel", "LeanLeft", "LeanRight", "Mount"];

    [Fact]
    public void NoKeyDoesTwoThingsInProjectIgi()
    {
        AssertUnique(SharedRows(static _ => true).Concat(HostKeys()));
    }

    [Fact]
    public void NoKeyDoesTwoThingsInIgi2()
    {
        IEnumerable<(KeyCode, string)> title = new Igi2Gameplay().InputBindings
            .Select(static binding => binding.Default)
            .Where(static binding => InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out _))
            .Select(static binding => (Key(binding.Key), binding.Action));
        AssertUnique(SharedRows(static action => !NotReadByIgi2.Contains(action)).Concat(title).Concat(HostKeys()));
    }

    [Fact]
    public void ASettingsFileOnTheOldKeysIsMovedOnceAndAKeyItsPlayerChoseIsKept()
    {
        Core.Configuration.ConsoleSystem console = new(new Core.Configuration.CVarRegistry());
        KeyLayoutCVars.Revision.Value = 0;
        CoopVoiceCVars.Key.Value = "v";
        console.SetBind("v", "toggle g_thirdperson");
        console.SetBind("l", "lamp_switch");
        console.SetBind("k", "say hello");
        try
        {
            DefaultBinds.Apply(console);

            Assert.Equal("p", DefaultBinds.KeyOf(console.Binds, "toggle g_thirdperson"));
            Assert.Equal("semicolon", DefaultBinds.KeyOf(console.Binds, "lamp_switch"));
            Assert.Equal("say hello", console.Binds["k"]);
            Assert.Equal("k", CoopVoiceCVars.Key.Value);
            Assert.Equal(DefaultBinds.CurrentRevision, KeyLayoutCVars.Revision.Value);
        }
        finally
        {
            KeyLayoutCVars.Revision.Value = DefaultBinds.CurrentRevision;
            CoopVoiceCVars.Key.Value = "k";
        }
    }

    private static IEnumerable<(KeyCode Key, string Owner)> SharedRows(Func<string, bool> read) =>
        PlayerProfile.DefaultBindings
            .Where(binding => read(binding.Action) && InputBindingKeyNames.TryParseKeyboardKey(binding.Key, out _))
            .Select(static binding => (Key(binding.Key), binding.Action));

    // The host's own keys: the default binds, the keys its settings name, quick save and load.
    private static IEnumerable<(KeyCode Key, string Owner)> HostKeys()
    {
        foreach ((string key, string statement) in DefaultBinds.All)
        {
            Assert.True(BindKeyNames.TryParse(key, out KeyCode code), $"'{key}' is no key.");
            yield return (code, statement);
        }

        foreach ((string name, string owner) in ((string, string)[])[
            (CoopVoiceCVars.Key.DefaultValue, "push to talk"),
            (ControlsHelpCVars.Key.DefaultValue, "controls help"),
            (TeamMenuCVars.Key.DefaultValue, "team menu")])
        {
            Assert.True(BindKeyNames.TryParse(name, out KeyCode code), $"'{name}' is no key.");
            yield return (code, owner);
        }

        yield return (KeyCode.F5, "quick save");
        yield return (KeyCode.F9, "quick load");
    }

    private static KeyCode Key(string stored)
    {
        Assert.True(InputBindingKeyNames.TryParseKeyboardKey(stored, out KeyCode key));
        return key;
    }

    private static void AssertUnique(IEnumerable<(KeyCode Key, string Owner)> keys)
    {
        string[] clashes = [.. keys.GroupBy(static k => k.Key).Where(static g => g.Count() > 1)
            .Select(static g => $"{g.Key}: {string.Join(" / ", g.Select(static k => k.Owner))}")];
        Assert.True(clashes.Length == 0, "Keys doing two things: " + string.Join("; ", clashes));
    }
}
