using OpenIGI.Launcher.Services;
using Xunit;

namespace OpenIGI.Launcher.Tests;

public sealed class InviteLinksTests
{
    [Theory]
    [InlineData("openigi://join/ABC234", "-join", "ABC234")]
    [InlineData("openigi://join/abc234/", "-join", "ABC234")]
    [InlineData("OPENIGI://party/QWERTY", "+party_join", "QWERTY")]
    public void An_invite_link_starts_the_game_into_the_room_or_the_party(string link, string option, string code) =>
        Assert.Equal([option, code], InviteLinks.Arguments(link));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openigi://join/ABC23")]
    [InlineData("openigi://join/ABC2340")]
    [InlineData("openigi://join/ABCDE1")]
    [InlineData("openigi://host/ABC234")]
    [InlineData("openigi://join/ABC234 -exec evil")]
    [InlineData("https://openigi.com/join/ABC234")]
    public void Anything_but_an_invite_link_starts_nothing(string? link) => Assert.Null(InviteLinks.Arguments(link));

    [Fact]
    public void The_invite_is_found_among_the_launchers_arguments() =>
        Assert.Equal(["-join", "ZXCVBN"], InviteLinks.FromCommandLine(["--minimized", "openigi://join/ZXCVBN"]));
}
