using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>A wizard page's fields, each with its label to its left.</summary>
internal sealed class WizardForm : EditorElement
{
    private readonly (string Label, EditorElement Field)[] _rows;

    /// <summary>Initializes a new instance of the <see cref="WizardForm"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="rows">The fields and their labels, top to bottom.</param>
    public WizardForm(EditorTheme theme, (string Label, EditorElement Field)[] rows)
        : base(theme)
    {
        _rows = rows;
        foreach ((_, EditorElement field) in rows)
        {
            AddChild(field);
        }
    }

    private float LabelWidth => Theme.Px(110f);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        float y = contentRect.Y + Theme.Px(6f);
        foreach ((_, EditorElement field) in _rows)
        {
            field.Place(context, new UiRect(contentRect.X + LabelWidth, y, MathF.Min(Theme.Px(420f), contentRect.Width - LabelWidth), Theme.ControlHeight));
            y += Theme.ControlHeight + Theme.Px(10f);
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        foreach ((string label, EditorElement field) in _rows)
        {
            EditorDraw.Label(painter, Theme, label, new UiRect(Bounds.X, field.Bounds.Y, LabelWidth, Theme.ControlHeight), Theme.TextMuted);
        }
    }
}

/// <summary>A wizard's summary of what it will do, a line per fact, warnings marked.</summary>
/// <param name="theme">The theme.</param>
internal sealed class WizardReview(EditorTheme theme) : EditorElement(theme)
{
    /// <summary>Gets or sets the lines: a label, its value, and whether it warns.</summary>
    public IReadOnlyList<(string Label, string Value, bool Warning)> Lines { get; set; } = [];

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        EditorDraw.Frame(painter, Theme, Bounds, Theme.Input, Theme.Border);
        float pad = Theme.Px(12f);
        float y = Bounds.Y + pad;
        foreach ((string label, string value, bool warning) in Lines)
        {
            if (y + Theme.RowHeight > Bounds.Bottom - pad)
            {
                break;
            }

            EditorDraw.Label(painter, Theme, label, new UiRect(Bounds.X + pad, y, Theme.Px(100f), Theme.RowHeight), Theme.TextMuted);
            EditorDraw.Label(painter, Theme, value, new UiRect(Bounds.X + pad + Theme.Px(100f), y, Bounds.Width - (pad * 2f) - Theme.Px(100f), Theme.RowHeight), warning ? Theme.Warning : Theme.Text);
            y += Theme.RowHeight + Theme.Px(6f);
        }
    }
}
