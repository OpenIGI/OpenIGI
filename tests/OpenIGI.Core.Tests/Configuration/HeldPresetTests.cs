using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>A game that offers one graphics preset for now (<see cref="GraphicsPresetRegistry.Hold"/>): I.G.I. 2's Vanilla.</summary>
public sealed class HeldPresetTests
{
    /// <summary>
    /// Ultra in Project I.G.I., then I.G.I. 2 from the main menu: its picture is Vanilla, the interface as the player had it;
    /// back in Project I.G.I. the player's Ultra is back, and reads as Ultra.
    /// </summary>
    [Fact]
    public void The_players_preset_comes_back_with_a_game_that_offers_it()
    {
        Setup s = Start();
        s.Presets.Apply(GraphicsPreset.Ultra);

        s.Presets.Hold(GraphicsPreset.Vanilla);
        Assert.Equal("0", s.Quality.ValueText);
        Assert.True(s.Hud.Value);
        Assert.Equal(GraphicsPreset.Vanilla, s.Presets.Look);
        Assert.False(s.Presets.IsAllowed(GraphicsPreset.Ultra));
        Assert.True(s.Presets.IsAllowed(GraphicsPreset.Vanilla));

        // Holding again changes nothing: the record is still the player's Ultra.
        s.Presets.Hold(GraphicsPreset.Vanilla);
        s.Presets.Hold(null);
        Assert.Equal("3", s.Quality.ValueText);
        Assert.Equal(GraphicsPreset.Ultra, s.Presets.Current);
        Assert.True(s.Presets.IsAllowed(GraphicsPreset.Ultra));
    }

    /// <summary>A change to the interface while held is the player's and stays when the hold goes.</summary>
    [Fact]
    public void The_interface_is_the_players_through_a_hold()
    {
        Setup s = Start();
        s.Presets.Apply(GraphicsPreset.Ultra);
        s.Presets.Hold(GraphicsPreset.Vanilla);
        s.Hud.Value = false;

        s.Presets.Hold(null);
        Assert.False(s.Hud.Value);
        Assert.Equal("3", s.Quality.ValueText);
    }

    /// <summary>Quit while held: the file holds the player's Ultra, not the game's Vanilla, and the next start reads it back.</summary>
    [Fact]
    public void A_game_quit_while_held_saves_the_players_picture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openigi-held-{Guid.NewGuid():N}.cfg");
        try
        {
            Setup s = Start();
            s.Presets.Apply(GraphicsPreset.Ultra);
            s.Presets.Hold(GraphicsPreset.Vanilla);

            string line = Assert.Single(SettingsFile.Render(s.Console, s.Presets).Split(Environment.NewLine), static l => l.StartsWith("t_quality", StringComparison.Ordinal));
            Assert.Contains('3', line);
            Assert.True(SettingsFile.Save(s.Console, path, s.Presets));

            Setup again = Start();
            Assert.True(SettingsFile.Load(again.Console, path));
            Assert.Equal("3", again.Quality.ValueText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A player on the defaults saves nothing of the picture under a hold: the held value is not theirs.</summary>
    [Fact]
    public void A_player_on_the_defaults_writes_none_of_the_held_picture()
    {
        Setup s = Start();
        s.Presets.Hold(GraphicsPreset.Ultra);
        Assert.Equal("3", s.Quality.ValueText);
        Assert.DoesNotContain("t_quality", SettingsFile.Render(s.Console, s.Presets), StringComparison.Ordinal);
    }

    /// <summary>The browser's build-wide lock is already one picture: a game's hold does nothing under it.</summary>
    [Fact]
    public void A_locked_build_is_not_held_again()
    {
        Setup s = Start();
        s.Presets.Lock(GraphicsPreset.Vanilla);
        s.Presets.Hold(GraphicsPreset.Vanilla);
        Assert.Null(s.Presets.HeldPreset);
    }

    private static Setup Start()
    {
        CVarRegistry variables = new();
        GraphicsPresetRegistry presets = new();
        NumericCVar<int> quality = presets.Define(
            CVar.Int("t_quality", 1, 0, 3, CVarOptions.Archive, registry: variables), "0", "1", "2", "3");
        BoolCVar hud = presets.DefineInterface(
            CVar.Bool("t_hud", true, CVarOptions.Archive, registry: variables), "false", "true", "true", "true");
        presets.Detect();
        return new Setup(new ConsoleSystem(variables), presets, quality, hud);
    }

    private sealed record Setup(ConsoleSystem Console, GraphicsPresetRegistry Presets, NumericCVar<int> Quality, BoolCVar Hud);
}
