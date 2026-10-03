using OpenIGI.Core.Diagnostics;
using Xunit;

namespace OpenIGI.Core.Tests.Diagnostics;

/// <summary>Pins how a level specification is read and how categories inherit their parents' levels.</summary>
public sealed class LogLevelPolicyTests
{
    [Theory]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("DEBUG", LogLevel.Debug)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("warn", LogLevel.Warning)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("off", LogLevel.None)]
    [InlineData("minimal", LogLevel.Warning)]
    [InlineData("verbose", LogLevel.Information)]
    public void TryParseLevel_ReadsLevelsAndTheOldVerbosityNames(string text, LogLevel expected)
    {
        Assert.True(LogLevelPolicy.TryParseLevel(text, out LogLevel level));
        Assert.Equal(expected, level);
    }

    [Fact]
    public void TryApply_SetsTheDefaultAndOverrides()
    {
        LogLevelPolicy policy = new();

        Assert.True(policy.TryApply("warning, Vfs=debug; AI.Gunner=trace"));

        Assert.Equal(LogLevel.Warning, policy.Default);
        Assert.Equal(LogLevel.Debug, policy.LevelFor("Vfs"));
        Assert.Equal(LogLevel.Trace, policy.LevelFor("AI.Gunner"));
        Assert.Equal(LogLevel.Warning, policy.LevelFor("Renderer"));
    }

    [Fact]
    public void TryApply_ReportsWhatItCouldNotRead()
    {
        LogLevelPolicy policy = new();

        Assert.False(policy.TryApply("loud,Vfs=debug"));
        Assert.Equal(LogLevel.Debug, policy.LevelFor("Vfs"));
    }

    [Fact]
    public void LevelFor_TheLongestMatchingPrefixWins()
    {
        LogLevelPolicy policy = new(LogLevel.Warning);
        policy.SetOverride("AI", LogLevel.Information);
        policy.SetOverride("AI.Gunner", LogLevel.Trace);

        Assert.Equal(LogLevel.Information, policy.LevelFor("ai.alarm"));
        Assert.Equal(LogLevel.Trace, policy.LevelFor("AI.Gunner.Aim"));

        // A prefix covers a category only at a dot: "AIR" is not under "AI".
        Assert.Equal(LogLevel.Warning, policy.LevelFor("AIR"));
    }

    [Fact]
    public void SetOverride_ClearingFallsBackToTheDefault()
    {
        LogLevelPolicy policy = new(LogLevel.Error);
        policy.SetOverride("Vfs", LogLevel.Debug);
        Assert.Equal(LogLevel.Debug, policy.LevelFor("Vfs"));

        policy.SetOverride("Vfs", null);

        Assert.Equal(LogLevel.Error, policy.LevelFor("Vfs"));
    }

    [Fact]
    public void Default_ChangingItReachesCategoriesAlreadyAskedAbout()
    {
        LogLevelPolicy policy = new(LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, policy.LevelFor("Renderer"));

        policy.Default = LogLevel.Debug;

        Assert.Equal(LogLevel.Debug, policy.LevelFor("Renderer"));
    }
}
