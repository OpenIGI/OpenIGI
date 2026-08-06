using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Frontend.Screens;

/// <summary>A selectable summary card for one player profile.</summary>
public sealed class ProfileCard : Control
{
    /// <summary>Initializes a profile card.</summary>
    public ProfileCard(UiTheme theme, string name)
        : base(theme) => Name = name ?? string.Empty;

    /// <summary>Gets or sets the profile name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the difficulty label.</summary>
    public string Difficulty { get; set; } = string.Empty;

    /// <summary>Gets or sets the current mission label.</summary>
    public string Mission { get; set; } = string.Empty;

    /// <summary>Gets or sets the campaign-progress label.</summary>
    public string Progress { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this card represents the active profile.</summary>
    public bool Active { get; set; }

    /// <inheritdoc/>
    protected override UiVec2 MeasureContent(LayoutContext context, UiVec2 available) =>
        new(available.X, context.Px(104f));

    /// <inheritdoc/>
    protected override void OnDraw(UiPainter painter)
    {
        float focus = MathF.Max(Visual.Focus, IsPointerOver ? 0.35f : 0f);
        float appear = Visual.Appear;
        float pad = painter.Context.Px(18f);
        Bgra32 fill = Active ? Theme.Panel.WithAlpha(232) : Theme.Panel.WithAlpha(175);
        Bgra32 border = Interpolate.Lerp(Theme.Border, Theme.Accent, MathF.Max(focus, Active ? 0.42f : 0f));
        Bgra32 primary = (Active ? Theme.Accent : Theme.Text).WithAlpha((byte)(255 * appear));
        Bgra32 muted = Theme.TextMuted.WithAlpha((byte)(255 * appear));
        float nameSize = painter.Context.Px(Theme.BodyTextSize * 1.12f);
        float metaSize = painter.Context.Px(Theme.BodyTextSize * 0.76f);

        painter.FillRect(Bounds, fill, BlendMode.AlphaBlend);
        painter.StrokeRect(Bounds, painter.Context.Px(1f), border, BlendMode.AlphaBlend);
        if (Active || focus > 0.01f)
        {
            painter.FillRect(
                new UiRect(Bounds.X, Bounds.Y, painter.Context.Px(3f), Bounds.Height),
                Theme.Accent.WithAlpha((byte)(255 * MathF.Max(focus, Active ? 0.8f : 0f) * appear)),
                BlendMode.AlphaBlend);
        }

        float nameY = Bounds.Y + pad;
        painter.DrawText(Theme.Heading, Name, new UiVec2(Bounds.X + pad, nameY), nameSize, primary);

        if (Active)
        {
            const string active = "ACTIVE";
            float activeWidth = Theme.Body.MeasureWidth(active, metaSize);
            painter.DrawText(
                Theme.Body,
                active,
                new UiVec2(Bounds.Right - pad - activeWidth, nameY + painter.Context.Px(3f)),
                metaSize,
                Theme.Accent.WithAlpha((byte)(255 * appear)));
        }

        float middleY = nameY + Theme.Heading.LineHeight(nameSize) + painter.Context.Px(8f);
        painter.DrawText(Theme.Body, Difficulty, new UiVec2(Bounds.X + pad, middleY), metaSize, muted);
        float progressWidth = Theme.Body.MeasureWidth(Progress, metaSize);
        painter.DrawText(Theme.Body, Progress, new UiVec2(Bounds.Right - pad - progressWidth, middleY), metaSize, muted);
        painter.DrawText(
            Theme.Body,
            Mission,
            new UiVec2(Bounds.X + pad, middleY + Theme.Body.LineHeight(metaSize) + painter.Context.Px(5f)),
            metaSize,
            Theme.Text.WithAlpha((byte)(210 * appear)));
    }
}
