using OpenIGI.Releases.Accounts;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>A Discord avatar's address, asked for as a PNG still: custom, animated and default artwork alike.</summary>
public sealed class AvatarCacheTests
{
    [Theory]
    [InlineData("https://cdn.discordapp.com/avatars/1/abc.png?size=128", "https://cdn.discordapp.com/avatars/1/abc.png?size=128")]
    [InlineData("https://cdn.discordapp.com/avatars/1/a_abc.gif?size=128", "https://cdn.discordapp.com/avatars/1/a_abc.png?size=128")]
    [InlineData("https://cdn.discordapp.com/embed/avatars/5.png", "https://cdn.discordapp.com/embed/avatars/5.png")]
    [InlineData("https://cdn.discordapp.com/avatars/1/abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void An_avatar_is_asked_for_as_a_png_still(string? avatar, string? expected) =>
        Assert.Equal(expected, AvatarCache.PngUrl(avatar));
}
