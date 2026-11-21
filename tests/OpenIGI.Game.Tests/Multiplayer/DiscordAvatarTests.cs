using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>A player's Discord avatar as a session passes it on: only ever a picture on Discord's own server.</summary>
public sealed class DiscordAvatarTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("https://cdn.discordapp.com/avatars/279730101439168512/" + Hash + ".png?size=128", "279730101439168512/" + Hash)]
    [InlineData("https://cdn.discordapp.com/avatars/279730101439168512/a_" + Hash + ".gif?size=128", "279730101439168512/a_" + Hash)]
    [InlineData("https://cdn.discordapp.com/embed/avatars/5.png", "embed/5")]
    [InlineData("https://evil.example/avatars/279730101439168512/" + Hash + ".png", "")]
    [InlineData("http://cdn.discordapp.com/avatars/279730101439168512/" + Hash + ".png", "")]
    [InlineData("https://cdn.discordapp.com/avatars/1/abc.png", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void An_account_avatar_becomes_a_reference_only_when_it_is_a_discord_avatar(string? avatar, string reference) =>
        Assert.Equal(reference, DiscordAvatar.Reference(avatar));

    [Theory]
    [InlineData("279730101439168512/" + Hash, "https://cdn.discordapp.com/avatars/279730101439168512/" + Hash + ".png?size=64")]
    [InlineData("279730101439168512/a_" + Hash, "https://cdn.discordapp.com/avatars/279730101439168512/a_" + Hash + ".png?size=64")]
    [InlineData("embed/5", "https://cdn.discordapp.com/embed/avatars/5.png")]
    [InlineData("evil.example/" + Hash, null)]
    [InlineData("279730101439168512/../../x", null)]
    [InlineData("279730101439168512/" + Hash + "?x=1", null)]
    [InlineData("embed/../5", null)]
    [InlineData("", null)]
    public void A_reference_names_only_a_png_on_discords_server(string reference, string? url) =>
        Assert.Equal(url, DiscordAvatar.Url(reference));

    [Fact]
    public void A_join_request_carries_the_avatar_and_drops_one_that_is_not_discords()
    {
        JoinRequest sent = new("Anya", "dev", string.Empty, Avatar: "279730101439168512/" + Hash);
        Assert.Equal(sent.Avatar, JoinRequest.Decode(sent.Encode())?.Avatar);

        JoinRequest forged = sent with { Avatar = "evil.example/x" };
        Assert.Equal(string.Empty, JoinRequest.Decode(forged.Encode())?.Avatar);
    }

    [Fact]
    public void The_scoreboard_carries_each_players_avatar()
    {
        Scoreboard sent = new(1, 0, 0, [new ScoreboardLine(3, "Anya", 0, 0, 2, 2, true, Entity: 4, Avatar: "embed/3")]);
        BitWriter writer = new(256);
        sent.Write(writer);
        BitReader reader = new(writer.AsSpan().ToArray());

        Assert.Equal("embed/3", Scoreboard.Read(ref reader)?.Lines[0].Avatar);
    }
}
