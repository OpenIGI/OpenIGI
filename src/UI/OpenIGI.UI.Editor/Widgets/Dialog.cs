using OpenIGI.Core.Graphics;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>
/// A window in the middle of the editor for one task, such as making a mission: a title, a close button, and the task's
/// own content, over a dimmed editor that does not take presses while it is open. Escape or the close button dismiss it.
/// </summary>
public sealed class Dialog : Popup
{
    private readonly EditorElement _content;
    private readonly Button _close;
    private readonly string _title;
    private readonly UiVec2 _size;

    private Dialog(EditorTheme theme, string title, EditorElement content, UiVec2 size)
        : base(theme)
    {
        _title = title;
        _content = content;
        _size = size;
        _close = new Button(theme, string.Empty, KitIcon.Close) { Style = ButtonStyle.Flat, Tooltip = "Close (Esc)" };
        _close.Clicked += Close;
        AddChild(_content);
        AddChild(_close);
    }

    /// <inheritdoc/>
    public override bool IsModal => true;

    /// <summary>Opens a dialog in the middle of the window.</summary>
    /// <param name="root">The window.</param>
    /// <param name="title">The dialog's title.</param>
    /// <param name="content">What it holds.</param>
    /// <param name="width">Its width, in unscaled pixels.</param>
    /// <param name="height">Its height, in unscaled pixels, title included.</param>
    /// <returns>The dialog.</returns>
    public static Dialog Open(EditorRoot root, string title, EditorElement content, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(content);
        EditorTheme theme = root.Theme;
        UiVec2 size = new(
            MathF.Min(theme.Px(width), root.Bounds.Width - theme.Px(40f)),
            MathF.Min(theme.Px(height), root.Bounds.Height - theme.Px(40f)));
        Dialog dialog = new(theme, title, content, size)
        {
            Anchor = new UiRect(
                MathF.Round(root.Bounds.Center.X - (size.X * 0.5f)),
                MathF.Round(root.Bounds.Center.Y - (size.Y * 0.5f)),
                size.X,
                0f),
            Placement = PopupPlacement.Below,
        };
        root.CloseAllPopups();
        root.ShowPopup(dialog);
        return dialog;
    }

    /// <summary>Asks a question with a few answers, as buttons along the dialog's foot; any answer closes it.</summary>
    /// <param name="root">The window.</param>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The question.</param>
    /// <param name="answers">The answers, left to right; the last is the one Enter would mean, drawn as the primary one.</param>
    /// <returns>The dialog.</returns>
    public static Dialog Ask(EditorRoot root, string title, string message, params (string Label, Action Chosen)[] answers)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(answers);
        Dialog? dialog = null;
        Question question = new(root.Theme, message, answers, () => dialog?.Close());
        dialog = Open(root, title, question, 460f, 150f);
        return dialog;
    }

    private float TitleHeight => Theme.ControlHeight + Theme.Px(12f);

    // A message and a row of answers.
    private sealed class Question : EditorElement
    {
        private readonly string _message;
        private readonly Button[] _answers;

        public Question(EditorTheme theme, string message, (string Label, Action Chosen)[] answers, Action close)
            : base(theme)
        {
            _message = message;
            _answers = [.. answers.Select((answer, index) =>
            {
                Button button = new(theme, answer.Label) { Style = index == answers.Length - 1 ? ButtonStyle.Primary : ButtonStyle.Standard };
                button.Clicked += () =>
                {
                    close();
                    answer.Chosen();
                };
                AddChild(button);
                return button;
            })];
        }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
        {
            float pad = Theme.Px(12f);
            float x = contentRect.Right - pad;
            for (int i = _answers.Length - 1; i >= 0; i--)
            {
                float width = MathF.Max(Theme.Px(90f), Theme.Font.MeasureWidth(_answers[i].Text, Theme.TextSize) + Theme.Px(28f));
                x -= width;
                _answers[i].Place(context, new UiRect(x, contentRect.Bottom - pad - Theme.ControlHeight, width, Theme.ControlHeight));
                x -= Theme.Px(8f);
            }
        }

        protected override void OnDraw(UiPainter painter)
        {
            float pad = Theme.Px(12f);
            EditorDraw.Label(painter, Theme, _message, new UiRect(Bounds.X + pad, Bounds.Y + pad, Bounds.Width - (pad * 2f), Theme.RowHeight), Theme.TextMuted);
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => _size;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float button = Theme.ControlHeight;
        _close.Place(context, new UiRect(contentRect.Right - button - Theme.Px(6f), contentRect.Y + ((TitleHeight - button) * 0.5f), button, button));
        _content.Place(context, UiRect.FromEdges(contentRect.X, contentRect.Y + TitleHeight, contentRect.Right, contentRect.Bottom));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        // The editor behind is dimmed: it does not take presses while the dialog is open.
        if (Root is { } root)
        {
            painter.FillRect(root.Bounds, Bgra32.FromRgba(0, 0, 0, 110));
        }

        base.OnDraw(painter);
        UiRect title = new(Bounds.X + Theme.Px(12f), Bounds.Y, Bounds.Width - Theme.Px(48f), TitleHeight);
        EditorDraw.Label(painter, Theme, _title, title, Theme.Text, strong: true);
        painter.FillRect(new UiRect(Bounds.X, Bounds.Y + TitleHeight - Theme.Hairline, Bounds.Width, Theme.Hairline), Theme.Border);
    }
}
