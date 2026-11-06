using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer.Modes;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>Multiplayer map layouts: read as a modder writes them, refused when they lay out nothing to play.</summary>
public sealed class MpLayoutTests
{
    private const string Arena = """
        {
          // The radar base's yard, two teams.
          "format": 1,
          "title": "igi1",
          "map": "1",
          "name": "Radar Yard",
          "modes": ["tdm", "ffa"],
          "bounds": [{ "min": [0, 0, -100], "max": [1000, 1000, 500] }],
          "spawns": [
            { "position": [10, 10, 0], "yaw": 90, "team": 0 },
            { "position": [990, 990, 0], "yaw": 270, "team": 1 },
            { "position": [500, 500, 0] },
          ],
          "points": [{ "id": "A", "kind": "capture", "position": [500, 500, 0], "radius": 64 }],
        }
        """;

    [Fact]
    public void A_layout_reads_as_written_and_answers_where_play_is()
    {
        MpLayout layout = Assert.IsType<MpLayout>(MpLayout.Parse(Arena, out string? problem), exactMatch: true);
        Assert.Null(problem);
        Assert.Equal("Radar Yard", layout.Name);
        Assert.True(layout.Plays("TDM"));
        Assert.False(layout.Plays("ctf"));
        Assert.True(layout.InBounds(new Vector3d(500, 500, 0)));
        Assert.False(layout.InBounds(new Vector3d(1500, 500, 0)));
        Assert.Equal(2, layout.SpawnsFor(0).Count());
        Assert.Equal(3, layout.SpawnsFor(-1).Count());
        Assert.Equal(MpPointKind.Capture, Assert.Single(layout.Points).Kind);
        Assert.False(layout.KeepsSoldiers);
    }

    [Fact]
    public void A_layout_saved_reads_back_the_same()
    {
        MpLayout layout = MpLayout.Parse(Arena, out _)!;
        MpLayout again = MpLayout.Parse(layout.ToJson(), out string? problem)!;
        Assert.Null(problem);
        Assert.Equal(layout.Name, again.Name);
        Assert.Equal(layout.Spawns.Length, again.Spawns.Length);
        Assert.Equal(layout.Points[0].Radius, again.Points[0].Radius);
    }

    [Theory]
    [InlineData("""{ "format": 1, "spawns": [] }""", "no spawns")]
    [InlineData("""{ "format": 9, "spawns": [{ "position": [0, 0, 0] }] }""", "newer")]
    [InlineData("""{ "spawns": [{ "position": [0, 0] }] }""", "x, y, z")]
    [InlineData("""not json""", "not a layout")]
    public void A_layout_that_lays_out_nothing_playable_is_refused_with_a_reason(string json, string reason)
    {
        Assert.Null(MpLayout.Parse(json, out string? problem));
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }
}
