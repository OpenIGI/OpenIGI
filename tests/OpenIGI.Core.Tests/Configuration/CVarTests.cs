using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Pins the behaviour every console variable shares: clamping, parsing, defaults and change events.
/// </summary>
/// <remarks>
/// Each test builds its own <see cref="CVarRegistry"/> rather than touching
/// <see cref="CVarRegistry.Default"/>, so nothing leaks between tests or into the running game.
/// </remarks>
public sealed class CVarTests
{
    /// <summary>The presets a settings enum stands in for.</summary>
    private enum Quality
    {
        Off,
        Low,
        High,
    }

    [Fact]
    public void Numeric_ClampsRatherThanRejecting()
    {
        NumericCVar<int> quality = new("r_test_quality", 3, 0, 4);

        Assert.True(quality.TrySetText("99", out string? reason));
        Assert.Null(reason);
        Assert.Equal(4, quality.Value);

        Assert.True(quality.TrySetText("-5", out _));
        Assert.Equal(0, quality.Value);
    }

    [Fact]
    public void Numeric_ClampsTheDefaultItself()
    {
        // A declaration whose default sits outside its own range is a bug, but silently holding an
        // out-of-range value would be a worse one: the range is the contract the consumer relies on.
        NumericCVar<int> variable = new("r_test_clamped_default", 10, 0, 4);

        Assert.Equal(4, variable.DefaultValue);
        Assert.Equal(4, variable.Value);
        Assert.True(variable.IsDefault);
    }

    [Fact]
    public void Numeric_RefusesTextThatIsNotANumber()
    {
        NumericCVar<float> density = new("r_test_density", 0.5f, 0f, 1f);

        Assert.False(density.TrySetText("dense", out string? reason));
        Assert.NotNull(reason);
        Assert.Equal(0.5f, density.Value);
    }

    [Fact]
    public void Numeric_FormatsInvariantlySoSettingsFilesArePortable()
    {
        NumericCVar<float> scale = new("r_test_scale", 1f, 0.5f, 2f) { Value = 1.25f };

        // A machine whose decimal separator is a comma must still write "1.25", or the file it
        // saves cannot be read anywhere else.
        Assert.Equal("1.25", scale.ValueText);
        Assert.True(scale.TrySetText("0.75", out _));
        Assert.Equal(0.75f, scale.Value);
    }

    [Fact]
    public void Numeric_MapsNotANumberOntoTheLowerBound()
    {
        NumericCVar<float> intensity = new("r_test_intensity", 1f, 0f, 4f);

        Assert.True(intensity.TrySetText("NaN", out _));
        Assert.Equal(0f, intensity.Value);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("ON", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    [InlineData("FALSE", false)]
    public void Bool_AcceptsEverySpellingAPlayerMightType(string text, bool expected)
    {
        BoolCVar flag = new("r_test_flag", !expected);

        Assert.True(flag.TrySetText(text, out _));
        Assert.Equal(expected, flag.Value);
    }

    [Fact]
    public void Bool_StoresAsZeroOrOne()
    {
        BoolCVar flag = new("r_test_flag", false) { Value = true };

        Assert.Equal("1", flag.ValueText);
        Assert.False(flag.TrySetText("maybe", out _));
    }

    [Fact]
    public void Enum_RoundTripsByName()
    {
        EnumCVar<Quality> quality = new("r_test_enum", Quality.Low);

        Assert.Equal("low", quality.ValueText);
        Assert.True(quality.TrySetText("HIGH", out _));
        Assert.Equal(Quality.High, quality.Value);
        Assert.Equal("off | low | high", quality.RangeText);
        Assert.Equal(3, quality.Choices.Count);
    }

    [Fact]
    public void Enum_RefusesAValueOutsideTheEnumeration()
    {
        EnumCVar<Quality> quality = new("r_test_enum", Quality.Low);

        // Enum.TryParse accepts any number for an enum type, which would let "7" through as a
        // nonsense value; the variable checks the result is actually one of the members.
        Assert.False(quality.TrySetText("7", out _));
        Assert.False(quality.TrySetText("ludicrous", out _));
        Assert.Equal(Quality.Low, quality.Value);
    }

    [Fact]
    public void Changed_FiresOnceWithBothValues()
    {
        NumericCVar<int> variable = new("r_test_event", 1, 0, 8);
        List<CVarChangedEventArgs> events = [];
        variable.Changed += (_, e) => events.Add(e);

        variable.Value = 4;
        variable.Value = 4;

        CVarChangedEventArgs only = Assert.Single(events);
        Assert.Equal("1", only.PreviousText);
        Assert.Equal("4", only.CurrentText);
        Assert.Same(variable, only.Variable);
    }

    [Fact]
    public void Changed_DoesNotFireWhenClampingLandsOnTheCurrentValue()
    {
        NumericCVar<int> variable = new("r_test_event", 4, 0, 4);
        int fired = 0;
        variable.Changed += (_, _) => fired++;

        variable.TrySetText("99", out _);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Reset_RestoresTheDefault()
    {
        NumericCVar<int> variable = new("r_test_reset", 2, 0, 8) { Value = 7 };

        Assert.False(variable.IsDefault);
        variable.Reset();

        Assert.True(variable.IsDefault);
        Assert.Equal(2, variable.Value);
    }

    /// <summary>
    /// A value is recognised by what it parses to, not by how it happens to be spelled.
    /// </summary>
    /// <remarks>
    /// The graphics presets and the settings menu's quality groups both work by recognising the values
    /// they wrote. They used to do it by comparing against <see cref="ICVar.ValueText"/>, which renders a
    /// float as its own shortest round trip -- so a preset declaring <c>0.70</c> assigned 0.7, read back
    /// "0.7", failed its own comparison, and reported Custom from then on. Three shipped menu rows were
    /// broken this way. The same trap catches a clamped value, and a boolean or enumeration spelled in any
    /// of the forms it accepts but does not render.
    /// </remarks>
    [Fact]
    public void MatchesText_RecognisesEveryFormThatWouldAssignTheSameValue()
    {
        NumericCVar<float> scale = new("r_test_scale", 1f, 0.5f, 2f) { Value = 0.7f };

        Assert.Equal("0.7", scale.ValueText);
        Assert.True(scale.MatchesText("0.70"));
        Assert.True(scale.MatchesText("0.7"));
        Assert.False(scale.MatchesText("0.85"));

        // Clamped on the way in, so it is the value this variable would hold.
        NumericCVar<int> directions = new("r_test_ao", 6, 0, 8) { Value = 8 };
        Assert.True(directions.MatchesText("99"));
        Assert.False(directions.MatchesText("6"));

        BoolCVar cards = new("r_test_cards", false) { Value = true };
        Assert.True(cards.MatchesText("1"));
        Assert.True(cards.MatchesText("true"));
        Assert.False(cards.MatchesText("0"));

        EnumCVar<Quality> lighting = new("r_test_lighting", Quality.Off) { Value = Quality.High };
        Assert.True(lighting.MatchesText("high"));
        Assert.True(lighting.MatchesText("High"));
        Assert.False(lighting.MatchesText("low"));

        // Text that does not parse at all is not a match, and must not throw.
        Assert.False(scale.MatchesText("banana"));
        Assert.False(scale.MatchesText(string.Empty));
    }
}
