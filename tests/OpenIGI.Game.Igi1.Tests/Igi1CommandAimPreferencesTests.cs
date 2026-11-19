using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Player.Aim;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>A player's modern aim settings, as a command carries them to the server.</summary>
public sealed class Igi1CommandAimPreferencesTests
{
    [Fact]
    public void APlayersSettingsComeBackAsTheyWentUnderTheServersSwitch()
    {
        float[] channels = new float[Igi1Command.ChannelCount];
        ModernAimOptions mine = new(Enabled: true, Toggle: true, Sensitivity: 0.75f, Zoom: 0.4f, AutoMount: false);
        Igi1Command.PackAimPreferences(channels, mine);

        Assert.Equal(mine, Igi1Command.UnpackAimPreferences(channels, enabled: true));
        Assert.False(Igi1Command.UnpackAimPreferences(channels, enabled: false).Enabled);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1f)]
    [InlineData(1e9f)]
    public void ANonsenseChannelReadsAsTheDefaults(float value)
    {
        float[] channels = new float[Igi1Command.ChannelCount];
        channels[^1] = value;
        Assert.Equal(ModernAimOptions.Off with { Enabled = true }, Igi1Command.UnpackAimPreferences(channels, enabled: true));
    }
}
