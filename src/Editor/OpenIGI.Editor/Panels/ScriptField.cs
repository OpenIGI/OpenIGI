using OpenIGI.Scripting.Qsc;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Editor.Widgets;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// A parameter that holds script to run (<c>Run at start</c>: <c>MenuManager_SetEnabled(FALSE) &amp;&amp; …</c>), edited as code:
/// highlighted, several lines tall, checked as it is typed, and written only once it is script the game can run.
/// </summary>
internal sealed class ScriptField : EditorElement
{
    private readonly Func<string> _get;
    private readonly Action<string> _set;
    private readonly CodeEditor _code;
    private string _shown = string.Empty;
    private string? _error;

    /// <summary>Initializes a new instance of the <see cref="ScriptField"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="get">Reads the script.</param>
    /// <param name="set">Writes it.</param>
    public ScriptField(EditorTheme theme, Func<string> get, Action<string> set)
        : base(theme)
    {
        _get = get;
        _set = set;
        _code = new CodeEditor(theme) { Highlight = line => QscHighlighter.Line(theme, line) };
        _code.TextChanged += Commit;
        AddChild(_code);
        Refresh();
    }

    /// <summary>Gets how many grid rows it wants: its lines, a little room, and a line for an error.</summary>
    public int Lines => Math.Clamp(_code.LineCount, 2, 8) + 1;

    /// <summary>Gets whether a parameter's text is script to run rather than a name or a path: it calls something.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it should be edited as code.</returns>
    public static bool HoldsScript(string text) => text.Contains('(') && text.Contains(')');

    /// <summary>Pulls the script in again when it changed from outside (an undo), leaving it alone while being typed in.</summary>
    public void Refresh()
    {
        string text = _get();
        if (text != _shown && !_code.IsFocusWithin)
        {
            _shown = text;
            _code.Text = text;
            _error = null;
            _code.ErrorLine = null;
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect) =>
        _code.Place(context, contentRect with { Height = MathF.Max(0f, contentRect.Height - (Theme.RowHeight * 0.9f)) });

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        UiRect status = new(Bounds.X + Theme.Px(2f), Bounds.Bottom - Theme.RowHeight, Bounds.Width, Theme.RowHeight);
        if (_error is not null)
        {
            EditorDraw.Label(painter, Theme, $"Not saved: {_error}", status, Theme.Error);
        }
    }

    private void Commit()
    {
        string text = _code.Text;
        try
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                QscParser.ParseExpression(text);
            }

            _error = null;
            _code.ErrorLine = null;
            _shown = text;
            _set(text);
        }
        catch (QscParseException error)
        {
            _error = error.Message;
            _code.ErrorLine = 0;
        }
    }
}
