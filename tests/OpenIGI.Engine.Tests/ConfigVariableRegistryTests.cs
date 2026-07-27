using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Configuration;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>Covers the live bindings and duplicate detection of the variable registry.</summary>
public sealed class ConfigVariableRegistryTests
{
    [Fact]
    public void ReadsThroughToTheOwningField()
    {
        var registry = new ConfigVariableRegistry();
        int health = 100;
        registry.RegisterInteger("player.health", () => health, value => health = value);

        Assert.Equal("100", registry.GetString("player.health"));

        health = 42;
        Assert.Equal("42", registry.GetString("player.health"));
    }

    [Fact]
    public void WritesThroughToTheOwningField()
    {
        var registry = new ConfigVariableRegistry();
        bool alarm = false;
        registry.RegisterBoolean("alarm.isOn", () => alarm, value => alarm = value);

        registry.SetString("alarm.isOn", "1");
        Assert.True(alarm);

        registry.SetString("alarm.isOn", "0");
        Assert.False(alarm);

        registry.SetString("alarm.isOn", "true");
        Assert.True(alarm);
    }

    [Fact]
    public void RoundTripsEveryValueType()
    {
        var registry = new ConfigVariableRegistry();
        int integer = 0;
        double real = 0;
        string text = string.Empty;

        registry.RegisterInteger("i", () => integer, value => integer = value);
        registry.RegisterReal("r", () => real, value => real = value);
        registry.RegisterText("s", () => text, value => text = value);

        registry.SetString("i", "-7");
        registry.SetString("r", "1.25");
        registry.SetString("s", "Joe");

        Assert.Equal(-7, integer);
        Assert.Equal(1.25, real);
        Assert.Equal("Joe", text);

        Assert.True(registry.TryGet("r", out ConfigVariable? variable));
        Assert.Equal(1.25, variable.GetReal());
        Assert.Equal(ConfigVariableKind.Real, variable.Kind);
    }

    [Fact]
    public void ParsesRealValuesInvariantly()
    {
        var registry = new ConfigVariableRegistry();
        double value = 0;
        registry.RegisterReal("gfx.gamma", () => value, real => value = real);

        registry.SetString("gfx.gamma", "0.5");
        Assert.Equal(0.5, value);
    }

    [Fact]
    public void DuplicateRegistrationIsFatal()
    {
        var registry = new ConfigVariableRegistry();
        registry.RegisterBoolean("guard_1.isOn", () => true);

        EngineFatalException error = Assert.Throws<EngineFatalException>(
            () => registry.RegisterBoolean("guard_1.isOn", () => false));
        Assert.Contains("already registered", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyVariablesRejectWrites()
    {
        var registry = new ConfigVariableRegistry();
        registry.RegisterInteger("engine.tick", () => 7);

        Assert.True(registry.TryGet("engine.tick", out ConfigVariable? variable));
        Assert.True(variable.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => registry.SetString("engine.tick", "8"));
    }

    [Fact]
    public void RejectsValuesThatDoNotParse()
    {
        var registry = new ConfigVariableRegistry();
        int value = 0;
        registry.RegisterInteger("count", () => value, parsed => value = parsed);

        Assert.Throws<FormatException>(() => registry.SetString("count", "many"));
    }

    [Fact]
    public void UnregistersVariables()
    {
        var registry = new ConfigVariableRegistry();
        registry.RegisterBoolean("guard_1.isOn", () => true);

        Assert.True(registry.Unregister("guard_1.isOn"));
        Assert.False(registry.Contains("guard_1.isOn"));

        // Re-registering after the owning task is destroyed must work.
        registry.RegisterBoolean("guard_1.isOn", () => false);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void LooksUpNamesCaseInsensitively()
    {
        var registry = new ConfigVariableRegistry();
        registry.RegisterBoolean("GOIsBlood", () => true);

        Assert.True(registry.Contains("goisblood"));
    }

    [Fact]
    public void UnknownNamesReportNotFound()
    {
        var registry = new ConfigVariableRegistry();

        Assert.False(registry.TryGet("nope", out _));
        Assert.Throws<KeyNotFoundException>(() => registry.GetString("nope"));
        Assert.Throws<KeyNotFoundException>(() => registry.SetString("nope", "1"));
    }
}
