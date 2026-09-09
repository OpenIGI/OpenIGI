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
/// The remaster's presentation of the mission's status messages: a minimal comms feed at the top left when
/// something happens, and the subtitle line that carries a spoken one.
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

    /// <summary>How long a feed item spends easing into place, matching the original's panel open.</summary>
    private const float OpenTicks = 9f;

    /// <summary>How many rendered frames a feed item takes to leave the screen.</summary>
    private const int ExitFrames = 12;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private readonly Dictionary<FeedKey, RetiringMessage> _retiring = [];
    private List<FeedItem> _previousFeed = [];
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

    /// <summary>Builds the faces the message feed is set in.</summary>
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
    /// <param name="topOffset">Extra vertical room reserved above the cards, in back-buffer pixels.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(
        SpriteBatch batch,
        IEnumerable<StatusMessageDisplay> messages,
        int screenWidth,
        int screenHeight,
        float topOffset = 0f)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(messages);

        if (_body is not { } body)
        {
            return;
        }

        var context = new LayoutContext(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
        var painter = new UiPainter(batch, context);

        List<FeedItem> currentFeed = BuildFeed(messages, out List<StatusMessageDisplay> subtitles);
        BeginRetiring(currentFeed);

        float x = context.Px(34f);
        float y = context.Px(34f) + Math.Max(0f, topOffset);
        float maxMessageWidth = MathF.Min(context.Px(520f), screenWidth * 0.42f);

        foreach (FeedItem item in currentFeed)
        {
            y += DrawMessage(painter, context, body, item.Message, x, y, maxMessageWidth, 1f) + context.Px(16f);
        }

        // The original removes a slot on a tick boundary.  Preserve its final displayed text for a few
        // rendered frames, moving it gently back toward the left as it dissolves instead of popping away.
        FeedKey[] expired = [];
        foreach ((FeedKey key, RetiringMessage retiring) in _retiring.OrderBy(entry => entry.Value.Order))
        {
            float exitOpacity = retiring.FramesRemaining / (float)ExitFrames;
            y += DrawMessage(painter, context, body, retiring.Message, x, y, maxMessageWidth, exitOpacity)
                + context.Px(16f);

            if (retiring.FramesRemaining <= 1)
            {
                expired = [.. expired, key];
            }
            else
            {
                _retiring[key] = retiring with { FramesRemaining = retiring.FramesRemaining - 1 };
            }
        }

        foreach (FeedKey key in expired)
        {
            _retiring.Remove(key);
        }

        foreach (StatusMessageDisplay subtitle in subtitles)
        {
            DrawSubtitle(painter, context, body, subtitle, screenWidth, screenHeight);
        }

        _previousFeed = currentFeed;
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
        _retiring.Clear();
        _previousFeed.Clear();
    }

    /// <summary>Draws one comms-feed item and returns how tall it was.</summary>
    private float DrawMessage(
        UiPainter painter,
        LayoutContext context,
        UiFont body,
        in StatusMessageDisplay message,
        float x,
        float y,
        float maxWidth,
        float lifecycleOpacity)
    {
        UiFont label = _label ?? body;
        float open = Math.Clamp(message.DisplayFrame / OpenTicks, 0f, 1f);
        float eased = 1f - MathF.Pow(1f - open, 3f);

        float textSize = context.Px(22f);
        float headerSize = context.Px(14f);
        float markerWidth = context.Px(3f);
        float textX = x + context.Px(15f);

        // Measured against the full text rather than the revealed part, so an item keeps a stable layout
        // from its first frame rather than growing a line at a time as the message types itself out.
        float wrapWidth = maxWidth - (textX - x);
        string wrapped = body.WrapText(message.Text, textSize, wrapWidth);
        float textHeight = body.Measure(wrapped, textSize).Y;
        float height = textHeight + context.Px(28f);

        // A very short lateral ease gives a new item presence without putting a panel behind the view.
        float slide = ((1f - eased) * context.Px(22f)) + ((1f - lifecycleOpacity) * context.Px(18f));

        painter.PushOpacity(eased * lifecycleOpacity);
        painter.FillRect(new UiRect(x - slide, y + context.Px(2f), markerWidth, height), Signal);
        DrawTracked(
            painter,
            label,
            "TRANSMISSION",
            new UiVec2(textX - slide, y),
            headerSize,
            Muted,
            context.Px(1.8f));

        // The typewriter. Only what has been typed is laid out, re-wrapped from scratch each frame the
        // way the original re-lays its three lines on every revealed character -- so a word that no
        // longer fits moves down as its last letter lands, rather than the whole line being decided in
        // advance and letters appearing into a gap that was already reserved for them.
        int revealed = Math.Clamp(message.RevealedCharacters, 0, message.Text.Length);
        var textAt = new UiVec2(textX - slide, y + context.Px(20f));
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

        painter.PopOpacity();
        return height;
    }

    private static List<FeedItem> BuildFeed(
        IEnumerable<StatusMessageDisplay> messages,
        out List<StatusMessageDisplay> subtitles)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var feed = new List<FeedItem>();
        subtitles = [];

        foreach (StatusMessageDisplay message in messages)
        {
            if (message.IsCutscene)
            {
                subtitles.Add(message);
                continue;
            }

            int occurrence = occurrences.GetValueOrDefault(message.Text);
            occurrences[message.Text] = occurrence + 1;
            feed.Add(new FeedItem(new FeedKey(message.Text, occurrence), message, feed.Count));
        }

        return feed;
    }

    private void BeginRetiring(IReadOnlyList<FeedItem> currentFeed)
    {
        var active = new HashSet<FeedKey>(currentFeed.Select(item => item.Key));
        foreach (FeedItem previous in _previousFeed)
        {
            if (!active.Contains(previous.Key) && !_retiring.ContainsKey(previous.Key))
            {
                StatusMessageDisplay complete = previous.Message with
                {
                    DisplayFrame = (int)OpenTicks,
                    RevealedCharacters = previous.Message.Text.Length,
                };
                _retiring.Add(previous.Key, new RetiringMessage(complete, ExitFrames, previous.Order));
            }
        }

        foreach (FeedItem activeItem in currentFeed)
        {
            _retiring.Remove(activeItem.Key);
        }
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

        float textSize = context.Px(27f);
        float maxWidth = MathF.Min(screenWidth * 0.72f, context.Px(1180f));
        string wrapped = body.WrapText(message.Text, textSize, maxWidth);
        UiVec2 size = body.Measure(wrapped, textSize);

        // Centred and lifted a little clear of the bottom, which is where a viewer expects a subtitle --
        // the original's left-aligned line at 415/480 is a readout convention rather than a spoken one.
        float x = (screenWidth - size.X) * 0.5f;
        float y = (screenHeight * StatusScreenLayout.SubtitleYFraction) - (size.Y * 0.5f);

        painter.DrawTextShadowed(
            body,
            wrapped,
            new UiVec2(x, y),
            textSize,
            Bgra32.FromRgb(248, 250, 252),
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
        painter.FillRect(new UiRect(x, y + context.Px(3f), context.Px(2f), size * 0.82f), Signal);
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

    // Mission objectives reserve amber; the comms feed uses a quieter signal-blue accent of its own.
    private static Bgra32 Signal => Bgra32.FromRgb(117, 202, 216);
    private static Bgra32 Text => Bgra32.FromRgb(245, 248, 250);
    private static Bgra32 Muted => Bgra32.FromRgb(202, 213, 220);

    private readonly record struct FeedKey(string Text, int Occurrence);

    private readonly record struct FeedItem(FeedKey Key, StatusMessageDisplay Message, int Order);

    private readonly record struct RetiringMessage(StatusMessageDisplay Message, int FramesRemaining, int Order);
}
