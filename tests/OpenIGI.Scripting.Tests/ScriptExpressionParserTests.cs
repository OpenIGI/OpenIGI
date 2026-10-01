using OpenIGI.Scripting.Expressions;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class ScriptExpressionParserTests
{
    [Theory]
    // Arithmetic, in C's order.
    [InlineData("1 + 2 * 3", "(1 + (2 * 3))")]
    [InlineData("1 * 2 + 3", "((1 * 2) + 3)")]
    [InlineData("1 - 2 - 3", "((1 - 2) - 3)")]
    [InlineData("1 + 2 << 3", "((1 + 2) << 3)")]
    [InlineData("1 << 2 + 3", "(1 << (2 + 3))")]
    // Comparison binds looser than shifts, equality looser than comparison.
    [InlineData("a < b == c > d", "((a < b) == (c > d))")]
    // The bitwise tier, and its order relative to the logical one.
    [InlineData("a & b | c ^ d", "((a & b) | (c ^ d))")]
    [InlineData("a & b && c | d", "((a & b) && (c | d))")]
    [InlineData("a && b || c && d", "((a && b) || (c && d))")]
    // Assignment is right-associative and looser than everything but the comma.
    [InlineData("a = b = c", "(a = (b = c))")]
    [InlineData("a = b || c", "(a = (b || c))")]
    [InlineData("a = b, c", "((a = b) , c)")]
    public void PrecedenceAndAssociativityFollowC(string source, string expected) =>
        Assert.Equal(expected, ScriptExpressionParser.Parse(source).ToString());

    [Theory]
    [InlineData("-a * b", "((-a) * b)")]
    [InlineData("-a + b", "((-a) + b)")]
    [InlineData("!a && b", "((!a) && b)")]
    [InlineData("~a | b", "((~a) | b)")]
    [InlineData("- -a", "(-(-a))")]
    [InlineData("!!a", "(!(!a))")]
    public void PrefixPlusAndMinusBindTighterThanTheirBinaryForms(string source, string expected)
    {
        // The tokenizer emits one code for '-'; the parser rewrites it in prefix position, and the
        // rewritten operator binds at rank 2 rather than 4. Miss that and "-a * b" parses as "-(a * b)".
        Assert.Equal(expected, ScriptExpressionParser.Parse(source).ToString());
    }

    [Theory]
    [InlineData("a ? b : c", "(a ? b : c)")]
    [InlineData("a ? b : c ? d : e", "(a ? b : (c ? d : e))")]
    [InlineData("a || b ? c : d", "((a || b) ? c : d)")]
    [InlineData("a = b ? c : d", "(a = (b ? c : d))")]
    public void TernaryIsRightAssociativeAndSitsBetweenLogicalOrAndAssignment(string source, string expected) =>
        Assert.Equal(expected, ScriptExpressionParser.Parse(source).ToString());

    [Fact]
    public void AdjacentStringLiteralsConcatenate()
    {
        ScriptExpression expression = ScriptExpressionParser.Parse("\"Hello, \" \"world\"");

        Assert.Equal(ScriptExpressionKind.String, expression.Kind);
        Assert.Equal("Hello, world", expression.Text);
    }

    [Fact]
    public void CallsParseTheirArgumentsWithTheCommaSuppressed()
    {
        ScriptExpression expression = ScriptExpressionParser.Parse("f(a, b + c, g(d))");

        Assert.True(expression.IsCall);
        Assert.Equal("f", expression.Text);
        Assert.Equal(3, expression.Arguments.Count);
        Assert.Equal("a", expression.Arguments[0].ToString());
        Assert.Equal("(b + c)", expression.Arguments[1].ToString());
        Assert.Equal("g(d)", expression.Arguments[2].ToString());
    }

    [Fact]
    public void ACallWithNoArgumentsParses()
    {
        ScriptExpression expression = ScriptExpressionParser.Parse("Config_IsGermany()");

        Assert.True(expression.IsCall);
        Assert.Empty(expression.Arguments);
    }

    [Fact]
    public void ABareNameIsNotACall()
    {
        ScriptExpression expression = ScriptExpressionParser.Parse("this.nUserData");

        Assert.False(expression.IsCall);
        Assert.True(expression.IsAssignable);
        Assert.Equal("this.nUserData", expression.Text);
    }

    [Fact]
    public void DottedMemberPathsAreOneIdentifier()
    {
        // The original scans a name in one pass, so `MenuScreen_905.nUserData` is a single token
        // rather than a member-access operator applied to two names.
        ScriptExpression expression = ScriptExpressionParser.Parse("MenuScreen_905.nUserData = 1");

        Assert.Equal(ScriptOperator.Assign, expression.Operator);
        Assert.Equal("MenuScreen_905.nUserData", expression.Left!.Text);
    }

    [Fact]
    public void ConstantsAreResolvedByTheLexerNotTheParser()
    {
        ScriptExpression expression = ScriptExpressionParser.Parse(
            "TRUE",
            name => name == "TRUE" ? 1 : null);

        Assert.Equal(ScriptExpressionKind.Constant, expression.Kind);
        Assert.Equal(1, expression.Constant);
        Assert.False(expression.IsAssignable);
    }

    [Fact]
    public void AssigningToSomethingThatIsNotANameIsRejected()
    {
        // "Expression: Cannot assign to constant" / "Binary operator requires lvalue" in the original.
        ScriptExpressionException ex = Assert.Throws<ScriptExpressionException>(
            () => ScriptExpressionParser.Parse("1 = 2"));

        Assert.Contains("lvalue", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(a + b")]
    [InlineData("a ? b")]
    [InlineData("f(a, b")]
    [InlineData("a +")]
    [InlineData("* a")]
    public void MalformedInputThrowsRatherThanReturningAPartialTree(string source) =>
        Assert.Throws<ScriptExpressionException>(() => ScriptExpressionParser.Parse(source));

    [Theory]
    [InlineData("0x10", 16.0)]
    [InlineData("0xFF", 255.0)]
    [InlineData("42", 42.0)]
    [InlineData("1.5", 1.5)]
    [InlineData("1e3", 1000.0)]
    public void NumbersParseAsDoubles(string source, double expected)
    {
        ScriptExpression expression = ScriptExpressionParser.Parse(source);

        Assert.Equal(ScriptExpressionKind.Number, expression.Kind);
        Assert.Equal(expected, expression.Number);
    }

    [Theory]
    // Every one of these is a verbatim action or condition string from mainmenu.qvm.
    [InlineData("MenuManager_PushScreen(914)")]
    [InlineData("MenuManager_ActivatePopupScreen(934)")]
    [InlineData("MenuManager_RequestScreen(912, FALSE)")]
    [InlineData("MenuManager_PopScreen(FALSE)")]
    [InlineData("!LevelFlow_IsCountryUSA()")]
    [InlineData("!Config_IsGermany()")]
    [InlineData("this.nUserData = Config_GraphicOptionsGetPerformanceFlags()")]
    [InlineData("Config_GraphicOptionsSetPerformanceFlags(this.nUserData)")]
    [InlineData("Config_FillScreenResolutionListBox(ListBox_951.nActiveID)")]
    [InlineData("Config_GraphicOptionsSetResolution(this.nActiveID)")]
    [InlineData("MenuScreen_905.nUserData = Config_GraphicOptionsGetPerfFlagsFromLevel(this.nValue)")]
    [InlineData("Config_GraphicOptionsGetPerfLevelFromFlags(MenuScreen_905.nUserData)")]
    [InlineData("MenuManager_RequestScreen(904, FALSE) && Game_UpdateVolume()")]
    [InlineData("Config_IsContentControlPasswordEnabled() ? MenuManager_ActivatePopupScreen(932) : MenuManager_PushScreen(915)")]
    [InlineData("ControlsMenu_ConvertFloatFromOffset(ControlsMenu_954.nOffset)")]
    public void RetailMenuExpressionsParse(string source)
    {
        ScriptExpression expression = ScriptExpressionParser.Parse(
            source,
            name => name switch { "TRUE" => 1, "FALSE" => 0, _ => null });

        Assert.NotNull(expression);
    }

    [Fact]
    public void SequencingWithLogicalAndKeepsBothCallsInOrder()
    {
        // The retail sound screen uses && to sequence two calls, so the tree must keep the left call
        // on the left rather than folding it into a condition.
        ScriptExpression expression = ScriptExpressionParser.Parse(
            "MenuManager_RequestScreen(904, FALSE) && Game_UpdateVolume()",
            name => name == "FALSE" ? 0 : null);

        Assert.Equal(ScriptOperator.LogicalAnd, expression.Operator);
        Assert.Equal("MenuManager_RequestScreen", expression.Left!.Text);
        Assert.Equal("Game_UpdateVolume", expression.Right!.Text);
    }

    [Fact]
    public void TheConditionalActionFromTheContentControlEntryHasTheRightShape()
    {
        ScriptExpression expression = ScriptExpressionParser.Parse(
            "Config_IsContentControlPasswordEnabled() ? MenuManager_ActivatePopupScreen(932) : MenuManager_PushScreen(915)");

        Assert.Equal(ScriptExpressionKind.Conditional, expression.Kind);
        Assert.Equal("Config_IsContentControlPasswordEnabled", expression.Left!.Text);
        Assert.Equal("MenuManager_ActivatePopupScreen", expression.Right!.Text);
        Assert.Equal("MenuManager_PushScreen", expression.Extra!.Text);
    }

    [Fact]
    public void ParenthesesProduceAGroupNodeThatPreservesShape()
    {
        ScriptExpression expression = ScriptExpressionParser.Parse("(1 + 2) * 3");

        Assert.Equal("(((1 + 2)) * 3)", expression.ToString());
    }

    [Fact]
    public void ATrailingSemicolonIsAccepted()
    {
        // Menu action strings are written as statements and some carry their terminator.
        Assert.NotNull(ScriptExpressionParser.Parse("MenuManager_PopScreen(0);"));
    }

    [Fact]
    public void ACharacterTheOriginalCannotReadEndsTheExpression()
    {
        // Mission 10 ships a container condition ending in two stray backticks. The original's word
        // reader takes nothing from them, so its lexer reports no token and the parser stops there with
        // the expression complete; rejecting it made the container run from the first tick.
        ScriptExpression expression = ScriptExpressionParser.Parse("(a && b)\n|| this.isRun\n``\n");

        Assert.Equal("(((a && b)) || this.isRun)", expression.ToString());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("`", true)]
    [InlineData(" `` ", true)]
    [InlineData("a", false)]
    [InlineData("MenuManager_PopScreen(0)", false)]
    public void ASourceTheOriginalCannotReadAnyOfIsBlank(string source, bool blank)
    {
        // A lone backtick is the original's empty string: its lexer reads nothing and its parser returns no
        // expression and no error. The retail main menu carries one.
        Assert.Equal(blank, ScriptExpressionParser.IsBlank(source));
    }
}
