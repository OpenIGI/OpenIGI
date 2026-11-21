using OpenIGI.Core.Graphics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Presentation.Hud.Remastered.Widgets;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Animation;
using OpenIGI.UI.Kit;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <content>
/// The kill feed down the top right corner, in the played title's look -- killer, the weapon's own silhouette, how it struck,
/// victim, the names in the sides' inks on a plate of the title's glass -- with the player's own rows bracketed,
/// each sliding in and fading away; the player's own kill announced low in the middle; and the killer's weapon on the card a
/// dead player watches it from.
/// </content>
public sealed partial class RemasteredHudRenderer
{
    // A row's height, the gap between rows, and the picture's height inside it, in layout pixels.
    private const float FeedGap = 4f;
    private const float FeedPicture = 17f;
    private const float FeedPictureWidest = 74f;
    private const float FeedNameWidest = 168f;
    private const float FeedPad = 10f;
    private const float FeedSpace = 8f;

    // How long a row takes to slide in, and to fade and fold away at the end of its life, in seconds.
    private const float FeedIn = 0.16f;
    private const float FeedOut = 0.45f;

    // The weapon's silhouette: white in every title, as the feed's pictures are drawn.
    private static Bgra32 FeedInk => Bgra32.FromRgb(236, 238, 240);

    private HudSilhouettes? _silhouettes;

    /// <inheritdoc/>
    public void DrawKillFeed(SpriteBatch batch, KillFeedView feed, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(feed);
        if (feed.Entries.Count == 0 || Overlay(batch, screenWidth, screenHeight) is not { } o)
        {
            return;
        }

        float right = o.Safe.Right - o.Px(16f);
        float y = o.Safe.Y + o.Px(64f);
        float height = o.Text.LineHeightAt(o.Px(HudOverlay.Small)) + o.Px(12f);
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
            if (opacity > 0.01f)
            {
                parts.Clear();
                RowParts(o, parts, feed, entry.Note);
                DrawRow(batch, o, feed, entry.Note, parts, right + (o.Px(36f) * (1f - appear)), y, height, opacity);
            }

            y += (height + o.Px(FeedGap)) * fold;
        }

        DrawKillConfirm(o, feed);
    }

    // One row, right-aligned at its edge: its plate, the player's own brackets, and its parts left to right.
    private void DrawRow(SpriteBatch batch, HudOverlay o, KillFeedView feed, KillNote note, List<FeedPart> parts, float right, float y, float height, float opacity)
    {
        float space = o.Px(FeedSpace);
        float pad = o.Px(FeedPad);
        float width = (pad * 2f) + parts.Sum(p => p.Width) + (space * Math.Max(0, parts.Count - 1));
        var strip = new UiRect(right - width, y, width, height);
        bool killed = feed.OwnEntity >= 0 && note.KillerEntity == feed.OwnEntity;
        bool died = feed.OwnEntity >= 0 && note.VictimEntity == feed.OwnEntity;

        o.Painter.PushOpacity(opacity);
        o.Plate(strip, Bgra32.FromRgba(0, 0, 0, killed || died ? (byte)200 : (byte)150));
        o.Plate(strip, HudOverlay.Fade(o.Ink.Glass.WithAlpha(255), 0.25f));
        if (killed || died)
        {
            o.Brackets(strip, died ? o.Ink.Enemy : o.Ink.Highlight, 5f);
        }

        float x = strip.X + pad;
        float middle = y + (height * 0.5f);
        foreach (FeedPart part in parts)
        {
            DrawPart(batch, o, part, x, middle, opacity);
            x += part.Width + space;
        }

        o.Painter.PopOpacity();
    }

    // What a row shows, left to right: the killer, the weapon or the cause, the marks of how it struck, the victim.
    private static void RowParts(HudOverlay o, List<FeedPart> parts, KillFeedView feed, KillNote note)
    {
        float size = o.Px(HudOverlay.Small);
        float widest = o.Px(FeedNameWidest);
        float glyph = o.Px(FeedPicture);
        if (note.HasKiller)
        {
            Bgra32 colour = (note.Marks & KillMarks.TeamKill) != 0 ? o.Ink.Accent : NameColour(o, feed, note.KillerEntity, note.KillerTeam, guard: false);
            parts.Add(FeedPart.Name(o, o.Text.ClipAt(OverlayText.Prose(note.Killer), size, widest), size, colour));
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
        if (credited && TryPicture(feed.Art, note.Weapon, glyph, o.Px(FeedPictureWidest), out FeedPart picture))
        {
            parts.Add(picture);
        }
        else
        {
            string weaponName = cause == KitIcon.None ? feed.Art?.WeaponName(note.Weapon) ?? string.Empty : string.Empty;
            if (weaponName.Length > 0)
            {
                parts.Add(FeedPart.Name(o, OverlayText.Capitals(weaponName), size, o.Quiet));
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
            parts.Add(FeedPart.Glyph(KitIcon.Headshot, glyph, o.Ink.Accent));
        }

        if ((note.Marks & KillMarks.TeamKill) != 0)
        {
            parts.Add(FeedPart.Glyph(KitIcon.TeamKill, glyph, o.Ink.Accent));
        }

        bool guard = (note.Marks & KillMarks.Guard) != 0;
        parts.Add(FeedPart.Name(o, o.Text.ClipAt(OverlayText.Prose(note.Victim), size, widest), size, NameColour(o, feed, note.VictimEntity, note.VictimTeam, guard)));
    }

    // A name's ink as the player sees the sides: its own the title's bright ink, its side the ally's, the other the enemy's; in co-op
    // every player the ally's and the level's soldiers quiet; in a free-for-all everyone else the enemy's.
    private static Bgra32 NameColour(HudOverlay o, KillFeedView feed, int entity, int team, bool guard)
    {
        if (guard)
        {
            return o.Quiet;
        }

        if (feed.OwnEntity >= 0 && entity == feed.OwnEntity)
        {
            return o.Ink.Highlight;
        }

        return feed.Coop ? o.Ink.Ally
            : !feed.Teams ? o.Ink.Enemy
            : team < 0 || feed.OwnTeam < 0 ? o.Ink.Plain
            : team == feed.OwnTeam ? o.Ink.Ally
            : o.Ink.Enemy;
    }

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

    private void DrawPart(SpriteBatch batch, HudOverlay o, FeedPart part, float x, float middle, float opacity)
    {
        switch (part.Kind)
        {
            case FeedPartKind.Name:
                o.Text.DrawAt(part.Text, x, middle - (o.Text.LineHeightAt(part.Size) * 0.5f), part.Size, part.Colour, shadow: true);
                break;

            case FeedPartKind.Glyph:
                KitIcons.Draw(o.Painter, part.Icon, new UiVec2(x + (part.Width * 0.5f), middle), part.Width, part.Colour);
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
    private static void DrawKillConfirm(HudOverlay o, KillFeedView feed)
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

        UiRect safe = o.Safe;
        float centre = safe.X + (safe.Width * 0.5f);
        float y = safe.Y + (safe.Height * 0.72f) + (o.Px(10f) * (1f - appear));
        float small = o.Px(HudOverlay.Small);
        float large = o.Px(HudOverlay.Large);
        float glyph = o.Px(22f);
        float gap = o.Px(10f);
        const string Said = "ELIMINATED";
        string victim = o.Text.ClipAt(OverlayText.Capitals(kill.Note.Victim), large, o.Px(320f));
        float saidWidth = o.Text.MeasureAt(Said, small);
        float victimWidth = o.Text.MeasureAt(victim, large);
        float width = glyph + gap + saidWidth + gap + victimWidth;
        float x = centre - (width * 0.5f);
        bool head = (kill.Note.Marks & KillMarks.Headshot) != 0;
        bool guard = (kill.Note.Marks & KillMarks.Guard) != 0;

        // Under it, how it struck and the run it is part of.
        List<(string Text, Bgra32 Colour)> tags = [];
        if (head)
        {
            tags.Add(("HEADSHOT", o.Ink.Accent));
        }

        if ((kill.Note.Marks & KillMarks.Backstab) != 0)
        {
            tags.Add(("BACKSTAB", o.Ink.Accent));
        }

        if (KillFeedView.StreakName(streak) is { Length: > 0 } run)
        {
            tags.Add((OverlayText.Capitals(run), o.Ink.Highlight));
        }

        o.Painter.PushOpacity(opacity);
        float lineHeight = o.Text.LineHeightAt(large);
        float body = o.Px(HudOverlay.Body);
        o.Band(new UiRect(x - o.Px(60f), y - o.Px(6f), width + o.Px(120f), lineHeight + o.Px(12f) + (tags.Count > 0 ? o.Text.LineHeightAt(body) + o.Px(8f) : 0f)), Bgra32.FromRgba(0, 0, 0, 170));
        KitIcons.Draw(o.Painter, head ? KitIcon.Headshot : KitIcon.Skull, new UiVec2(x + (glyph * 0.5f), y + (lineHeight * 0.5f)), glyph, head ? o.Ink.Accent : FeedInk);
        x += glyph + gap;
        o.Text.DrawAt(Said, x, y + ((lineHeight - o.Text.LineHeightAt(small)) * 0.5f), small, o.Quiet, shadow: true);
        x += saidWidth + gap;
        o.Text.DrawAt(victim, x, y, large, guard ? o.Ink.Plain : o.Ink.Enemy, shadow: true);

        float tagGap = o.Px(18f);
        float tagsWidth = tags.Sum(t => o.Text.MeasureAt(t.Text, body)) + (tagGap * Math.Max(0, tags.Count - 1));
        float tx = centre - (tagsWidth * 0.5f);
        foreach ((string text, Bgra32 colour) in tags)
        {
            tx += o.Text.DrawAt(text, tx, y + lineHeight + o.Px(6f), body, colour, shadow: true) + tagGap;
        }

        o.Painter.PopOpacity();
    }

    // The killer's weapon and how it struck, after the name on a dead player's card: as wide as it draws, or nought.
    private static float KilledByWidth(HudOverlay o, KillFeedView? feed, out FeedPart? picture, out bool head)
    {
        picture = null;
        head = false;
        if (feed?.KilledBy() is not { HasKiller: true } note)
        {
            return 0f;
        }

        head = (note.Marks & KillMarks.Headshot) != 0;
        float glyph = o.Px(20f);
        float width = 0f;
        if (TryPicture(feed.Art, note.Weapon, glyph, o.Px(90f), out FeedPart part))
        {
            picture = part;
            width += o.Px(14f) + part.Width;
        }

        return width + (head ? o.Px(10f) + glyph : 0f);
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
        FeedPartKind Kind, float Width, string Text, float Size, Bgra32 Colour, KitIcon Icon, IHudIconSource? Icons, HudIcon Art, float Scale, float Height, bool Mirror = false)
    {
        public static FeedPart Name(HudOverlay o, string text, float size, Bgra32 colour) =>
            new(FeedPartKind.Name, o.Text.MeasureAt(text, size), text, size, colour, KitIcon.None, null, HudIcon.None, 1f, size);

        public static FeedPart Glyph(KitIcon icon, float size, Bgra32 colour) =>
            new(FeedPartKind.Glyph, size, string.Empty, size, colour, icon, null, HudIcon.None, 1f, size);

        public static FeedPart Sprite(IHudIconSource icons, HudIcon icon, float scale, float width, float height, bool mirror) =>
            new(FeedPartKind.Picture, width, string.Empty, height, Bgra32.White, KitIcon.None, icons, icon, scale, height, mirror);
    }
}
