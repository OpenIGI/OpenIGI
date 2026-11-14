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
            width += HintWidth(context, i) + (i > 0 ? context.Px(HintGap) : 0f);
        }

        return new UiVec2(width, KitDraw.MeasureKeyCap(context, Theme, "W").Y);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        float total = MeasureContent(context, Bounds.Size).X;
        float x = Bounds.Right - total;
        float labelSize = context.Px(LabelSize);
        for (int i = 0; i < _hints.Count; i++)
        {
            // "Q E" is two keys, each on a cap of its own.
            float pen = x;
            foreach (string key in Keys(i))
            {
                UiVec2 cap = KitDraw.MeasureKeyCap(context, Theme, key);
                UiRect box = KitDraw.KeyCap(painter, Theme, key, new UiVec2(pen, Bounds.Y + ((Bounds.Height - cap.Y) * 0.5f)));
                pen = box.Right + context.Px(CapGap);
            }

            painter.DrawLabel(
                Theme.Heading,
                _labels[i],
                new UiVec2(pen - context.Px(CapGap) + context.Px(LabelGap), UiRowStyle.CapsCenterY(Theme.Heading, labelSize, Bounds)),
                labelSize,
                Theme.TextSecondary,
                LabelTracking);
            x += HintWidth(context, i) + context.Px(HintGap);
        }
    }

    // Design units: the gap between two hints, between two caps of one hint, and between the caps and their label.
    private const float HintGap = 32f;
    private const float CapGap = 4f;
    private const float LabelGap = 10f;
    private const float LabelSize = 15f;
    private const float LabelTracking = 0.12f;

    private string[] Keys(int index) => _hints[index].Key.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private float HintWidth(LayoutContext context, int index)
    {
        float caps = 0f;
        string[] keys = Keys(index);
        for (int i = 0; i < keys.Length; i++)
        {
            caps += KitDraw.MeasureKeyCap(context, Theme, keys[i]).X + (i > 0 ? context.Px(CapGap) : 0f);
        }

        return caps + context.Px(LabelGap) + UiPainter.MeasureLabel(Theme.Heading, _labels[index], context.Px(LabelSize), LabelTracking);
    }
}
