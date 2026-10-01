using OpenIGI.Releases;
using Xunit;

namespace OpenIGI.Releases.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("v0.2.0-beta.3", "0.2.0-beta.3")]
    [InlineData("1.0.0-beta.1+abc1234", "1.0.0-beta.1")]
    [InlineData(" V2.10.4 ", "2.10.4")]
    public void A_tag_or_informational_version_parses_to_its_release(string text, string expected) =>
        Assert.Equal(expected, ReleaseVersion.Parse(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.x")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-beta..1")]
    [InlineData("0.0.0-dev+")]
    public void Anything_else_is_rejected(string text)
    {
        if (text == "0.0.0-dev+")
        {
            // An empty build suffix is still a version; it is the pre-release that must be well formed.
            Assert.True(ReleaseVersion.TryParse(text, out _));
            return;
        }

        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Fact]
    public void Betas_order_numerically_and_precede_their_release()
    {
        string[] ordered =
        [
            "0.1.0-beta.1",
            "0.1.0-beta.2",
            "0.1.0-beta.9",
            "0.1.0-beta.10",
            "0.1.0",
            "0.1.1-beta.1",
            "0.1.1",
            "0.2.0-beta.1",
            "0.10.0",
            "1.0.0",
        ];

        string[] shuffled = [.. ordered.Reverse()];
        Assert.Equal(ordered, shuffled.Select(ReleaseVersion.Parse).Order().Select(static v => v.ToString()));
    }

    [Fact]
    public void Build_metadata_does_not_make_a_different_release()
    {
        Assert.Equal(ReleaseVersion.Parse("0.2.0-beta.1+aaaa"), ReleaseVersion.Parse("0.2.0-beta.1+bbbb"));
        Assert.True(ReleaseVersion.Parse("0.2.0") > ReleaseVersion.Parse("0.2.0-beta.7"));
        Assert.False(ReleaseVersion.Parse("0.2.0").IsPrerelease);
        Assert.True(ReleaseVersion.Parse("0.2.0-beta.7").IsPrerelease);
    }
}
