using OpenIGI.Desktop;
using OpenIGI.Host.DevTools;
using OpenIGI.Input;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Covers the key names <c>bind</c> writes into <c>settings.cfg</c> and readers type by hand.
/// </summary>
public sealed class BindKeyNamesTests
{
    /// <summary>
    /// The names are persisted, so a key that stopped round-tripping would silently orphan every
    /// binding a reader had saved against it.
    /// </summary>
    [Fact]
    public void Every_key_round_trips_through_its_name()
    {
        foreach (KeyCode key in Enum.GetValues<KeyCode>())
        {
            if (key == KeyCode.None)
            {
                continue;
            }

            string name = BindKeyNames.NameOf(key);
            Assert.False(string.IsNullOrEmpty(name), $"{key} has no bind name.");

            Assert.True(BindKeyNames.TryParse(name, out KeyCode parsed), $"'{name}' did not parse.");
            Assert.Equal(key, parsed);
        }
    }

    /// <summary>
    /// <c>D1</c> is the enumeration's spelling of the key printed "1"; nobody would guess to write
    /// <c>bind d1</c>, so the printed form is what the name is.
    /// </summary>
    [Theory]
    [InlineData(KeyCode.D1, "1")]
    [InlineData(KeyCode.D0, "0")]
    public void Digit_keys_are_named_after_what_is_printed_on_them(KeyCode key, string expected)
    {
        Assert.Equal(expected, BindKeyNames.NameOf(key));
    }

    [Fact]
    public void The_enumeration_spelling_is_still_accepted()
    {
        Assert.True(BindKeyNames.TryParse("d1", out KeyCode parsed));
        Assert.Equal(KeyCode.D1, parsed);
    }

    [Fact]
    public void Names_are_case_insensitive_and_trimmed()
    {
        Assert.True(BindKeyNames.TryParse("  F5  ", out KeyCode upper));
        Assert.True(BindKeyNames.TryParse("f5", out KeyCode lower));
        Assert.Equal(upper, lower);
        Assert.Equal(KeyCode.F5, lower);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-key")]
    public void An_unknown_name_is_refused_rather_than_guessed(string? name)
    {
        Assert.False(BindKeyNames.TryParse(name, out KeyCode key));
        Assert.Equal(KeyCode.None, key);
    }

    [Fact]
    public void The_absent_key_has_no_name()
    {
        Assert.Equal(string.Empty, BindKeyNames.NameOf(KeyCode.None));
    }
}
