using OpenIGI.Game.Igi2.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Where a co-op look's body is borrowed from, and what counts as one.</summary>
public sealed class Igi2LookPacksTests
{
    [Theory]
    [InlineData("igiagent2_1")]
    [InlineData("igiagent2_4")]
    [InlineData("chn3_1")]
    [InlineData("anya_1")]
    [InlineData("anya_3")]
    public void A_look_s_body_at_any_detail_is_a_look(string model) => Assert.True(Igi2LookPacks.IsLook(model));

    [Theory]
    [InlineData("005_01_1")]
    [InlineData("100_01_1")]
    [InlineData("anya_at_keyboard")]
    [InlineData("")]
    public void Anything_else_is_not_and_opens_nothing(string model) => Assert.False(Igi2LookPacks.IsLook(model));

    [Fact]
    public void The_multiplayer_bodies_come_from_the_multiplayer_pack_and_the_campaign_s_people_from_their_locations()
    {
        Assert.Equal(Igi2LookPacks.Multiplayer, Igi2LookPacks.SourceOf("les2_1"));
        Assert.Equal("location3", Igi2LookPacks.SourceOf("anya_1").Name);
        Assert.Equal("location2", Igi2LookPacks.SourceOf("priboi_1").Name);
    }
}
