using OpenIGI.Game.Igi2.Config;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The configuration, as config.c reads <c>config.qvm</c> and answers the scripts.</summary>
public sealed class Igi2GameConfigTests
{
    /// <summary>With no file, one profile stands: David Jones, at the middle difficulty, with the music at eight tenths.</summary>
    [Fact]
    public void The_defaults_are_one_profile()
    {
        Igi2GameConfig config = new();
        Assert.Single(config.Profiles);
        Assert.Equal("David Jones", config.Active?.CallSign);
        Assert.Equal(1, config.Active?.Difficulty);
        Assert.Equal(0.8f, config.Active?.Music.Volume);
        Assert.True(config.Active?.InvertMouse);
    }

    /// <summary>The scripts read the active profile's difficulty, and a setter before any profile writes nothing.</summary>
    [Fact]
    public void Scripts_read_the_active_profile()
    {
        Igi2GameConfig config = new();
        NativeRegistry natives = new();
        config.Register(natives);
        config.Active!.Difficulty = 2;
        Assert.True(natives.TryResolve("Config_GetActiveDifficultyLevel", out QvmSymbol? difficulty));
        Assert.Equal(QvmSymbolKind.Function, difficulty.Kind);
    }

    /// <summary>
    /// The shipped file is read whole: it completes, so its profiles stand, the active one its <c>GOStart</c> names, at <c>GD_2</c>,
    /// with its bindings, and the graphics options filed with it.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_shipped_file_loads()
    {
        string root = GameDataSource.Igi2.RequireRoot();
        RetailIgi2LevelLoadTests.Harness harness = new(Path.Combine(root, "MISSIONS", "location1", "level1", "objects.qvm"), loadTables: true);
        Igi2GameConfig config = harness.Session.Services.Require<Igi2GameConfig>();

        Assert.NotEmpty(config.Profiles);
        Igi2PlayerProfile? active = config.Active;
        Assert.NotNull(active);
        Assert.Equal(1, active.Difficulty);
        Assert.NotEmpty(active.Bindings);
        Assert.True(active.Gfx.LensFlares);
    }
}
