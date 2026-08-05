using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Desktop;

/// <summary>
/// The remaster's presentation of the mission's status messages: the comms cards that slide in at the top
/// left when something happens, and the subtitle line that carries a spoken one.
/// </summary>
/// <remarks>
/// <para>
/// The same messages, the same moments, a different surface. Timing, ordering and wrapping still come
/// from the simulation — <see cref="MissionObjectiveRuntime.DisplayedMessages"/> is what the original's
/// slot array produces, typewriter and all — so this is a reskin in the strict sense: nothing here can
/// make a message arrive earlier, hold longer, or appear in a different order than the retail path shows
/// it in. <see cref="StatusScreenRenderer.DrawStatusMessages"/> is the faithful one; this is its
/// counterpart, chosen by <c>r_messages_tactical</c>.
/// </para>
/// <para>
/// <b>What is deliberately kept.</b> The two-stage open is retail's own idea and a good one, so the card
/// keeps it in spirit: it arrives by unfurling rather than by fading, and the text follows the panel
/// rather than sharing its entrance. What changes is the execution — the original's opening flash is one
/// 33 ms frame at three-quarters alpha, which on a modern display reads as a dropped frame rather than as
/// emphasis, so here it becomes a short accent sweep along the card's leading edge.
/// </para>
/// <para>
/// <b>What is deliberately dropped.</b> The 235-pixel panel width and the 42-pixel step are authored
/// against 640x480 and are far too tight for a card set in Saira at a modern size, so this measures its
/// own: the card is as wide as it needs to be up to a limit, and the column steps by the height each card
/// actually took. A message that wraps to three lines therefore pushes the ones below it down instead of
/// overlapping them, which the fixed step cannot do.
/// </para>
/// </remarks>
internal sealed class StatusMessageRenderer : IDisposable
{
    private const string LogCategory = "Messages";

    /// <summary>How long a card spends sliding in, in ticks, matching the original's panel open.</summary>
    private const float OpenTicks = 9f;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private UiFont? _body;
    private UiFont? _label;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="StatusMessageRenderer"/> class.</summary>
    /// <param name="device">The device the font atlases are uploaded to.</param>
    /// <param name="logger">Where to report a missing font.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public StatusMessageRenderer(IGraphicsDevice device, IIgiLogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(logger);
        _device = device;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the renderer has its faces and can draw.</summary>
    public bool IsReady => _body is not null;

    /// <summary>Builds the faces the cards are set in.</summary>
    /// <returns><see langword="false"/> when the fonts are missing.</returns>
    public bool Load()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!UiFontLibrary.Exists(UiFontLibrary.Body))
        {
            _logger.Warning(LogCategory, $"No interface font at {UiFontLibrary.FontsDirectory}; messages cannot draw.");
            return false;
        }

        try
        {
            _body = UiFontLibrary.Load(_device, UiFontLibrary.Body, pixelHeight: 48f);
            _label = UiFontLibrary.Exists(UiFontLibrary.Emphasis)
                ? UiFontLibrary.Load(_device, UiFontLibrary.Emphasis, pixelHeight: 48f)
                : null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning(LogCategory, $"Could not build the message fonts: {ex.Message}");
            return false;
        }
    }

    /// <summary>Draws the messages on screen this frame.</summary>
    /// <param name="batch">The 2D batch, already begun under a 2D projection.</param>
    /// <param name="messages">The messages on screen, in slot order.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(
        SpriteBatch batch,
        IEnumerable<StatusMessageDisplay> messages,
        int screenWidth,
        int screenHeight)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(messages);

        if (_body is not { } body)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = new UiPainter(batch, context);

        float x = context.Px(34f);
        float y = context.Px(34f);
        float maxCardWidth = MathF.Min(context.Px(460f), screenWidth * 0.42f);

        foreach (StatusMessageDisplay message in messages)
        {
            if (message.IsCutscene)
            {
                DrawSubtitle(painter, context, body, message, screenWidth, screenHeight);
                continue;
            }

            y += DrawCard(painter, context, body, message, x, y, maxCardWidth) + context.Px(10f);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _body?.Dispose();
        _body = null;
        _label?.Dispose();
        _label = null;
    }

    /// <summary>Draws one comms card and returns how tall it was.</summary>
    private float DrawCard(
        UiPainter painter,
        LayoutContext context,
        UiFont body,
        in StatusMessageDisplay message,
        float x,
        float y,
        float maxWidth)
    {
        UiFont label = _label ?? body;
        float open = Math.Clamp(message.DisplayFrame / OpenTicks, 0f, 1f);
        float eased = 1f - MathF.Pow(1f - open, 3f);

        float textSize = context.Px(19f);
        float padX = context.Px(18f);
        float padY = context.Px(14f);
        float headerSize = context.Px(11f);

        // Measured against the full text rather than the revealed part, so the card is its final size
        // from the first frame and does not grow a line at a time as the message types itself out.
        float wrapWidth = maxWidth - (padX * 2f);
        string wrapped = body.WrapText(message.Text, textSize, wrapWidth);
        float textHeight = body.Measure(wrapped, textSize).Y;
        float width = MathF.Min(maxWidth, body.Measure(wrapped, textSize).X + (padX * 2f));
        float height = textHeight + (padY * 2f) + context.Px(18f);

        // The entrance: the card unfurls to its width and slides a little way in from the left, which is
        // the direction the column reads from.
        float drawnWidth = width * eased;
        float slide = (1f - eased) * context.Px(22f);
        var card = new UiRect(x - slide, y, drawnWidth, height);
        if (drawnWidth <= 1f)
        {
            return height;
        }

        painter.PushOpacity(eased);
        painter.FillRect(card, Panel);
        painter.FillRect(new UiRect(card.X, card.Y, context.Px(3f), card.Height), Accent);
        painter.StrokeRect(card, MathF.Max(1f, context.Px(1f)), Border);

        // The accent sweep that replaces retail's single-frame flash: a bright band riding the card's
        // opening edge, gone by the time the card is fully out.
        if (open < 1f)
        {
            painter.FillRect(
                new UiRect(card.Right - context.Px(2f), card.Y, context.Px(2f), card.Height),
                Accent.WithAlpha(220));
        }

        painter.PushClip(card);
        DrawTracked(
            painter,
            label,
            "TRANSMISSION",
            new UiVec2(card.X + padX, card.Y + context.Px(11f)),
            headerSize,
            Accent.WithAlpha(200),
            context.Px(2.2f));

        // The typewriter. Only what has been typed is laid out, re-wrapped from scratch each frame the
        // way the original re-lays its three lines on every revealed character -- so a word that no
        // longer fits moves down as its last letter lands, rather than the whole line being decided in
        // advance and letters appearing into a gap that was already reserved for them.
        int revealed = Math.Clamp(message.RevealedCharacters, 0, message.Text.Length);
        var textAt = new UiVec2(card.X + padX, card.Y + padY + context.Px(18f));
        if (revealed > 0)
        {
            string typed = body.WrapText(message.Text[..revealed], textSize, wrapWidth);
            painter.DrawText(body, typed, textAt, textSize, Text);

            // The caret, while there is still text to come. Solid rather than blinking: it is marking
            // where the line is being written, and a blink at this speed reads as a rendering fault.
            if (revealed < message.Text.Length)
            {
                DrawCaret(painter, context, body, typed, textAt, textSize);
            }
        }

        painter.PopClip();
        painter.PopOpacity();
        return height;
    }

    private static void DrawSubtitle(
        UiPainter painter,
        LayoutContext context,
        UiFont body,
        in StatusMessageDisplay message,
        int screenWidth,
        int screenHeight)
    {
        if (message.RevealedCharacters <= 0)
        {
            return;
        }

        float textSize = context.Px(24f);
        float maxWidth = MathF.Min(screenWidth * 0.72f, context.Px(1180f));
        string wrapped = body.WrapText(message.Text, textSize, maxWidth);
        UiVec2 size = body.Measure(wrapped, textSize);

        // Centred and lifted a little clear of the bottom, which is where a viewer expects a subtitle --
        // the original's left-aligned line at 415/480 is a readout convention rather than a spoken one.
        float x = (screenWidth - size.X) * 0.5f;
        float y = (screenHeight * StatusScreenLayout.SubtitleYFraction) - (size.Y * 0.5f);

        float padX = context.Px(20f);
        float padY = context.Px(10f);
        painter.FillRect(
            new UiRect(x - padX, y - padY, size.X + (padX * 2f), size.Y + (padY * 2f)),
            Bgra32.FromRgba(0, 0, 0, 150));

        painter.DrawTextShadowed(
            body,
            wrapped,
            new UiVec2(x, y),
            textSize,
            Bgra32.FromRgb(238, 244, 248),
            Bgra32.FromRgba(0, 0, 0, 200),
            new UiVec2(context.Px(1.5f), context.Px(1.5f)));
    }

    /// <summary>Draws the writing caret at the end of the last typed line.</summary>
    /// <param name="painter">The surface to draw into.</param>
    /// <param name="context">The frame's layout context.</param>
    /// <param name="font">The face the text is set in.</param>
    /// <param name="typed">The text laid out so far, line feeds and all.</param>
    /// <param name="textAt">Where that text's first line starts.</param>
    /// <param name="size">The text size in pixels.</param>
    private static void DrawCaret(
        UiPainter painter,
        LayoutContext context,
        UiFont font,
        string typed,
        UiVec2 textAt,
        float size)
    {
        int lastFeed = typed.LastIndexOf('\n');
        ReadOnlySpan<char> lastLine = lastFeed < 0 ? typed : typed.AsSpan(lastFeed + 1);

        int line = 0;
        foreach (char c in typed)
        {
            if (c == '\n')
            {
                line++;
            }
        }

        float x = textAt.X + font.MeasureWidth(lastLine, size) + context.Px(2f);
        float y = textAt.Y + (line * font.LineHeight(size));
        painter.FillRect(new UiRect(x, y + context.Px(3f), context.Px(2f), size * 0.82f), Accent);
    }

    private static float DrawTracked(
        UiPainter painter,
        UiFont font,
        ReadOnlySpan<char> text,
        UiVec2 at,
        float size,
        Bgra32 colour,
        float tracking)
    {
        Span<char> one = stackalloc char[1];
        float x = at.X;
        foreach (char c in text)
        {
            one[0] = c;
            painter.DrawText(font, one, new UiVec2(x, at.Y), size, colour);
            x += font.MeasureWidth(one, size) + tracking;
        }

        return MathF.Max(0f, x - at.X - tracking);
    }

    private static Bgra32 Panel => Bgra32.FromRgba(8, 12, 15, 214);
    private static Bgra32 Accent => Bgra32.FromRgb(240, 176, 84);
    private static Bgra32 Border => Bgra32.FromRgba(120, 134, 146, 70);
    private static Bgra32 Text => Bgra32.FromRgb(226, 234, 240);
}
