using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenIGI.Net.Discord;
using Xunit;

namespace OpenIGI.Net.Tests.Discord;

/// <summary>What the game says to Discord's local connection: framed as Discord reads it, and an activity as it shows one.</summary>
public sealed class DiscordPresenceTests
{
    /// <summary>A frame is its opcode and its length, little-endian, and then the JSON.</summary>
    [Fact]
    public void A_frame_carries_its_opcode_and_length_before_the_json()
    {
        byte[] frame = DiscordPresence.Encode(DiscordPresence.Handshake, DiscordPresence.HandshakeJson("123"));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(frame));
        int length = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4));
        Assert.Equal(frame.Length - 8, length);
        using JsonDocument json = JsonDocument.Parse(Encoding.UTF8.GetString(frame, 8, length));
        Assert.Equal(1, json.RootElement.GetProperty("v").GetInt32());
        Assert.Equal("123", json.RootElement.GetProperty("client_id").GetString());
    }

    /// <summary>An activity: its two lines, the clock, the party's size, and at most two buttons; none clears it.</summary>
    [Fact]
    public void An_activity_is_sent_as_discord_shows_it()
    {
        PresenceActivity activity = new(
            "Team Deathmatch · Trainyard", "Official OpenIGI EU", DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), 5, 16,
            [new PresenceButton("Join game", "https://openigi.com/join/ABC123"), new PresenceButton("Play free in the browser", "https://openigi.com"), new PresenceButton("Third", "https://x")]);

        using JsonDocument set = JsonDocument.Parse(DiscordPresence.SetActivityJson(42, activity, 7));
        JsonElement root = set.RootElement;
        Assert.Equal("SET_ACTIVITY", root.GetProperty("cmd").GetString());
        Assert.Equal("7", root.GetProperty("nonce").GetString());
        Assert.Equal(42, root.GetProperty("args").GetProperty("pid").GetInt32());
        JsonElement shown = root.GetProperty("args").GetProperty("activity");
        Assert.Equal("Team Deathmatch · Trainyard", shown.GetProperty("details").GetString());
        Assert.Equal(1_800_000_000, shown.GetProperty("timestamps").GetProperty("start").GetInt64());
        Assert.Equal([5, 16], shown.GetProperty("party").GetProperty("size").EnumerateArray().Select(static n => n.GetInt32()));
        Assert.Equal(2, shown.GetProperty("buttons").GetArrayLength());
        Assert.Equal("Play free in the browser", shown.GetProperty("buttons")[1].GetProperty("label").GetString());

        using JsonDocument clear = JsonDocument.Parse(DiscordPresence.SetActivityJson(42, null, 8));
        Assert.Equal(JsonValueKind.Null, clear.RootElement.GetProperty("args").GetProperty("activity").ValueKind);
    }

    /// <summary>With no party count there is no party; a one-letter line is padded, as Discord refuses one.</summary>
    [Fact]
    public void Lines_discord_would_refuse_are_made_acceptable()
    {
        using JsonDocument set = JsonDocument.Parse(DiscordPresence.SetActivityJson(1, new PresenceActivity("In the menus", "X", null, 0, 0, []), 1));
        JsonElement shown = set.RootElement.GetProperty("args").GetProperty("activity");
        Assert.False(shown.TryGetProperty("party", out _));
        Assert.False(shown.TryGetProperty("timestamps", out _));
        Assert.Equal(2, shown.GetProperty("state").GetString()!.Length);
    }
}
