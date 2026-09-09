using OpenIGI.Core.Configuration;
using OpenIGI.UI;
using OpenIGI.UI.Layout;

namespace OpenIGI.Desktop;

/// <summary>
/// The pinned-value panel: a short list of names and whatever they read this frame.
/// </summary>
/// <remarks>
/// <para>
/// Re-read every frame it draws, because that is the entire point — a value you have to ask for again
/// is a value you cannot watch change. The list is short by nature, so it is drawn directly rather
/// than through the virtualised table; a reader who pins forty things has already lost the thread.
/// </para>
/// <para>
/// This is one of the two panels that can outlive the console being shut, which is why it must cost
/// almost nothing: the entries are read into a list it owns rather than allocated per frame.
/// </para>
/// </remarks>
public sealed class WatchBody : UiElement
{
    private const int NameColumns = 26;

    private readonly ConsoleWatchList _watch;
    private readonly CVarRegistry _registry;
    private readonly DevConsoleTheme _theme;
    private readonly List<ConsoleWatchEntry> _entries = [];


    /// <summary>Initialises a new instance of the <see cref="WatchBody"/> class.</summary>
    /// <param name="watch">The pinned set.</param>
    /// <param name="registry">The registry names resolve against.</param>
    /// <param name="theme">The palette and face.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public WatchBody(ConsoleWatchList watch, CVarRegistry registry, DevConsoleTheme theme)
    {
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(theme);

        _watch = watch;
        _registry = registry;
        _theme = theme;
    }

    /// <summary>Gets how tall the panel wants to be for what is pinned, in pixels.</summary>
    /// <remarks>The window follows this, so pinning a value grows the panel instead of scrolling it.</remarks>
    public float PreferredHeight =>
        (MathF.Max(1, _watch.Names.Count) * _theme.RowHeight) + (_theme.Unit * 4f);

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        _watch.Read(_registry, _entries);

        float rowHeight = _theme.RowHeight;
        float x = MathF.Round(Bounds.X + _theme.Gutter);
        float y = MathF.Round(Bounds.Y + _theme.Unit * 2f);

        if (_entries.Count == 0)
        {
            painter.DrawText(
                _theme.BodyFont,
                "nothing pinned — try  watch r_vsync",
                new UiVec2(x, y + (_theme.Unit * 2f)),
                _theme.BodySize,
                _theme.Ui.TextDisabled);
            return;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            ConsoleWatchEntry entry = _entries[i];
            var row = new UiRect(Bounds.X, y, Bounds.Width, rowHeight);

            if ((i & 1) == 1)
            {
                painter.FillRect(row, _theme.Ui.Text.WithAlpha(8));
            }

            float textY = MathF.Round(row.Y + ((row.Height - _theme.BodyFont.LineHeight(_theme.BodySize)) * 0.5f));

            painter.DrawText(
                _theme.BodyFont,
                Fit(entry.Name, NameColumns - 1),
                new UiVec2(x, textY),
                _theme.BodySize,
                entry.IsResolved ? _theme.Ui.TextMuted : _theme.Ui.TextDisabled);

            // The value is right-aligned against a fixed column so a changing number grows leftwards
            // instead of shuffling everything after it, which is the whole point of watching one.
            painter.DrawText(
                _theme.BodyFont,
                entry.Value,
                new UiVec2(MathF.Round(x + (_theme.Cell * NameColumns)), textY),
                _theme.BodySize,
                entry.IsResolved ? _theme.Ui.Text : _theme.Ui.Danger);

            y = MathF.Round(y + rowHeight);
        }
    }

    private static string Fit(string text, int columns) =>
        columns <= 1 || text.Length <= columns ? text : string.Concat(text.AsSpan(0, columns - 1), "…");
}
