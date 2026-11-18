using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// The kill feed down the top right corner -- killer, the weapon's own silhouette, how it struck, victim -- with the player's
/// own rows lit, each sliding in and fading away; the player's own kill announced low in the middle; and the killer's weapon
/// on the card a dead player watches it from.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // A row's height, the gap between rows, and the picture's height inside it, in design pixels.
    private const float FeedRow = 28f;
    private const float FeedGap = 4f;
    private const float FeedPicture = 17f;
    private const float FeedPictureWidest = 74f;
    private const float FeedName = 15f;
    private const float FeedNameWidest = 168f;
    private const float FeedPad = 10f;
    private const float FeedSpace = 8f;

    // How long a row takes to slide in, and to fade and fold away at the end of its life, in seconds.
    private const float FeedIn = 0.16f;
    private const float FeedOut = 0.45f;

    private static Bgra32 FeedFace => Bgra32.FromRgba(8, 10, 13, 150);
    private static Bgra32 FeedOwnFace => Bgra32.FromRgba(16, 19, 24, 205);
    private static Bgra32 FeedInk => Bgra32.FromRgb(236, 238, 240);
    private static Bgra32 GuardGrey => Bgra32.FromRgb(168, 172, 176);
    private static Bgra32 TeamKillAmber => Bgra32.FromRgb(255, 190, 70);

    private HudSilhouettes? _silhouettes;

    /// <inheritdoc/>
    public void DrawKillFeed(SpriteBatch batch, KillFeedView feed, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(feed);
        if (_theme is not { } type || feed.Entries.Count == 0)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = UiPainter.Create(batch, context);
        float right = context.SafeRect.Right - context.Px(16f);
        float y = context.SafeRect.Y + context.Px(64f);
        List<FeedPart> parts = [];
        foreach (KillFeedEntry entry in feed.Entries)
        {
            float age = (float)entry.Age;
            float left = (float)(entry.Life - entry.Age);
            float appear = Easing.Evaluate(Ease.OutCubic, Math.Clamp(age / FeedIn, 0f, 1f));
            float fade = Math.Clamp(left / FeedOut, 0f, 1f);
            float opacity = MathF.Min(appear, fade);

            // A row leaving folds up as it fades, so the ones under it glide up into its place.
            float fold = Easing.Evaluate(Ease.OutCubic, fade);
            float height = context.Px(FeedRow);
            if (opacity > 0.01f)
            {
                parts.Clear();
                RowParts(parts, type, context, feed, entry.Note);
                DrawRow(batch, painter, context, feed, entry.Note, parts, right + (context.Px(36f) * (1f - appear)), y, height, opacity);
            }

            y += (height + context.Px(FeedGap)) * fold;
        }

        DrawKillConfirm(painter, context, type, feed);
    }

    // One row, right-aligned at its edge: its strip, the player's own edge, and its parts left to right.
    private void DrawRow(
        SpriteBatch batch, UiPainter painter, LayoutContext context, KillFeedView feed, KillNote note, List<FeedPart> parts, float right, float y, float height, float opacity)
    {
        float space = context.Px(FeedSpace);
        float pad = context.Px(FeedPad);
        float width = (pad * 2f) + parts.Sum(p => p.Width) + (space * Math.Max(0, parts.Count - 1));
        var strip = new UiRect(right - width, y, width, height);
        bool killed = feed.OwnEntity >= 0 && note.KillerEntity == feed.OwnEntity;
        bool died = feed.OwnEntity >= 0 && note.VictimEntity == feed.OwnEntity;

        painter.PushOpacity(opacity);
        painter.FillRect(strip, killed || died ? FeedOwnFace : FeedFace);
        if (killed || died)
        {
            Bgra32 edge = died ? Enemy : FeedInk;
            painter.FillRect(new UiRect(strip.X, strip.Y, MathF.Max(2f, context.Px(2.5f)), height), edge);
            painter.StrokeRect(strip, MathF.Max(1f, context.Px(1f)), edge.WithAlpha(died ? (byte)120 : (byte)90));
        }

        float x = strip.X + pad;
        float middle = y + (height * 0.5f);
        foreach (FeedPart part in parts)
        {
            DrawPart(batch, painter, part, x, middle, opacity);
            x += part.Width + space;
        }

        painter.PopOpacity();
    }

    // What a row shows, left to right: the killer, the weapon or the cause, the marks of how it struck, the victim.
    private static void RowParts(List<FeedPart> parts, UiTheme type, LayoutContext context, KillFeedView feed, KillNote note)
    {
        float nameSize = context.Px(FeedName);
        float widest = context.Px(FeedNameWidest);
        float glyph = context.Px(FeedPicture);
        if (note.HasKiller)
        {
            Bgra32 colour = (note.Marks & KillMarks.TeamKill) != 0 ? TeamKillAmber : NameColour(feed, note.KillerEntity, note.KillerTeam, guard: false);
            parts.Add(FeedPart.Name(type.Body, Clip(type.Body, note.Killer, nameSize, widest, 0.02f), nameSize, colour));
        }

        // The weapon's own picture where the title has one; else what killed, as a mark; else the weapon's name.
        KitIcon cause = note.Cause switch
        {
            KillCause.Explosive => KitIcon.Explosion,
            KillCause.Fall => KitIcon.Fall,
            KillCause.Suicide or KillCause.World => KitIcon.Skull,
            KillCause.Melee => KitIcon.Knife,
            _ => KitIcon.None,
        };
        bool credited = note.HasKiller && note.Cause is KillCause.Weapon or KillCause.Explosive or KillCause.Melee;
        if (credited && TryPicture(feed.Art, note.Weapon, glyph, context.Px(FeedPictureWidest), out FeedPart picture))
        {
            parts.Add(picture);
        }
        else
        {
            string weaponName = cause == KitIcon.None ? feed.Art?.WeaponName(note.Weapon) ?? string.Empty : string.Empty;
            if (weaponName.Length > 0)
            {
                float small = context.Px(11f);
                string shown = weaponName.ToUpperInvariant();
                parts.Add(FeedPart.Name(type.Mono, shown, small, UiPalette.TextMuted, UiPainter.MeasureLabel(type.Mono, shown, small, 0.12f), 0.12f));
            }
            else
            {
                // A credited round with no picture or name to show is marked as a hit; nobody's death as a skull.
                KitIcon mark = cause != KitIcon.None ? cause : note.HasKiller ? KitIcon.Target : KitIcon.Skull;
                parts.Add(FeedPart.Glyph(mark, glyph, FeedInk));
            }
        }

        if ((note.Marks & KillMarks.Backstab) != 0 && note.Cause != KillCause.Melee)
        {
            parts.Add(FeedPart.Glyph(KitIcon.Knife, glyph, FeedInk));
        }

        if ((note.Marks & KillMarks.Headshot) != 0)
        {
            parts.Add(FeedPart.Glyph(KitIcon.Headshot, glyph, HeadshotColour));
        }

        if ((note.Marks & KillMarks.TeamKill) != 0)
        {
            parts.Add(FeedPart.Glyph(KitIcon.TeamKill, glyph, TeamKillAmber));
        }

        bool guard = (note.Marks & KillMarks.Guard) != 0;
        parts.Add(FeedPart.Name(type.Body, Clip(type.Body, note.Victim, nameSize, widest, 0.02f), nameSize, NameColour(feed, note.VictimEntity, note.VictimTeam, guard)));
    }

    // A name's colour as the player sees the sides: its own white, its side blue, the other red; in co-op every player blue
    // and the level's soldiers grey; in a free-for-all everyone else red.
    private static Bgra32 NameColour(KillFeedView feed, int entity, int team, bool guard)
    {
        if (guard)
        {
            return GuardGrey;
        }

        if (feed.OwnEntity >= 0 && entity == feed.OwnEntity)
        {
            return UiPalette.TextBright;
        }

        return feed.Coop ? Ally
            : !feed.Teams ? Enemy
            : team < 0 || feed.OwnTeam < 0 ? FeedInk
            : team == feed.OwnTeam ? Ally
            : Enemy;
    }

    private static Bgra32 HeadshotColour => UiPalette.Accent;

    // The weapon's picture fitted to the row: as tall as a glyph, no wider than the widest the feed allows.
    private static bool TryPicture(IKillFeedArt? art, string weapon, float height, float widest, out FeedPart part)
    {
        part = default;
        if (art?.Icons is not { } icons || string.IsNullOrEmpty(weapon) || art.WeaponIcon(weapon) is not { IsSome: true } icon
            || !icons.TryMeasure(icon, out float w, out float h) || w <= 0f || h <= 0f)
        {
            return false;
        }

        float scale = MathF.Min(height / h, widest / w);
        part = FeedPart.Sprite(icons, icon, scale, w * scale, h * scale, art.WeaponsFaceLeft);
        return true;
    }

    private void DrawPart(SpriteBatch batch, UiPainter painter, FeedPart part, float x, float middle, float opacity)
    {
        switch (part.Kind)
        {
            case FeedPartKind.Name:
                float top = middle - (part.Font!.LineHeight(part.Size) * 0.5f);
                painter.DrawLabel(part.Font, part.Text, new UiVec2(x + 1f, top + 1f), part.Size, Bgra32.FromRgba(0, 0, 0, 150), part.Tracking);
                painter.DrawLabel(part.Font, part.Text, new UiVec2(x, top), part.Size, part.Colour, part.Tracking);
                break;

            case FeedPartKind.Glyph:
                KitIcons.Draw(painter, part.Icon, new UiVec2(x + (part.Width * 0.5f), middle), part.Width, part.Colour);
                break;

            case FeedPartKind.Picture:
                DrawPicture(batch, part, x, middle - (part.Height * 0.5f), FeedInk.WithAlpha((byte)(255f * opacity)));
                break;
        }
    }

    // A title's picture as a one-colour silhouette, turned to point at the victim, where the title hands its art over whole;
    // else the title's own drawing of it.
    private void DrawPicture(SpriteBatch batch, FeedPart part, float x, float y, Bgra32 colour)
    {
        _silhouettes ??= new HudSilhouettes(_device);
        if (part.Icons!.Resolve(part.Art) is { } source && _silhouettes.Draw(batch, source, x, y, part.Scale, colour, part.Mirror))
        {
            return;
        }

        part.Icons.Draw(batch, part.Art, x, y, part.Scale, colour);
    }

    // The player's own newest kill, low in the middle: the mark, ELIMINATED and whom; how it struck and a run of kills under it.
    private static void DrawKillConfirm(UiPainter painter, LayoutContext context, UiTheme type, KillFeedView feed)
    {
        if (feed.Confirmed() is not ({ } kill, int streak))
        {
            return;
        }

        float age = (float)kill.Age;
        float appear = Easing.Evaluate(Ease.OutBack, Math.Clamp(age / 0.22f, 0f, 1f));
        float opacity = MathF.Min(Math.Clamp(age / 0.12f, 0f, 1f), Math.Clamp(((float)KillFeedView.ConfirmSeconds - age) / 0.5f, 0f, 1f));
        if (opacity <= 0.01f)
        {
            return;
        }

        UiRect safe = context.SafeRect;
        float centre = safe.X + (safe.Width * 0.5f);
        float y = safe.Y + (safe.Height * 0.72f) + (context.Px(10f) * (1f - appear));
        float label = context.Px(13f);
        float name = context.Px(24f);
        float glyph = context.Px(22f);
        float gap = context.Px(10f);
        const string Said = "ELIMINATED";
        string victim = Clip(type.Heading, kill.Note.Victim.ToUpperInvariant(), name, context.Px(320f), 0.06f);
        float saidWidth = UiPainter.MeasureLabel(type.Mono, Said, label, 0.24f);
        float victimWidth = UiPainter.MeasureLabel(type.Heading, victim, name, 0.06f);
        float width = glyph + gap + saidWidth + gap + victimWidth;
        float x = centre - (width * 0.5f);
        bool head = (kill.Note.Marks & KillMarks.Headshot) != 0;
        bool guard = (kill.Note.Marks & KillMarks.Guard) != 0;

        painter.PushOpacity(opacity);
        bool tagged = head || (kill.Note.Marks & KillMarks.Backstab) != 0 || streak > 1;
        Band(painter, new UiRect(x - context.Px(60f), y - context.Px(6f), width + context.Px(120f), context.Px(tagged ? 68f : 42f)), StripFace);
        KitIcons.Draw(painter, head ? KitIcon.Headshot : KitIcon.Skull, new UiVec2(x + (glyph * 0.5f), y + (context.Px(30f) * 0.5f)), glyph, head ? HeadshotColour : FeedInk);
        x += glyph + gap;
        painter.DrawLabel(type.Mono, Said, new UiVec2(x, y + context.Px(8f)), label, UiPalette.TextMuted, 0.24f);
        x += saidWidth + gap;
        painter.DrawLabel(type.Heading, victim, new UiVec2(x, y), name, guard ? FeedInk : Enemy, 0.06f);

        // Under it, how it struck and the run it is part of.
        List<(string Text, Bgra32 Colour)> tags = [];
        if (head)
        {
            tags.Add(("HEADSHOT", HeadshotColour));
        }

        if ((kill.Note.Marks & KillMarks.Backstab) != 0)
        {
            tags.Add(("BACKSTAB", HeadshotColour));
        }

        if (KillFeedView.StreakName(streak) is { Length: > 0 } run)
        {
            tags.Add((run, Victory));
        }

        float tag = context.Px(16f);
        float tagGap = context.Px(18f);
        float tagsWidth = tags.Sum(t => UiPainter.MeasureLabel(type.Heading, t.Text, tag, 0.12f)) + (tagGap * Math.Max(0, tags.Count - 1));
        float tx = centre - (tagsWidth * 0.5f);
        foreach ((string text, Bgra32 colour) in tags)
        {
            painter.DrawLabel(type.Heading, text, new UiVec2(tx, y + context.Px(37f)), tag, colour, 0.12f);
            tx += UiPainter.MeasureLabel(type.Heading, text, tag, 0.12f) + tagGap;
        }

        painter.PopOpacity();
    }

    // The killer's weapon and how it struck, after the name on a dead player's card: as wide as it draws, or nought.
    private static float KilledByWidth(KillFeedView? feed, LayoutContext context, out FeedPart? picture, out bool head)
    {
        picture = null;
        head = false;
        if (feed?.KilledBy() is not { HasKiller: true } note)
        {
            return 0f;
        }

        head = (note.Marks & KillMarks.Headshot) != 0;
        float glyph = context.Px(20f);
        float width = 0f;
        if (TryPicture(feed.Art, note.Weapon, glyph, context.Px(90f), out FeedPart part))
        {
            picture = part;
            width += context.Px(14f) + part.Width;
        }

        return width + (head ? context.Px(10f) + glyph : 0f);
    }

    // What a feed row is made of.
    private enum FeedPartKind
    {
        Name,
        Glyph,
        Picture,
    }

    // One piece of a feed row, measured.
    private readonly record struct FeedPart(
        FeedPartKind Kind, float Width, string Text, UiFont? Font, float Size, Bgra32 Colour, float Tracking, KitIcon Icon, IHudIconSource? Icons, HudIcon Art, float Scale, float Height, bool Mirror = false)
    {
        public static FeedPart Name(UiFont font, string text, float size, Bgra32 colour, float? width = null, float tracking = 0.02f) =>
            new(FeedPartKind.Name, width ?? UiPainter.MeasureLabel(font, text, size, tracking), text, font, size, colour, tracking, KitIcon.None, null, HudIcon.None, 1f, size);

        public static FeedPart Glyph(KitIcon icon, float size, Bgra32 colour) =>
            new(FeedPartKind.Glyph, size, string.Empty, null, size, colour, 0f, icon, null, HudIcon.None, 1f, size);

        public static FeedPart Sprite(IHudIconSource icons, HudIcon icon, float scale, float width, float height, bool mirror) =>
            new(FeedPartKind.Picture, width, string.Empty, null, height, Bgra32.White, 0f, KitIcon.None, icons, icon, scale, height, mirror);
    }
}
