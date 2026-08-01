using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Pins the console grammar: statement separation, quoting and comments.
/// </summary>
/// <remarks>
/// The settings file is executed through the same grammar, so a mistake here corrupts saved settings
/// as readily as it breaks a typed command.
/// </remarks>
public sealed class CommandTokenizerTests
{
    [Fact]
    public void SplitStatements_SeparatesOnSemicolons()
    {
        IReadOnlyList<string> statements = CommandTokenizer.SplitStatements("r_vsync 0; r_scale 0.75 ;  ");

        Assert.Equal(["r_vsync 0", "r_scale 0.75"], statements);
    }

    [Fact]
    public void SplitStatements_KeepsSeparatorsInsideQuotes()
    {
        // This is what makes a binding able to carry a multi-statement script.
        IReadOnlyList<string> statements =
            CommandTokenizer.SplitStatements("bind f5 \"r_fog 0; echo fog off\"");

        Assert.Equal("bind f5 \"r_fog 0; echo fog off\"", Assert.Single(statements));
    }

    [Fact]
    public void SplitStatements_StopsAtAComment()
    {
        IReadOnlyList<string> statements = CommandTokenizer.SplitStatements("r_vsync 0 // tearing is fine; really");

        Assert.Equal(["r_vsync 0"], statements);
    }

    [Fact]
    public void SplitStatements_DoesNotTreatASlashInsideQuotesAsAComment()
    {
        IReadOnlyList<string> statements = CommandTokenizer.SplitStatements("exec \"cfg//odd.cfg\"");

        Assert.Equal(["exec \"cfg//odd.cfg\""], statements);
    }

    [Fact]
    public void Tokenize_SplitsOnWhitespaceAndStripsQuotes()
    {
        IReadOnlyList<string> tokens = CommandTokenizer.Tokenize("bind f5   \"exec quick.cfg\"");

        Assert.Equal(["bind", "f5", "exec quick.cfg"], tokens);
    }

    [Fact]
    public void Tokenize_PreservesAnEmptyQuotedToken()
    {
        // Clearing a text variable depends on this: `ui_profile ""` must assign an empty string
        // rather than being read as the variable with no argument, which would print it instead.
        IReadOnlyList<string> tokens = CommandTokenizer.Tokenize("ui_profile \"\"");

        Assert.Equal(["ui_profile", string.Empty], tokens);
    }

    [Fact]
    public void Tokenize_TakesTheRestOfTheLineWhenAQuoteIsNeverClosed()
    {
        IReadOnlyList<string> tokens = CommandTokenizer.Tokenize("echo \"half typed");

        Assert.Equal(["echo", "half typed"], tokens);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("c:/games/igi", "\"c:/games/igi\"")]
    public void Quote_ProducesTextThatTokenizesBackUnchanged(string value, string expected)
    {
        Assert.Equal(expected, CommandTokenizer.Quote(value));

        IReadOnlyList<string> tokens = CommandTokenizer.Tokenize("x " + expected);
        Assert.Equal(value, tokens[1]);
    }
}
