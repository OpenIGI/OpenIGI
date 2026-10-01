using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>One footer hint: a key and what it does.</summary>
/// <param name="Key">The key's name, as shown on its cap.</param>
/// <param name="Label">What the key does.</param>
public readonly record struct KeyHint(string Key, string Label);

/// <summary>The footer's row of key hints, right-aligned: a key cap, then what it does, 32 units apart.</summary>
public class KeyHintBar : UiElement
{
    private readonly List<KeyHint> _hints = [];
    private readonly List<string> _labels = [];

    /// <summary>Initializes a new instance of the <see cref="KeyHintBar"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <exception cref="ArgumentNullException"><paramref name="theme"/> is <see langword="null"/>.</exception>
    public KeyHintBar(UiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme = theme;
    }

    /// <summary>Gets the theme.</summary>
    public UiTheme Theme { get; }

    /// <summary>Gets the hints shown.</summary>
    public IReadOnlyList<KeyHint> Hints => _hints;

    /// <summary>Replaces the hints shown.</summary>
    /// <param name="hints">The hints, left to right.</param>
    public void SetHints(IEnumerable<KeyHint> hints)
    {
        ArgumentNullException.ThrowIfNull(hints);
        _hints.Clear();
        _labels.Clear();
        foreach (KeyHint hint in hints)
        {
            _hints.Add(hint);
            _labels.Add(hint.Label.ToUpperInvariant());
        }
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        float width = 0f;
        for (int i = 0; i < _hints.Count; i++)
        {
            width += HintWidth(context, i) + (i > 0 ? context.Px(32f) : 0f);
        }

        return new UiVec2(width, KitDraw.MeasureKeyCap(context, Theme, "W").Y);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float total = MeasureContent(context, Bounds.Size).X;
        float x = Bounds.Right - total;
        float labelSize = context.Px(16f);
        for (int i = 0; i < _hints.Count; i++)
        {
            UiVec2 cap = KitDraw.MeasureKeyCap(context, Theme, _hints[i].Key);
            var at = new UiVec2(x, Bounds.Y + ((Bounds.Height - cap.Y) * 0.5f));
            UiRect box = KitDraw.KeyCap(painter, Theme, _hints[i].Key, at);
            painter.DrawLabel(
                Theme.Heading,
                _labels[i],
                new UiVec2(box.Right + context.Px(10f), UiRowStyle.CapsCenterY(Theme.Heading, labelSize, Bounds)),
                labelSize,
                UiPalette.TextTertiary,
                0.16f);
            x += HintWidth(context, i) + context.Px(32f);
        }
    }

    private float HintWidth(LayoutContext context, int index) =>
        KitDraw.MeasureKeyCap(context, Theme, _hints[index].Key).X
        + context.Px(10f)
        + UiPainter.MeasureLabel(Theme.Heading, _labels[index], context.Px(16f), 0.16f);
}
