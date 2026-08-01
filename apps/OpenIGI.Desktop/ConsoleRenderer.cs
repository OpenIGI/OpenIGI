using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop;

/// <summary>
/// Draws the developer console.
/// </summary>
/// <remarks>
/// <para>
/// An interim view, set in the game's own bitmap face so the console is usable from the first stage of
/// the remaster track rather than waiting for the SDF text system that stage R10 brings. The layout
/// choices — a panel over the top of the screen, a caret on the input line, a suggestion strip — are
/// the ones the final view will keep; only the typography changes.
/// </para>
/// <para>
/// It loads its own font rather than borrowing the HUD's, because the console has to work at the main
/// menu and while a level is still loading, when there is no HUD.
/// </para>
/// </remarks>
public sealed class ConsoleRenderer : IDisposable
{
    private const string LogCategory = "Console";

    /// <summary>The archive the console's font comes out of.</summary>
    public const string FontArchive = "LOCAL:menusystem/menusystem.res";

    /// <summary>The font the console is set in — the smallest of the three the game ships.</summary>
    public const string FontResource = "font1.fnt";

    /// <summary>The fraction of the window height the panel covers.</summary>
    private const float PanelFraction = 0.45f;

    private readonly IGraphicsDevice _device;
    private readonly IIgiLogger _logger;
    private BitmapTextRenderer? _font;
    private SpriteResource? _fontSprite;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ConsoleRenderer"/> class.</summary>
    /// <param name="device">The device the font is uploaded to.</param>
    /// <param name="logger">Where to report a missing font.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public ConsoleRenderer(IGraphicsDevice device, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets a value indicating whether the console has a font and can draw.</summary>
    public bool IsReady => _font is not null;

    /// <summary>Gets how many lines of scrollback the last draw had room for.</summary>
    /// <remarks>The model needs this to clamp scrolling, and it depends on the window size.</remarks>
    public int VisibleLines { get; private set; } = 20;

    /// <summary>Reads the console's font.</summary>
    /// <param name="fileSystem">Where to read it from.</param>
    /// <returns><see langword="false"/> when the font is missing or unreadable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    public bool Load(VirtualFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        try
        {
            if (!fileSystem.Exists(FontArchive))
            {
                _logger.Warning(LogCategory, $"No font archive at {FontArchive}; the console cannot draw.");
                return false;
            }

            IresArchive archive = IresArchive.Read(fileSystem.ReadAllBytes(FontArchive));
            IresEntry? entry = archive.Entries.FirstOrDefault(
                e => e.Kind == IresEntryKind.Body
                    && Path.GetFileName(e.Name).Equals(FontResource, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                _logger.Warning(LogCategory, $"No '{FontResource}' in {FontArchive}; the console cannot draw.");
                return false;
            }

            SpriteAsset asset = SpriteLoader.Load(entry.Data, FontResource);
            _fontSprite = SpriteResource.Upload(_device, asset);

            if (!_fontSprite.IsFont)
            {
                _logger.Warning(LogCategory, $"'{FontResource}' is not a font.");
                return false;
            }

            _font = new BitmapTextRenderer(_fontSprite);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or EngineFatalException)
        {
            _logger.Warning(LogCategory, $"Could not read the console font: {ex.Message}");
            return false;
        }
    }

    /// <summary>Draws the console.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="model">The console state to draw.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="scale">How much to magnify the text.</param>
    /// <param name="caretVisible">Whether the caret is in its visible blink phase.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(
        SpriteBatch batch,
        ConsoleModel model,
        int screenWidth,
        int screenHeight,
        float scale = 1f,
        bool caretVisible = true)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(model);

        if (_font is not { } font || !model.IsOpen)
        {
            return;
        }

        float step = font.LineHeight * scale;
        float pad = 8f * scale;
        float panelHeight = MathF.Max(step * 6f, screenHeight * PanelFraction);
        float promptTop = panelHeight - step - pad;

        Fill(batch, 0f, 0f, screenWidth, panelHeight, new Bgra32(6, 9, 12, 232));

        // A bright rule under the panel and a dim one above the prompt: the whole visual identity of a
        // modern console panel is two hairlines and a lot of restraint.
        Fill(batch, 0f, panelHeight - (2f * scale), screenWidth, panelHeight, new Bgra32(120, 190, 255, 210));
        Fill(batch, pad, promptTop - (2f * scale), screenWidth - pad, promptTop - scale, new Bgra32(60, 80, 100, 160));

        int room = Math.Max(1, (int)((promptTop - pad) / step));
        VisibleLines = room;

        int end = Math.Max(0, model.Lines.Count - model.ScrollOffset);
        int start = Math.Max(0, end - room);
        float y = promptTop - (2f * scale) - ((end - start) * step);

        for (int i = start; i < end; i++)
        {
            ConsoleLine line = model.Lines[i];
            font.Draw(batch, line.Text, pad + scale, y + scale, new Bgra32(0, 0, 0, 200), scale, scale);
            font.Draw(batch, line.Text, pad, y, ColourFor(line.Severity), scale, scale);
            y += step;
        }

        DrawPrompt(batch, font, model, screenWidth, promptTop, pad, step, scale, caretVisible);

        // Scrolled away from the newest output, say so rather than leaving the player wondering why
        // their command produced nothing.
        if (model.ScrollOffset > 0)
        {
            string note = $"-- scrolled back {model.ScrollOffset} line(s) --";
            (float width, _) = font.Measure(note, scale, scale);
            font.Draw(batch, note, screenWidth - width - pad, pad, new Bgra32(255, 200, 90, 220), scale, scale);
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
        _fontSprite?.Dispose();
        _fontSprite = null;
        _font = null;
    }

    private static void DrawPrompt(
        SpriteBatch batch,
        BitmapTextRenderer font,
        ConsoleModel model,
        int screenWidth,
        float promptTop,
        float pad,
        float step,
        float scale,
        bool caretVisible)
    {
        const string Prompt = "] ";
        font.Draw(batch, Prompt, pad, promptTop, new Bgra32(120, 190, 255, 255), scale, scale);

        (float promptWidth, _) = font.Measure(Prompt, scale, scale);
        float textLeft = pad + promptWidth;
        font.Draw(batch, model.Input, textLeft, promptTop, new Bgra32(235, 240, 245, 255), scale, scale);

        if (caretVisible)
        {
            (float caretOffset, _) = font.Measure(model.Input.AsSpan(0, model.CaretIndex), scale, scale);
            float caretX = textLeft + caretOffset;
            Fill(batch, caretX, promptTop, caretX + MathF.Max(1f, scale), promptTop + step, new Bgra32(120, 190, 255, 220));
        }

        if (model.Suggestions.Count == 0)
        {
            return;
        }

        // The strip sits below the prompt, inside the panel's shadow, and is truncated rather than
        // wrapped: it is a hint, not a listing. 'cvarlist' is there for the listing.
        int shown = Math.Min(model.Suggestions.Count, 8);
        string hint = string.Join("   ", model.Suggestions.Take(shown));
        if (model.Suggestions.Count > shown)
        {
            hint += $"   (+{model.Suggestions.Count - shown} more)";
        }

        float y = promptTop + step;
        Fill(batch, 0f, y, screenWidth, y + step, new Bgra32(6, 9, 12, 200));
        font.Draw(batch, hint, pad, y, new Bgra32(150, 165, 180, 230), scale, scale);
    }

    private static Bgra32 ColourFor(ConsoleSeverity severity) => severity switch
    {
        ConsoleSeverity.Echo => new Bgra32(120, 190, 255, 255),
        ConsoleSeverity.Notice => new Bgra32(140, 220, 170, 255),
        ConsoleSeverity.Warning => new Bgra32(255, 200, 90, 255),
        ConsoleSeverity.Error => new Bgra32(255, 110, 110, 255),
        _ => new Bgra32(205, 212, 220, 255),
    };

    private static void Fill(SpriteBatch batch, float left, float top, float right, float bottom, Bgra32 colour) =>
        batch.DrawQuad(
            TextureHandle.None,
            new Vertex2D(left, top, 0f, 0f, colour),
            new Vertex2D(right, top, 1f, 0f, colour),
            new Vertex2D(right, bottom, 1f, 1f, colour),
            new Vertex2D(left, bottom, 0f, 1f, colour));
}
