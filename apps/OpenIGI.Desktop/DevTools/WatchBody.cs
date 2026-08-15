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

    private float _cell = 8f;
    private float _textSize = 13f;

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
        MathF.Max(1, _watch.Names.Count) * _theme.Mono.LineHeight(_textSize) * 1.35f + 12f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        _textSize = context.Px(DevConsoleCVars.FontSize.Value * 0.95f);
        _cell = _theme.Mono.MeasureWidth("0", _textSize);
        return available;
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        _watch.Read(_registry, _entries);

        float lineHeight = _theme.Mono.LineHeight(_textSize) * 1.35f;
        float y = Bounds.Y + 6f;

        if (_entries.Count == 0)
        {
            painter.DrawText(
                _theme.Mono,
                "nothing pinned — try  watch r_vsync",
                new UiVec2(Bounds.X + _cell, y),
                _textSize,
                _theme.Ui.TextDisabled);
            return;
        }

        foreach (ConsoleWatchEntry entry in _entries)
        {
            painter.DrawText(
                _theme.Mono,
                Fit(entry.Name, NameColumns - 1),
                new UiVec2(Bounds.X + _cell, y),
                _textSize,
                entry.IsResolved ? _theme.Ui.TextMuted : _theme.Ui.TextDisabled);

            painter.DrawText(
                _theme.Mono,
                entry.Value,
                new UiVec2(Bounds.X + _cell + (_cell * NameColumns), y),
                _textSize,
                entry.IsResolved ? _theme.Ui.Text : _theme.Ui.Danger);

            y += lineHeight;
        }
    }

    private static string Fit(string text, int columns) =>
        columns <= 1 || text.Length <= columns ? text : string.Concat(text.AsSpan(0, columns - 1), "…");
}
