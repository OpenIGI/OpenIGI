using OpenIGI.Presentation.Hud.Remastered;
using Xunit;

namespace OpenIGI.Presentation.Tests.Rendering;

/// <summary>
/// The mark over a race's ghost: none close to, where the ghost's own contour is enough, coming in as it draws away, and going
/// with the ghost as it dissolves.
/// </summary>
public sealed class RaceGhostMarkTests
{
    [Theory]
    [InlineData(0.0, 0f)]
    [InlineData(11.9, 0f)]
    [InlineData(15.0, 0.5f)]
    [InlineData(18.0, 1f)]
    [InlineData(400.0, 1f)]
    public void The_mark_comes_in_as_the_ghost_draws_away(double metres, float opacity)
    {
        Assert.Equal(opacity, RemasteredHudRenderer.GhostMarkOpacity(metres, presence: 1f), 3);
    }

    [Fact]
    public void The_mark_goes_with_the_ghost()
    {
        Assert.Equal(0.5f, RemasteredHudRenderer.GhostMarkOpacity(40.0, presence: 0.5f), 3);
        Assert.Equal(0f, RemasteredHudRenderer.GhostMarkOpacity(40.0, presence: 0f));
    }
}
