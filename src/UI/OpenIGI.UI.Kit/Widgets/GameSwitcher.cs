using OpenIGI.Core.Graphics;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Drawing;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.UI.Kit.Widgets;

/// <summary>
/// The header's title, which is also the game switcher: an amber diamond, the game's name over its
/// tagline, and a caret that turns over when the list of games drops open.
/// </summary>
/// <remarks>
/// The name at the top left of the screen is the statement of which game is running, so that is where the
/// choice of game lives — in the menu and in the launcher alike. The list is the
/// <see cref="DropdownButton"/>'s, each entry a name over its tagline with the running one marked ACTIVE.
/// </remarks>
public class GameSwitcher : DropdownButton
{
    private readonly Tween _caret = new(0f, duration: 0.2f, Ease.InOutQuad);
    private string _title = string.Empty;
    private string? _titleSource;

    /// <summary>Initializes a new instance of the <see cref="GameSwitcher"/> class.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="title">The running game's name.</param>
    /// <param name="items">Every game's name, in the order they are listed.</param>
    public GameSwitcher(UiTheme theme, string title, IEnumerable<string> items)
        : base(theme, title, items)
    {
        Accent = ButtonAccent.Text;
        Padding = new Thickness(0f, 8f, 12f, 8f);
    }

    /// <summary>Gets or sets the running game's tagline, shown in monospace under its name.</summary>
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>Gets or sets the name's size, in design units.</summary>
    public float TitleSize { get; set; } = 26f;

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        Thickness padding = context.Px(Padding);
        float width = context.Px(12f + 14f) + TextBlockWidth(context) + context.Px(6f + 14f);
        float height = context.Px(TitleSize) + context.Px(2f) + Theme.Mono.LineHeight(context.Px(11f));
        return new UiVec2(width + padding.Horizontal, height + padding.Vertical);
    }

    /// <inheritdoc/>
    protected override void OnUpdate(float elapsedSeconds)
    {
        base.OnUpdate(elapsedSeconds);
        _caret.Retarget(IsOpen ? 1f : 0f);
        _caret.Update(elapsedSeconds);
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        LayoutContext context = painter.Context;
        Thickness padding = context.Px(Padding);
        float appear = Visual.Appear * (IsPointerOver ? 0.85f : 1f);
        UiRect inner = Bounds.Deflate(padding);
        float x = inner.X;
        float midY = inner.Center.Y;

        painter.FillDiamond(new UiVec2(x + context.Px(6f), midY), context.Px(12f), UiPalette.Fade(Theme.Accent, appear));
        x += context.Px(12f + 14f);

        float titleSize = context.Px(TitleSize);
        float subSize = context.Px(11f);
        float block = titleSize + context.Px(2f) + Theme.Mono.LineHeight(subSize);
        float top = midY - (block * 0.5f);
        var titleBox = new UiRect(x, top, inner.Width, titleSize);
        painter.DrawLabel(Theme.Display, UpperTitle, new UiVec2(x, UiRowStyle.CapsCenterY(Theme.Display, titleSize, titleBox)), titleSize, UiPalette.Fade(Theme.Text, appear), 0.18f);
        painter.DrawLabel(Theme.Mono, Subtitle, new UiVec2(x, top + titleSize + context.Px(2f)), subSize, UiPalette.Fade(Theme.TextMuted, appear), 0.3f);

        // The caret: a small filled triangle that turns over as the list opens.
        float caretX = x + TextBlockWidth(context) + context.Px(6f + 7f);
        float size = context.Px(8f);
        float turn = _caret.Value;
        float dir = 1f - (2f * turn);
        Span<UiVec2> caret =
        [
            new(caretX - size * 0.5f, midY - (size * 0.25f * dir)),
            new(caretX + size * 0.5f, midY - (size * 0.25f * dir)),
            new(caretX, midY + (size * 0.35f * dir)),
        ];
        if (dir < 0f)
        {
            (caret[0], caret[1]) = (caret[1], caret[0]);
        }

        painter.FillPolygon(caret, UiPalette.Fade(IsOpen ? Theme.Accent : Theme.TextMuted, appear));
    }

    private string UpperTitle
    {
        get
        {
            if (!ReferenceEquals(_titleSource, Text))
            {
                _titleSource = Text;
                _title = Text.ToUpperInvariant();
            }

            return _title;
        }
    }

    private float TextBlockWidth(LayoutContext context) =>
        MathF.Max(
            UiPainter.MeasureLabel(Theme.Display, UpperTitle, context.Px(TitleSize), 0.18f),
            UiPainter.MeasureLabel(Theme.Mono, Subtitle, context.Px(11f), 0.3f));
}
