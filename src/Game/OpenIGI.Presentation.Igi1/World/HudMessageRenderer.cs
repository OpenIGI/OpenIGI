using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1.World;

/// <summary>Draws the status screen's text and prompts: the action icon, the mission's messages and subtitles, and the HANDICAM timecode.</summary>
/// <remarks>
/// <see cref="StatusScreenRenderer"/>'s message half, the part of the HUD that is about what the mission
/// is saying rather than what is in the player's hands. It owns no archive; the status screen hands in
/// its readout font, the cutscene subtitle face and its sprite lookup.
/// </remarks>
internal sealed class HudMessageRenderer
{
    private readonly Func<BitmapTextRenderer?> _font;
    private readonly Func<BitmapTextRenderer?> _subtitleFont;
    private readonly Func<string, SpriteResource?> _sprite;
    private float _actionPulsePhase;

    /// <summary>Initializes a new instance of the <see cref="HudMessageRenderer"/> class.</summary>
    /// <param name="font">The HUD's readout font, or <see langword="null"/> while it is unavailable.</param>
    /// <param name="subtitleFont">The cutscene subtitle face, or <see langword="null"/> when it did not load.</param>
    /// <param name="sprite">A HUD sprite by name, searched through every archive the status screen holds.</param>
    public HudMessageRenderer(
        Func<BitmapTextRenderer?> font,
        Func<BitmapTextRenderer?> subtitleFont,
        Func<string, SpriteResource?> sprite)
    {
        _font = font;
        _subtitleFont = subtitleFont;
        _sprite = sprite;
    }

    /// <summary>Advances <c>StatusScreen+1256</c>, the action icon's pulse, once on the fixed 30 Hz tick.</summary>
    public void Tick()
    {
        _actionPulsePhase += StatusScreenLayout.ActionPulseStep;
        if (_actionPulsePhase > 2f)
        {
            _actionPulsePhase = 0f;
        }
    }

    /// <summary>Draws the native action sprite selected by a retail activatable task.</summary>
    /// <remarks>See <see cref="StatusScreenRenderer.DrawAction"/>.</remarks>
    public void DrawAction(
        SpriteBatch batch,
        string spriteName,
        int screenWidth,
        int screenHeight,
        int scale = UiScale.Native)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrEmpty(spriteName);
        if (_sprite(spriteName) is not { } icon
            || icon.Asset.Frames.Count == 0)
        {
            return;
        }

        float s = Math.Max(UiScale.Native, scale);
        SpriteFrame frame = icon.Asset.Frames[0];
        (float x, float y) = StatusScreenLayout.ActionIcon(
            screenWidth, screenHeight, frame.Width * s, frame.Height * s);
        float alpha = StatusScreenLayout.ActionIconAlpha(_actionPulsePhase);
        byte alphaByte = (byte)Math.Clamp((int)(alpha * byte.MaxValue), 0, byte.MaxValue);
        icon.DrawFrame(batch, frame, x, y, s, s, new Bgra32(128, 128, 128, alphaByte));
    }

    /// <summary>Draws the status messages the mission has sent, as the original places them.</summary>
    /// <param name="batch">The 2D batch, already begun under a 2D projection.</param>
    /// <param name="messages">The messages on screen, in slot order.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="scale">The UI scale.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>StatusMessageScreen</c>'s draw slot, <c>0x486020</c>. Ordinary messages stack down the left of
    /// the screen from a tenth of the way in; a cutscene message leaves the column and takes a single
    /// subtitle line low on the screen instead, which is why the two are separated here rather than
    /// merged into one list with a flag.
    /// </para>
    /// <para>
    /// The step between messages is applied per message <em>drawn</em>, so when one in the middle of the
    /// column finishes the ones below it close the gap on the next frame. That is the original's own
    /// behaviour and it is why the loop advances its own cursor rather than indexing by slot.
    /// </para>
    /// </remarks>
    public void DrawStatusMessages(
        SpriteBatch batch,
        IEnumerable<StatusMessageDisplay> messages,
        int screenWidth,
        int screenHeight,
        float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(messages);
        BitmapTextRenderer? font = _font();
        BitmapTextRenderer? subtitleFont = _subtitleFont();
        if (font is null && subtitleFont is null)
        {
            return;
        }

        float left = StatusScreenLayout.MessageLeft(screenWidth);
        float y = StatusScreenLayout.MessageTop(screenHeight);

        foreach (StatusMessageDisplay message in messages)
        {
            if (message.IsCutscene)
            {
                if (subtitleFont is not null)
                {
                    DrawSubtitle(batch, subtitleFont, message, screenWidth, screenHeight, scale);
                }
                continue;
            }

            if (font is not null)
            {
                DrawStatusMessage(batch, font, message, left, y, scale);
            }
            y += StatusScreenLayout.MessageStep * scale;
        }
    }

    /// <summary>Draws the HANDICAM battery and clock at their retail anchors.</summary>
    /// <remarks>
    /// <c>sub_4A4D10</c> draws <c>LOCAL:common/sprites/battery.spr</c> at 10%/20%, formats a
    /// four-field, 24-frame clock, and right-aligns it at 90%/80%. The original seeds the counter
    /// from its global random stream over a 21600-frame span; a task-id seed keeps captures
    /// deterministic while preserving that range and rate.
    /// </remarks>
    public void DrawHandicamTimecode(
        SpriteBatch batch, CutsceneFrame frame, int screenWidth, int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(frame);
        if (_font() is not { } font)
        {
            return;
        }

        const int FramesPerSecond = 24;
        const int SeedSpan = 21600;
        int seed = (int)((uint)(frame.Scene.Id * 1103515245 + 12345) % SeedSpan);
        int total = seed + (int)((long)frame.Scene.Ticks * FramesPerSecond / CutsceneRuntime.Frequency);
        int field3 = total % FramesPerSecond;
        int seconds = (total / FramesPerSecond) % 60;
        int minutes = (total / FramesPerSecond / 60) % 60;
        int hours = (total / FramesPerSecond / 60 / 60) % 60;
        string text = $"{hours:00} : {minutes:00} : {seconds:00} : {field3:00}";
        float scale = Math.Max(1f, screenWidth / (float)UiScale.AuthoredWidth);
        (float width, float height) = font.Measure(text, scale, scale);
        float x = (screenWidth * 0.90f) - width;
        float y = (screenHeight * 0.80f) - height;
        // The text object's three colour fields are literal 100.0 percent: full white. Retail submits
        // it once, so there is no invented black shadow or second outline pass.
        font.Draw(batch, text, x, y, new Bgra32(255, 255, 255, 255), scale, scale);

        if (_sprite("battery.spr") is { } battery && battery.Asset.Frames.Count > 0)
        {
            // The timer in 0x4A4CB0 wraps every 1.5 seconds and changes the sprite state at 0.75.
            // Multi-frame variants use the second frame for that half-cycle; shipped single-frame
            // assets retain their only frame.
            bool secondState = (frame.Scene.Ticks % 45) >= 23;
            int index = secondState && battery.Asset.Frames.Count > 1 ? 1 : 0;
            SpriteFrame batteryFrame = battery.Asset.Frames[index];
            float displayWidth = screenWidth * 0.07f;
            float spriteScale = displayWidth / Math.Max(1f, batteryFrame.Width);
            battery.DrawFrame(
                batch,
                batteryFrame,
                screenWidth * 0.10f,
                screenHeight * 0.20f,
                spriteScale,
                spriteScale,
                new Bgra32(128, 128, 128, 255));
        }
    }

    private void DrawStatusMessage(
        SpriteBatch batch,
        BitmapTextRenderer font,
        in StatusMessageDisplay message,
        float x,
        float y,
        float scale)
    {
        // A portrait (0x4860F0) is drawn at the line's corner once the panel has opened, the text set after it and the
        // panel as tall as it.
        SpriteResource? portrait = message.Portrait.Length > 0 ? _sprite(message.Portrait) : null;
        SpriteFrame? portraitFrame = portrait is { Asset.Frames.Count: > 0 } ? portrait.Asset.Frames[0] : null;
        if (portrait is not null && portraitFrame is { } shown && message.DisplayFrame > 9)
        {
            portrait.DrawFrame(batch, shown, x, y, scale, scale, BitmapTextRenderer.NeutralColor);
        }

        // The panel, drawn open from its own centre. Its width and height are independent factors that
        // finish at different times, so for the first couple of ticks this is a zero-width line and
        // nothing shows at all -- see StatusScreenLayout.MessageHeightScale.
        float halfWidth = StatusScreenLayout.MessagePanelHalfWidth * scale;
        float centreX = x + halfWidth;
        float halfHeight = (portraitFrame?.Height ?? StatusScreenLayout.MessagePanelHeight) * 0.5f * scale;
        float centreY = y + halfHeight;
        float openWidth = halfWidth * StatusScreenLayout.MessageWidthScale(message.DisplayFrame);
        float openHeight = halfHeight * StatusScreenLayout.MessageHeightScale(message.DisplayFrame);

        if (openWidth > 0f && openHeight > 0f)
        {
            byte alpha = (byte)Math.Clamp(
                StatusScreenLayout.MessagePanelAlpha(message.DisplayFrame) * 255f, 0f, 255f);
            Bgra32 panel = new(0, 0, 0, alpha);
            batch.DrawQuad(
                TextureHandle.None,
                new Vertex2D(centreX - openWidth, centreY - openHeight, 0f, 0f, panel),
                new Vertex2D(centreX + openWidth, centreY - openHeight, 1f, 0f, panel),
                new Vertex2D(centreX + openWidth, centreY + openHeight, 1f, 1f, panel),
                new Vertex2D(centreX - openWidth, centreY + openHeight, 0f, 1f, panel));
        }

        // Nothing is typed until the panel has finished opening, so there is never text over a panel that
        // is still narrower than it is.
        if (message.RevealedCharacters <= 0)
        {
            return;
        }

        DrawWrapped(
            batch,
            font,
            message.Text.AsSpan(0, message.RevealedCharacters),
            x + (((portraitFrame?.Width ?? 0) + StatusScreenLayout.MessageTextInset) * scale),
            y + (StatusScreenLayout.MessageTextDrop * scale),
            StatusScreenLayout.MessageWrapWidth * scale,
            scale,
            cutscene: false);
    }

    private static void DrawSubtitle(
        SpriteBatch batch,
        BitmapTextRenderer font,
        in StatusMessageDisplay message,
        int screenWidth,
        int screenHeight,
        float scale)
    {
        if (message.RevealedCharacters <= 0)
        {
            return;
        }

        // No panel and no reveal: a cutscene line is whole from its first frame and runs nearly the full
        // width of the screen, because it is a subtitle rather than a readout.
        DrawWrapped(
            batch,
            font,
            message.Text.AsSpan(0, message.RevealedCharacters),
            StatusScreenLayout.MessageLeft(screenWidth),
            StatusScreenLayout.SubtitleY(screenHeight),
            screenWidth * StatusScreenLayout.SubtitleWrapFraction,
            scale,
            cutscene: true);
    }

    /// <summary>Draws text broken at spaces to a maximum width, in the message colour.</summary>
    /// <remarks>
    /// Greedy, and measured against the same font the line is drawn with, which is what the original's
    /// re-layout amounts to. A word longer than the limit overhangs rather than being split.
    /// </remarks>
    private static void DrawWrapped(
        SpriteBatch batch,
        BitmapTextRenderer font,
        ReadOnlySpan<char> text,
        float x,
        float y,
        float maxWidth,
        float scale,
        bool cutscene)
    {
        Bgra32 ink;
        if (cutscene)
        {
            // The original's text-object colour uses a different 128-based modulation convention.
            // Our sprite shader uses ordinary 0-255 multiplication, so preserving that literal value
            // incorrectly halves the supplied retail font's brightness. Full white preserves the
            // source pixels; unlike the old code this remains a single pass, with no soft shadow.
            ink = new Bgra32(255, 255, 255, 255);
        }
        else
        {
            (float R, float G, float B) colour = StatusScreenLayout.MessageColour;
            ink = new Bgra32((byte)colour.B, (byte)colour.G, (byte)colour.R, 255);
        }
        Bgra32 shadow = new(0, 0, 0, 190);
        float step = font.LineHeight * scale;

        int lineStart = 0;
        int lastBreak = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool end = i == text.Length;
            if (!end && text[i] != ' ')
            {
                continue;
            }

            // Measured from the start of the line to here; if that no longer fits, the previous space is
            // where the line ends.
            if (font.Measure(text[lineStart..i], scale, scale).Width > maxWidth && lastBreak > lineStart)
            {
                ReadOnlySpan<char> line = text[lineStart..lastBreak];
                if (!cutscene)
                {
                    font.Draw(batch, line, x + scale, y + scale, shadow, scale, scale);
                }
                font.Draw(batch, line, x, y, ink, scale, scale);
                y += step;
                lineStart = lastBreak + 1;
            }

            lastBreak = i;
        }

        if (lineStart < text.Length)
        {
            ReadOnlySpan<char> tail = text[lineStart..];
            if (!cutscene)
            {
                font.Draw(batch, tail, x + scale, y + scale, shadow, scale, scale);
            }
            font.Draw(batch, tail, x, y, ink, scale, scale);
        }
    }
}
