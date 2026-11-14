using OpenIGI.Core.Graphics;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>
/// A compact page's sections, down the left edge of the screen: Home and the five, an icon over a word each.
/// </summary>
/// <remarks>
/// A phone on its side has some 390 units of height and twice that across. A header across the top would take a fifth of
/// what there is least of; a rail down the side takes a tenth of what there is most of, and its six targets are each as
/// large as a thumb. The current section is the one with the amber bar at its edge.
/// </remarks>
internal sealed class SectionRail : Panel
{
    // Design units: an entry's box, the gap between two, and the room above the first.
    private const float EntryWidth = 72f;
    private const float EntryHeight = 56f;
    private const float EntryGap = 4f;
    private const float Top = 10f;
    private const float Left = 4f;

    private readonly UiTheme _theme;
    private readonly List<Entry> _entries = [];

    /// <summary>Initializes a new instance of the <see cref="SectionRail"/> class.</summary>
    /// <param name="theme">The theme.</param>
    public SectionRail(UiTheme theme)
    {
        _theme = theme;
    }

    /// <summary>Adds a section.</summary>
    /// <param name="icon">Its icon; <see cref="KitIcon.None"/> draws the game's diamond.</param>
    /// <param name="label">Its name.</param>
    /// <param name="current">Whether it is the page showing.</param>
    /// <param name="go">Goes to it.</param>
    public void Add(KitIcon icon, string label, bool current, Action go)
    {
        var entry = new Entry(_theme, icon, label) { Current = current };
        entry.Activated += _ => go();
        _entries.Add(entry);
        AddChild(entry);
    }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available)
    {
        foreach (Entry entry in _entries)
        {
            entry.Measure(context, new UiVec2(context.Px(EntryWidth), context.Px(EntryHeight)));
        }

        return available;
    }

    /// <inheritdoc/>
    protected override void ArrangeContent(LayoutContext context, UiRect contentRect)
    {
        // Six entries at their full height are 366 units. A screen shorter than that shares what it has between them.
        int count = Math.Max(1, _entries.Count);
        float gap = context.Px(EntryGap);
        float room = contentRect.Height - context.Px(Top);
        float height = MathF.Min(context.Px(EntryHeight), (room - ((count - 1) * gap)) / count);
        float y = contentRect.Y + context.Px(Top);
        foreach (Entry entry in _entries)
        {
            entry.Arrange(new UiRect(contentRect.X + context.Px(Left), y, context.Px(EntryWidth), height));
            y += height + gap;
        }
    }

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        // To the screen's own edges, past the page's safe area: under a notch the rail is still the rail.
        float bottom = MathF.Max(Bounds.Bottom, painter.Context.Viewport.Y);
        painter.FillRect(UiRect.FromEdges(0f, 0f, Bounds.Right, bottom), UiPalette.InkAt(0.94f));
        float t = painter.Context.Px(1f);
        painter.FillRect(new UiRect(Bounds.Right - t, Bounds.Y, t, Bounds.Height), UiPalette.White(0.12f));
    }

    private sealed class Entry : Control
    {
        private readonly KitIcon _icon;
        private readonly string _label;

        public Entry(UiTheme theme, KitIcon icon, string label)
            : base(theme)
        {
            _icon = icon;
            _label = label.ToUpperInvariant();
        }

        public bool Current { get; init; }

        protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) => available;

        protected override void OnDraw(UiPainter painter)
        {
            LayoutContext context = painter.Context;
            float appear = Visual.Appear;
            float hot = MathF.Max(Visual.Focus, IsPointerOver ? 1f : 0f);
            if (Current)
            {
                painter.FillRect(Bounds, UiPalette.AccentAt(0.13f * appear));
                float inset = context.Px(8f);
                painter.FillRect(new UiRect(Bounds.X, Bounds.Y + inset, context.Px(3f), MathF.Max(0f, Bounds.Height - (inset * 2f))), UiPalette.Fade(Theme.Accent, appear));
            }
            else if (hot > 0.001f)
            {
                painter.FillRect(Bounds, UiPalette.White(0.07f * hot * appear));
            }

            Bgra32 ink = Current ? Theme.Accent : Interpolate.Lerp(Theme.TextSecondary, Theme.TextBright, hot);
            float icon = context.Px(22f);
            float labelSize = context.Px(12f);
            float labelHeight = labelSize * 0.75f;
            float block = icon + context.Px(5f) + labelHeight;
            float top = Bounds.Center.Y - (block * 0.5f);
            var at = new UiVec2(Bounds.Center.X, top + (icon * 0.5f));
            if (_icon == KitIcon.None)
            {
                painter.FillDiamond(at, context.Px(16f), UiPalette.Fade(Theme.Accent, appear));
            }
            else
            {
                KitIcons.Draw(painter, _icon, at, icon, UiPalette.Fade(ink, appear));
            }

            const float Tracking = 0.06f;
            float width = UiPainter.MeasureLabel(Theme.Heading, _label, labelSize, Tracking);
            var labelBox = new UiRect(Bounds.X, top + icon + context.Px(5f), Bounds.Width, labelHeight);
            painter.DrawLabel(
                Theme.Heading,
                _label,
                new UiVec2(Bounds.Center.X - (width * 0.5f), UiRowStyle.CapsCenterY(Theme.Heading, labelSize, labelBox)),
                labelSize,
                UiPalette.Fade(Current ? Theme.Text : ink, appear),
                Tracking);
        }
    }
}
