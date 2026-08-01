using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Pins the console's editing model — the part a player actually touches — without a window.
/// </summary>
public sealed class ConsoleModelTests
{
    private readonly CVarRegistry _variables = new();
    private readonly ConsoleSystem _console;
    private readonly ConsoleModel _model;

    /// <summary>Initialises a new instance of the <see cref="ConsoleModelTests"/> class.</summary>
    public ConsoleModelTests()
    {
        _console = new ConsoleSystem(_variables);
        BuiltInConsoleCommands.Register(_console);
        _model = new ConsoleModel(_console, scrollbackCapacity: 256);
    }

    [Fact]
    public void TypingInsertsAtTheCaretAndIgnoresControlCharacters()
    {
        _model.Insert("r_fog");
        _model.MoveCaret(-3);
        _model.Insert("\t\n");
        _model.Insert("XY");

        Assert.Equal("r_XYfog", _model.Input);
        Assert.Equal(4, _model.CaretIndex);
    }

    [Fact]
    public void BackspaceAndDeleteRespectTheEnds()
    {
        _model.SetInput("ab");
        _model.CaretToStart();

        _model.Backspace();
        Assert.Equal("ab", _model.Input);

        _model.Delete();
        Assert.Equal("b", _model.Input);

        _model.CaretToEnd();
        _model.Delete();
        Assert.Equal("b", _model.Input);
    }

    [Fact]
    public void WordMotionSkipsRunsOfWhitespace()
    {
        _model.SetInput("bind   f5 exec");

        _model.CaretToStart();
        _model.MoveCaretByWord(1);
        Assert.Equal(7, _model.CaretIndex);

        _model.CaretToEnd();
        _model.MoveCaretByWord(-1);
        Assert.Equal(10, _model.CaretIndex);
    }

    [Fact]
    public void SubmitRunsTheLineRecordsItAndClearsTheInput()
    {
        NumericCVar<int> quality = CVar.Int("r_fog_quality", 0, 0, 4, registry: _variables);
        _model.SetInput("r_fog_quality 2");

        Assert.True(_model.Submit());

        Assert.Equal(2, quality.Value);
        Assert.Equal(string.Empty, _model.Input);
        Assert.Equal(["r_fog_quality 2"], _model.History);
    }

    [Fact]
    public void SubmitDoesNotRecordABlankLineOrAnImmediateRepeat()
    {
        _model.SetInput("   ");
        _model.Submit();
        _model.SetInput("echo hi");
        _model.Submit();
        _model.SetInput("echo hi");
        _model.Submit();

        Assert.Equal(["echo hi"], _model.History);
    }

    [Fact]
    public void HistoryWalksBackAndRestoresWhatWasBeingTyped()
    {
        _model.SetInput("echo one");
        _model.Submit();
        _model.SetInput("echo two");
        _model.Submit();

        _model.SetInput("half typ");
        _model.HistoryPrevious();
        Assert.Equal("echo two", _model.Input);

        _model.HistoryPrevious();
        Assert.Equal("echo one", _model.Input);

        _model.HistoryPrevious();
        Assert.Equal("echo one", _model.Input);

        _model.HistoryNext();
        Assert.Equal("echo two", _model.Input);

        // Walking past the newest entry gives back the draft, not an empty line.
        _model.HistoryNext();
        Assert.Equal("half typ", _model.Input);
    }

    [Fact]
    public void CompleteExtendsToTheCommonPrefixAndFinishesAnUnambiguousMatch()
    {
        CVar.Int("r_fog_quality", 1, registry: _variables);
        CVar.Int("r_fog_density", 1, registry: _variables);

        _model.SetInput("r_f");
        _model.Complete();
        Assert.Equal("r_fog_", _model.Input);

        _model.Insert("q");
        _model.Complete();
        Assert.Equal("r_fog_quality ", _model.Input);
        Assert.Equal(_model.Input.Length, _model.CaretIndex);
    }

    [Fact]
    public void SuggestionsTrackWhatIsTyped()
    {
        CVar.Int("r_fog_quality", 1, registry: _variables);
        CVar.Int("r_shadows", 1, registry: _variables);

        _model.SetInput("r_");
        Assert.Equal(2, _model.Suggestions.Count);

        _model.Insert("s");
        Assert.Equal(["r_shadows"], _model.Suggestions);

        _model.SetInput(string.Empty);
        Assert.Empty(_model.Suggestions);
    }

    [Fact]
    public void OutputAppearsInTheScrollbackAndIsTrimmedWhenItOverflows()
    {
        for (int i = 0; i < 400; i++)
        {
            _console.Write($"line {i}");
        }

        Assert.InRange(_model.Lines.Count, 1, 256);
        Assert.Equal("line 399", _model.Lines[^1].Text);
    }

    [Fact]
    public void ScrollIsClampedToWhatThereIsToRead()
    {
        for (int i = 0; i < 20; i++)
        {
            _console.Write($"line {i}");
        }

        _model.Scroll(100, visibleLines: 10);
        Assert.Equal(10, _model.ScrollOffset);

        _model.Scroll(-100, visibleLines: 10);
        Assert.Equal(0, _model.ScrollOffset);
    }

    [Fact]
    public void SubmittingReturnsToTheBottomOfTheScrollback()
    {
        for (int i = 0; i < 20; i++)
        {
            _console.Write($"line {i}");
        }

        _model.Scroll(5, visibleLines: 10);
        _model.SetInput("echo hi");
        _model.Submit();

        Assert.Equal(0, _model.ScrollOffset);
    }
}
