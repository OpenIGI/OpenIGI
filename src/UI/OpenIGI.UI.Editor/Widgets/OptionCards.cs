using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Editor.Theme;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Widgets;

/// <summary>One choice of an <see cref="OptionCards"/>: a title, what choosing it means, and a note beside the title.</summary>
/// <param name="Title">The choice's name.</param>
/// <param name="Description">What it means, a line under the title.</param>
/// <param name="Note">A short tag beside the title (<c>running</c>, <c>not installed</c>), or empty.</param>
/// <param name="IsEnabled">Whether it can be chosen.</param>
public sealed record OptionCard(string Title, string Description, string Note = "", bool IsEnabled = true);

/// <summary>
/// A choice made by picking one of a few cards, each saying what it means: for decisions a wizard asks for, where a
/// dropdown would hide the consequences.
/// </summary>
public sealed class OptionCards : EditorElement
{
    private IReadOnlyList<OptionCard> _cards = [];
    private int _hovered = -1;
    private float _scroll;

    /// <summary>Initializes a new instance of the <see cref="OptionCards"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public OptionCards(EditorTheme theme)
        : base(theme)
    {
    }

    /// <summary>Raised when the user picks a card.</summary>
    public event Action<int>? SelectionChanged;

    /// <summary>Gets or sets the cards.</summary>
    public IReadOnlyList<OptionCard> Cards
    {
        get => _cards;
        set
        {
            _cards = value ?? [];
            SelectedIndex = Math.Min(SelectedIndex, _cards.Count - 1);
        }
    }

    /// <summary>Gets or sets the picked card, or -1.</summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>Gets or sets whether the cards sit side by side rather than one above another.</summary>
    public bool Horizontal { get; set; }

    /// <inheritdoc/>
    public override bool Focusable => true;

    private float CardHeight => Theme.ControlHeight + Theme.RowHeight + Theme.Px(18f);

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

    /// <inheritdoc/>
    protected internal override bool OnPointerDown(in PointerEvent e)
    {
        int index = CardAt(e.Position);
        if (e.Button == MouseButton.Left && index >= 0 && _cards[index].IsEnabled)
        {
            SelectedIndex = index;
            SelectionChanged?.Invoke(index);
        }

        return true;
    }

    /// <inheritdoc/>
    protected internal override void OnPointerMove(in PointerEvent e) => _hovered = CardAt(e.Position);

    /// <inheritdoc/>
    protected internal override bool OnWheel(UiVec2 position, float notches, KeyModifiers modifiers)
    {
        float most = MathF.Max(0f, (_cards.Count * (CardHeight + Theme.Px(6f))) - Bounds.Height);
        _scroll = Math.Clamp(_scroll - (notches * CardHeight), 0f, most);
        return !Horizontal;
    }

    /// <inheritdoc/>
    protected internal override bool OnKeyDown(in KeyEvent e)
    {
        int step = e.Key switch
        {
            KeyCode.Down or KeyCode.Right => 1,
            KeyCode.Up or KeyCode.Left => -1,
            _ => 0,
        };
        if (step == 0 || _cards.Count == 0)
        {
            return false;
        }

        int next = SelectedIndex;
        for (int i = 0; i < _cards.Count; i++)
        {
            next = Math.Clamp(next + step, 0, _cards.Count - 1);
            if (_cards[next].IsEnabled)
            {
                break;
            }
        }

        if (next != SelectedIndex && _cards[next].IsEnabled)
        {
            SelectedIndex = next;
            SelectionChanged?.Invoke(next);
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        painter.PushClip(Bounds);
        for (int i = 0; i < _cards.Count; i++)
        {
            UiRect card = CardRect(i);
            if (card.Bottom < Bounds.Y || card.Y > Bounds.Bottom)
            {
                continue;
            }

            OptionCard option = _cards[i];
            bool picked = i == SelectedIndex;
            Bgra32 fill = picked ? Theme.Accent.WithAlpha(40) : i == _hovered && option.IsEnabled ? Theme.Hover : Theme.Input;
            EditorDraw.Frame(painter, Theme, card, fill, picked ? Theme.Accent : Theme.Border);

            // A radio mark, then the title, its note, and what it means.
            float pad = Theme.Px(12f);
            UiVec2 mark = new(card.X + pad + Theme.Px(6f), card.Y + pad + (Theme.ControlHeight * 0.5f) - Theme.Px(2f));
            painter.FillDot(mark, Theme.Px(6f), option.IsEnabled ? Theme.TextMuted : Theme.TextDisabled);
            painter.FillDot(mark, Theme.Px(4.5f), fill);
            if (picked)
            {
                painter.FillDot(mark, Theme.Px(3f), Theme.Accent);
            }

            float textX = mark.X + Theme.Px(16f);
            UiRect title = new(textX, card.Y + pad - Theme.Px(2f), card.Right - textX - pad, Theme.ControlHeight);
            float width = EditorDraw.Label(painter, Theme, option.Title, title, option.IsEnabled ? Theme.Text : Theme.TextDisabled, strong: true);
            if (option.Note.Length > 0)
            {
                EditorDraw.Label(painter, Theme, option.Note, title with { X = title.X + width + Theme.Px(10f) }, picked ? Theme.Accent : Theme.TextMuted);
            }

            UiRect description = new(textX, title.Bottom - Theme.Px(2f), card.Right - textX - pad, Theme.RowHeight);
            EditorDraw.Label(painter, Theme, option.Description, description, option.IsEnabled ? Theme.TextMuted : Theme.TextDisabled);
        }

        painter.PopClip();
    }

    private UiRect CardRect(int index)
    {
        float gap = Theme.Px(6f);
        if (Horizontal)
        {
            float width = (Bounds.Width - (gap * Math.Max(0, _cards.Count - 1))) / Math.Max(1, _cards.Count);
            return new UiRect(Bounds.X + (index * (width + gap)), Bounds.Y, width, CardHeight);
        }

        return new UiRect(Bounds.X, Bounds.Y + (index * (CardHeight + gap)) - _scroll, Bounds.Width, CardHeight);
    }

    private int CardAt(UiVec2 point)
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            if (CardRect(i).Contains(point) && Bounds.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }
}
