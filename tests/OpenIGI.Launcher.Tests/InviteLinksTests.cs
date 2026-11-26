using OpenIGI.Launcher.Services;
using Xunit;

namespace OpenIGI.Launcher.Tests;

public sealed class InviteLinksTests
{
    [Theory]
    [InlineData("openigi://join/ABC234", "-join", "ABC234")]
    [InlineData("openigi://join/abc234/", "-join", "ABC234")]
    [InlineData("OPENIGI://party/QWERTY", "+party_join", "QWERTY")]
    public void An_invite_link_starts_the_game_into_the_room_or_the_party(string link, string option, string code)
    {
        Invite invite = InviteLinks.Read(link)!;
        Assert.Equal([option, code], invite.Arguments);
        Assert.Equal("igi1", invite.Game);
    }

    [Theory]
    [InlineData("openigi://join/ABC234?game=igi2", "igi2")]
    [InlineData("openigi://join/ABC234/?game=igi1", "igi1")]
    [InlineData("openigi://connect/185.110.190.243:26001?game=igi2", "igi2")]
    public void A_link_says_which_game_to_start(string link, string game) => Assert.Equal(game, InviteLinks.Read(link)!.Game);

    [Theory]
    [InlineData("openigi://connect/185.110.190.243:26001", "185.110.190.243:26001")]
    [InlineData("openigi://connect/Play.OpenIGI.com:26002", "play.openigi.com:26002")]
    public void A_server_link_connects_to_its_address(string link, string address) =>
        Assert.Equal(["-connect", address], InviteLinks.Read(link)!.Arguments);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openigi://join/ABC23")]
    [InlineData("openigi://join/ABC2340")]
    [InlineData("openigi://join/ABCDE1")]
    [InlineData("openigi://host/ABC234")]
    [InlineData("openigi://join/ABC234 -exec evil")]
    [InlineData("openigi://join/ABC234?game=igi9")]
    [InlineData("openigi://join/ABC234?game=igi2&exec=evil")]
    [InlineData("openigi://connect/185.110.190.243")]
    [InlineData("openigi://connect/185.110.190.243:0")]
    [InlineData("openigi://connect/185.110.190.243:70000")]
    [InlineData("openigi://connect/-evil:26001")]
    [InlineData("openigi://connect/a:1:2")]
    [InlineData("openigi://connect/host:26001/path")]
    [InlineData("openigi://connect/user@host:26001")]
    [InlineData("https://openigi.com/join/ABC234")]
    public void Anything_but_an_invite_link_starts_nothing(string? link) => Assert.Null(InviteLinks.Read(link));

    [Fact]
    public void The_invite_is_found_among_the_launchers_arguments() =>
        Assert.Equal(["-join", "ZXCVBN"], InviteLinks.FromCommandLine(["--minimized", "openigi://join/ZXCVBN"])!.Arguments);
}
