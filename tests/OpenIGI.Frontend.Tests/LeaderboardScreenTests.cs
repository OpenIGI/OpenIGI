using OpenIGI.Frontend.Screens;
using OpenIGI.Input;
using OpenIGI.UI;
using OpenIGI.UI.Kit.Leaderboards;
using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>The Leaderboard page: the board read and drawn, a record opened from a row and closed by Back, the metric stepped by Tab.</summary>
[Collection(CollectionName)]
public sealed class LeaderboardScreenTests : ShellTestHarness
{
    [Fact]
    public void The_board_shows_the_podium_and_the_rest_ranked_under_it()
    {
        (Shell shell, FakeHost host) = BuildShell(Path.Combine(Path.GetTempPath(), "openigi-lb-" + Guid.NewGuid().ToString("N")));
        host.Leaderboard = new FakeLeaderboard(new FakeBoardReader { RowCount = 12 });
        var screen = new LeaderboardScreen(shell.Context);
        shell.Context.Stack.Reset(screen, animate: false);
        Pump(shell);

        Assert.Equal(3, screen.Root!.Descendants().OfType<PodiumCard>().Count());
        Assert.Equal(9, screen.Root.Descendants().OfType<RankRow>().Count(row => row.Visibility == Visibility.Visible && !row.IsYou));

        // The player's own row, outside the top, pinned under the list.
        Assert.Contains(screen.Root.Descendants().OfType<RankRow>(), row => row.IsYou && row.Row.Rank == 212);
    }

    [Fact]
    public void A_row_opens_its_record_and_back_returns_to_the_board()
    {
        (Shell shell, FakeHost host) = BuildShell(Path.Combine(Path.GetTempPath(), "openigi-lb-" + Guid.NewGuid().ToString("N")));
        var boards = new FakeLeaderboard(new FakeBoardReader());
        host.Leaderboard = boards;
        var screen = new LeaderboardScreen(shell.Context);
        shell.Context.Stack.Reset(screen, animate: false);
        Pump(shell);

        RankRow row = screen.Root!.Descendants().OfType<RankRow>().First(line => !line.IsYou);
        row.Activate();
        Pump(shell);
        Assert.Equal(row.Row.RecordId, boards.Feed.RecordId);
        Assert.Single(screen.Root.Descendants().OfType<RecordHero>());
        Assert.Equal(3, screen.Root.Descendants().OfType<MatchLine>().Count());

        Back(shell);
        Pump(shell, 0.1f);
        Assert.Null(boards.Feed.RecordId);
        Assert.Same(screen, shell.Context.Stack.Top);
    }

    [Fact]
    public void Tab_ranks_by_the_next_metric()
    {
        (Shell shell, FakeHost host) = BuildShell(Path.Combine(Path.GetTempPath(), "openigi-lb-" + Guid.NewGuid().ToString("N")));
        var boards = new FakeLeaderboard(new FakeBoardReader());
        host.Leaderboard = boards;
        shell.Context.Stack.Reset(new LeaderboardScreen(shell.Context), animate: false);
        Pump(shell);

        var input = new InputState();
        input.BeginFrame();
        input.Keyboard.SetKey(KeyCode.Tab, true);
        shell.Update(new LayoutContext(1920f, 1080f), input, 1f / 60f);
        Assert.Equal("kills", boards.Feed.Metric.Id);
    }

    [Fact]
    public void A_board_that_cant_be_read_says_so()
    {
        (Shell shell, FakeHost host) = BuildShell(Path.Combine(Path.GetTempPath(), "openigi-lb-" + Guid.NewGuid().ToString("N")));
        host.Leaderboard = new FakeLeaderboard(new FakeBoardReader { Fail = true });
        var screen = new LeaderboardScreen(shell.Context);
        shell.Context.Stack.Reset(screen, animate: false);
        Pump(shell);

        Assert.Empty(screen.Root!.Descendants().OfType<PodiumCard>());
        Assert.Contains(screen.Root.Descendants().OfType<OpenIGI.UI.Widgets.Label>(), label => label.Text.Contains("CAN'T BE REACHED", StringComparison.Ordinal));
    }
}
