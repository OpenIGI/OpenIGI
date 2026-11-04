using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>One step of a <see cref="Wizard"/>.</summary>
/// <param name="Step">Its name in the step strip (<c>Game</c>, <c>Review</c>).</param>
/// <param name="Title">The heading over it.</param>
/// <param name="Subtitle">A line saying what the step decides.</param>
/// <param name="Content">What it holds.</param>
public sealed record WizardPage(string Step, string Title, string Subtitle, EditorElement Content)
{
    /// <summary>Gets why the wizard cannot go on from this step, or null when it can.</summary>
    public Func<string?> Problem { get; init; } = static () => null;

    /// <summary>Gets what the Next button says on this step, or null for the wizard's own.</summary>
    public Func<string?> NextText { get; init; } = static () => null;

    /// <summary>
    /// Gets what Next may do instead of going on: a step that sometimes leaves the wizard (to switch games) returns whether it
    /// did, and when it did not, Next goes on as usual. Null always goes on.
    /// </summary>
    public Func<bool>? Leave { get; init; }

    /// <summary>Gets what runs each time the step is shown.</summary>
    public Action? Entered { get; init; }

    /// <summary>Gets whether the step does not apply to what was chosen so far: Next and Back pass it, the strip leaves it out.</summary>
    public Func<bool> Skipped { get; init; } = static () => false;
}

/// <summary>
/// A task taken one decision at a time: a strip of steps, the current one's heading and what it decides, its content, and
/// Back and Next along the foot, Next held with the reason shown while the step is not done, the last step's Next doing
/// the task. For a dialog's content (<see cref="Dialog.Open"/>).
/// </summary>
public sealed class Wizard : EditorElement
{
    private readonly IReadOnlyList<WizardPage> _pages;
    private readonly Func<bool> _finish;
    private readonly string _finishText;
    private readonly Button _back;
    private readonly Button _next;
    private int _current = -1;

    /// <summary>Initializes a new instance of the <see cref="Wizard"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="pages">The steps, in order.</param>
    /// <param name="finishText">What the last step's button says (<c>Create Mission</c>).</param>
    /// <param name="finish">Does the task; whether it was done, which closes the wizard's dialog.</param>
    public Wizard(EditorTheme theme, IReadOnlyList<WizardPage> pages, string finishText, Func<bool> finish)
        : base(theme)
    {
        ArgumentNullException.ThrowIfNull(pages);
        _pages = pages;
        _finish = finish;
        _finishText = finishText;
        _back = new Button(theme, "Back", KitIcon.ArrowLeft);
        _back.Clicked += () => Show(Step(-1));
        _next = new Button(theme, "Next", KitIcon.ArrowRight) { Style = ButtonStyle.Primary };
        _next.Clicked += Next;
        foreach (WizardPage page in pages)
        {
            AddChild(page.Content);
        }

        AddChild(_back);
        AddChild(_next);
        Show(0);
    }

    /// <summary>Raised once the task is done.</summary>
    public event Action? Finished;

    /// <summary>Gets the step shown.</summary>
    public int Current => _current;

    private WizardPage Page => _pages[_current];

    private bool IsLast => Step(1) == _current;

    // The nearest step that applies, before or after the current one; the current one when there is none.
    private int Step(int direction)
    {
        for (int i = _current + direction; i >= 0 && i < _pages.Count; i += direction)
        {
            if (!_pages[i].Skipped())
            {
                return i;
            }
        }

        return _current;
    }

    private float Header => Theme.Px(30f) + (Theme.RowHeight * 2f) + Theme.Px(14f);

    private float Foot => Theme.ControlHeight + Theme.Px(24f);

    /// <summary>Shows a step.</summary>
    /// <param name="index">The step.</param>
    public void Show(int index)
    {
        index = Math.Clamp(index, 0, _pages.Count - 1);
        if (index == _current)
        {
            return;
        }

        _current = index;
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Content.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }

        Page.Entered?.Invoke();
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        string? problem = Page.Problem();
        _next.IsEnabled = problem is null;
        _next.Text = Page.NextText() ?? (IsLast ? _finishText : "Next");
        _back.Visibility = Step(-1) != _current ? Visibility.Visible : Visibility.Hidden;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float pad = Theme.Px(18f);
        UiRect body = UiRect.FromEdges(contentRect.X + pad, contentRect.Y + Header, contentRect.Right - pad, contentRect.Bottom - Foot - Theme.Px(6f));
        foreach (WizardPage page in _pages)
        {
            page.Content.Place(context, page.Content.Visibility == Visibility.Visible ? body : default);
        }

        float y = contentRect.Bottom - Foot + ((Foot - Theme.ControlHeight) * 0.5f);
        float nextWidth = MathF.Max(Theme.Px(110f), Theme.Font.MeasureWidth(_next.Text, Theme.TextSize) + Theme.Px(44f));
        _next.Place(context, new UiRect(contentRect.Right - pad - nextWidth, y, nextWidth, Theme.ControlHeight));
        _back.Place(context, new UiRect(contentRect.Right - pad - nextWidth - Theme.Px(8f) - Theme.Px(96f), y, Theme.Px(96f), Theme.ControlHeight));
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float pad = Theme.Px(18f);

        // The steps, done ones and the current one lit.
        float x = Bounds.X + pad;
        float stripY = Bounds.Y + Theme.Px(10f);
        int shown = 0;
        int lastShown = Enumerable.Range(0, _pages.Count).LastOrDefault(i => !_pages[i].Skipped());
        for (int i = 0; i < _pages.Count; i++)
        {
            if (_pages[i].Skipped())
            {
                continue;
            }

            string label = $"{++shown}  {_pages[i].Step}";
            float width = EditorDraw.Label(painter, Theme, label, new UiRect(x, stripY, Theme.Px(200f), Theme.RowHeight), i == _current ? Theme.Accent : i < _current ? Theme.Text : Theme.TextDisabled, strong: i == _current);
            x += width + Theme.Px(10f);
            if (i < lastShown)
            {
                EditorDraw.Label(painter, Theme, "›", new UiRect(x, stripY, Theme.Px(12f), Theme.RowHeight), Theme.TextDisabled);
                x += Theme.Px(18f);
            }
        }

        // The step's heading and what it decides.
        float headY = stripY + Theme.RowHeight + Theme.Px(8f);
        EditorDraw.Label(painter, Theme, Page.Title, new UiRect(Bounds.X + pad, headY, Bounds.Width - (pad * 2f), Theme.Px(24f)), Theme.Text, strong: true);
        EditorDraw.Label(painter, Theme, Page.Subtitle, new UiRect(Bounds.X + pad, headY + Theme.Px(24f), Bounds.Width - (pad * 2f), Theme.RowHeight), Theme.TextMuted);

        // The foot: a rule, and why Next is held.
        float footY = Bounds.Bottom - Foot;
        painter.FillRect(new UiRect(Bounds.X, footY, Bounds.Width, Theme.Hairline), Theme.Border);
        if (Page.Problem() is { } problem)
        {
            float room = Bounds.Width - (pad * 2f) - Theme.Px(230f);
            EditorDraw.Label(painter, Theme, problem, new UiRect(Bounds.X + pad, footY + ((Foot - Theme.RowHeight) * 0.5f), room, Theme.RowHeight), Theme.Warning);
        }
    }

    private void Next()
    {
        if (Page.Problem() is not null)
        {
            return;
        }

        if (Page.Leave is { } leave && leave())
        {
            Finished?.Invoke();
            return;
        }

        if (!IsLast)
        {
            Show(Step(1));
            return;
        }

        if (_finish())
        {
            Finished?.Invoke();
        }
    }
}
