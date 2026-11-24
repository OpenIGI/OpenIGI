using OpenIGI.Releases.Accounts;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>
/// The single-player boards a page shows: read when watched and not before, asked once while a read is out, kept while fresh,
/// each pick its own board, a failure shown and not asked again at once, and the board read again for another player's row.
/// </summary>
public sealed class MissionBoardFeedTests
{
    [Fact]
    public void A_board_is_read_when_watched_and_kept_while_fresh()
    {
        FakeBoards reader = new();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        MissionBoardFeed feed = new(reader, "igi1", () => now);

        feed.Update(watching: false);
        Assert.Empty(reader.Asked);

        feed.Update(watching: true);
        feed.Update(watching: true);
        Assert.Single(reader.Asked);
        Assert.Equal(FeedState.Loading, feed.BoardState);

        reader.Finish();
        feed.Update(watching: true);
        Assert.Equal(FeedState.Ready, feed.BoardState);
        Assert.Equal("igi1/all/2/classic/time", feed.Board!.Mission);

        now += TimeSpan.FromSeconds(10);
        feed.Update(watching: true);
        Assert.Single(reader.Asked);
    }

    [Fact]
    public void Each_pick_is_its_own_board()
    {
        FakeBoards reader = new();
        MissionBoardFeed feed = new(reader, "igi1");
        int before = feed.Revision;

        feed.ShowMission(3);
        feed.ShowDifficulty(1);
        feed.ShowRuleset("modern");
        feed.ShowMetric("rank");
        feed.Update(watching: true);

        Assert.True(feed.Revision > before);
        Assert.Equal(("igi1", 3, 1, "modern", "rank"), reader.Asked.Single());

        // Another game starts from every mission together.
        feed.ShowTitle("igi2");
        Assert.Null(feed.Mission);
    }

    [Fact]
    public void A_failed_read_is_shown_and_not_asked_again_at_once()
    {
        FakeBoards reader = new() { Fail = true };
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        MissionBoardFeed feed = new(reader, "igi1", () => now);

        feed.Update(watching: true);
        reader.Finish();
        feed.Update(watching: true);
        Assert.Equal(FeedState.Failed, feed.BoardState);
        Assert.Single(reader.Asked);

        now += TimeSpan.FromSeconds(31);
        feed.Update(watching: true);
        Assert.Equal(2, reader.Asked.Count);
    }

    [Fact]
    public void Another_player_reads_the_board_again_for_their_own_row()
    {
        FakeBoards reader = new();
        MissionBoardFeed feed = new(reader, "igi1");
        feed.Update(watching: true);
        reader.Finish();
        feed.Update(watching: true);

        feed.You = "111";
        Assert.Null(feed.Board);
        feed.Update(watching: true);
        Assert.Equal(2, reader.Asked.Count);
    }

    private sealed class FakeBoards : IRunBoardReader
    {
        private TaskCompletionSource<RunBoard>? _pending;
        private string _key = string.Empty;

        public List<(string, int?, int, string, string)> Asked { get; } = [];

        public bool Fail { get; set; }

        public Task<RunBoard> BoardAsync(string title, int? mission, int difficulty, string ruleset, string metric, string? you, CancellationToken cancel)
        {
            Asked.Add((title, mission, difficulty, ruleset, metric));
            _key = $"{title}/{mission?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "all"}/{difficulty}/{ruleset}/{metric}";
            _pending = new TaskCompletionSource<RunBoard>();
            return _pending.Task;
        }

        public void Finish()
        {
            if (Fail)
            {
                _pending!.SetException(new HttpRequestException("down"));
                return;
            }

            _pending!.SetResult(new RunBoard("igi1", _key, 2, "classic", "time", 0, [], null));
        }
    }
}
