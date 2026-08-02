using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Rendering;
using OpenIGI.UI;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Text;

namespace OpenIGI.Desktop;

/// <summary>
/// Draws the developer console through <c>OpenIGI.UI</c>: a translucent panel set in Saira, with
/// severity and syntax colouring, an inline completion ghost and a suggestion strip.
/// </summary>
/// <remarks>
/// <para>
/// This is the R10 reskin of the interim bitmap-font console renderer. It keeps that view's layout —
/// a panel over the top of the screen, two hairlines, a caret on the prompt, a suggestion strip — and
/// changes only how it is drawn: through the framework's <see cref="UiPainter"/> and a
/// signed-distance-capable <see cref="UiFont"/> rather than the game's bitmap face. Being the first
/// client of the framework, it is the one already there to compare against, which is exactly why the
/// stage sequencing puts it first.
/// </para>
/// <para>
/// Text is sized in back-buffer pixels rather than design units, and so is legible and identical at
/// every window size and aspect ratio: the console is a developer overlay, not part of the reference-
/// resolution layout, and a fixed on-screen size is what a tool wants.
/// </para>
/// </remarks>
public sealed class ConsoleUiRenderer : IDisposable
{
    private const string LogCategory = "Console";
    private const float PanelFraction = 0.5f;
    private const int MaxSuggestions = 10;

    private readonly IGraphicsDevice _device;
    private readonly ConsoleSystem _system;
    private readonly IIgiLogger _logger;
    private UiFont? _font;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ConsoleUiRenderer"/> class.</summary>
    /// <param name="device">The device the font atlas is uploaded to.</param>
    /// <param name="system">The console system, used to colour a command differently from a variable.</param>
    /// <param name="logger">Where to report a missing font.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ConsoleUiRenderer(IGraphicsDevice device, ConsoleSystem system, IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(system);
        _device = device;
        _system = system;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets a value indicating whether the console has a font and can draw.</summary>
    public bool IsReady => _font is not null;

    /// <summary>Gets how many scrollback lines the last draw had room for, for the model's scroll clamp.</summary>
    public int VisibleLines { get; private set; } = 20;

    /// <summary>Builds the console font from the shipped Saira face.</summary>
    /// <returns><see langword="false"/> when the font is missing.</returns>
    public bool Load()
    {
        if (!UiFontLibrary.Exists(UiFontLibrary.Body))
        {
            _logger.Warning(LogCategory, $"No console font at {UiFontLibrary.FontsDirectory}; the console cannot draw.");
            return false;
        }

        try
        {
            _font = UiFontLibrary.Load(_device, UiFontLibrary.Body, pixelHeight: 48f);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _logger.Warning(LogCategory, $"Could not build the console font: {ex.Message}");
            return false;
        }
    }

    /// <summary>Draws the console when it is open.</summary>
    /// <param name="batch">The batch to draw into; it must already be between Begin and End.</param>
    /// <param name="model">The console state.</param>
    /// <param name="screenWidth">The back buffer's width.</param>
    /// <param name="screenHeight">The back buffer's height.</param>
    /// <param name="caretVisible">Whether the caret is in its visible blink phase.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public void Draw(SpriteBatch batch, ConsoleModel model, int screenWidth, int screenHeight, bool caretVisible)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(model);

        if (_font is not { } font || !model.IsOpen)
        {
            return;
        }

        var context = new LayoutContext(screenWidth, screenHeight);
        var painter = new UiPainter(batch, context);

        // Text scales gently with resolution and is clamped so it stays legible from a 640x480 window to
        // a 4K display.
        float size = Math.Clamp(screenHeight / 44f, 15f, 32f);
        float line = font.LineHeight(size);
        float pad = MathF.Round(size * 0.6f);
        float panelHeight = MathF.Max(line * 6f, screenHeight * PanelFraction);
        float promptTop = panelHeight - line - pad;

        painter.FillRect(new UiRect(0f, 0f, screenWidth, panelHeight), Panel, BlendMode.AlphaBlend);
        painter.FillRect(new UiRect(0f, panelHeight - 2f, screenWidth, 2f), Accent.WithAlpha(220), BlendMode.AlphaBlend);
        painter.FillRect(new UiRect(pad, promptTop - 2f, screenWidth - (pad * 2f), 1f), Border, BlendMode.AlphaBlend);

        int room = Math.Max(1, (int)((promptTop - pad) / line));
        VisibleLines = room;

        // Scrollback, newest at the bottom, offset by the model's scroll.
        int end = Math.Max(0, model.Lines.Count - model.ScrollOffset);
        int start = Math.Max(0, end - room);
        float y = promptTop - 2f - ((end - start) * line);
        painter.PushClip(new UiRect(0f, 0f, screenWidth, promptTop - 2f));
        for (int i = start; i < end; i++)
        {
            ConsoleLine entry = model.Lines[i];
            painter.DrawTextShadowed(font, entry.Text, new UiVec2(pad, y), size, ColourFor(entry.Severity), Shadow, new UiVec2(1f, 1f));
            y += line;
        }

        painter.PopClip();

        DrawPrompt(painter, font, model, screenWidth, promptTop, pad, line, size, caretVisible);

        if (model.ScrollOffset > 0)
        {
            string note = $"-- scrolled back {model.ScrollOffset} line(s) --";
            float width = font.MeasureWidth(note, size);
            painter.DrawText(font, note, new UiVec2(screenWidth - width - pad, pad), size, Accent);
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
        _font?.Dispose();
        _font = null;
    }

    private void DrawPrompt(
        UiPainter painter,
        UiFont font,
        ConsoleModel model,
        int screenWidth,
        float promptTop,
        float pad,
        float line,
        float size,
        bool caretVisible)
    {
        const string prompt = "] ";
        painter.DrawText(font, prompt, new UiVec2(pad, promptTop), size, Accent);
        float textLeft = pad + font.MeasureWidth(prompt, size);

        DrawSyntaxColoured(painter, font, model.Input, textLeft, promptTop, size);

        // The inline completion ghost: the remainder of the best suggestion, shown dim behind the caret,
        // when the input is a prefix of it.
        if (model.Suggestions.Count > 0 && model.Input.Length > 0)
        {
            string best = model.Suggestions[0];
            if (best.Length > model.Input.Length &&
                best.StartsWith(model.Input, StringComparison.OrdinalIgnoreCase))
            {
                float inputWidth = font.MeasureWidth(model.Input, size);
                painter.DrawText(font, best.AsSpan(model.Input.Length), new UiVec2(textLeft + inputWidth, promptTop), size, Ghost);
            }
        }

        if (caretVisible)
        {
            float caretOffset = font.MeasureWidth(model.Input.AsSpan(0, model.CaretIndex), size);
            painter.FillRect(new UiRect(textLeft + caretOffset, promptTop, MathF.Max(1f, size * 0.08f), line), Accent.WithAlpha(220), BlendMode.AlphaBlend);
        }

        if (model.Suggestions.Count == 0)
        {
            return;
        }

        int shown = Math.Min(model.Suggestions.Count, MaxSuggestions);
        string hint = string.Join("   ", model.Suggestions.Take(shown));
        if (model.Suggestions.Count > shown)
        {
            hint += $"   (+{model.Suggestions.Count - shown} more)";
        }

        float y = promptTop + line;
        painter.FillRect(new UiRect(0f, y, screenWidth, line), Panel.WithAlpha(200), BlendMode.AlphaBlend);
        painter.DrawText(font, hint, new UiVec2(pad, y), size, Suggestion);
    }

    private void DrawSyntaxColoured(UiPainter painter, UiFont font, string input, float x, float y, float size)
    {
        if (input.Length == 0)
        {
            return;
        }

        int space = input.IndexOf(' ', StringComparison.Ordinal);
        string firstToken = space < 0 ? input : input[..space];
        Bgra32 tokenColour = ClassifyToken(firstToken);

        painter.DrawText(font, firstToken, new UiVec2(x, y), size, tokenColour);
        if (space >= 0)
        {
            float head = font.MeasureWidth(input.AsSpan(0, space), size);
            painter.DrawText(font, input.AsSpan(space), new UiVec2(x + head, y), size, Text);
        }
    }

    private Bgra32 ClassifyToken(string token)
    {
        if (_system.Commands.TryGet(token, out _))
        {
            return CommandColour;
        }

        return _system.Variables.Contains(token) ? CvarColour : Text;
    }

    private static Bgra32 ColourFor(ConsoleSeverity severity) => severity switch
    {
        ConsoleSeverity.Echo => Accent,
        ConsoleSeverity.Notice => Bgra32.FromRgb(140, 220, 170),
        ConsoleSeverity.Warning => Bgra32.FromRgb(255, 200, 90),
        ConsoleSeverity.Error => Bgra32.FromRgb(255, 110, 110),
        _ => Text,
    };

    private static Bgra32 Panel => Bgra32.FromRgba(8, 11, 14, 232);
    private static Bgra32 Accent => Bgra32.FromRgb(240, 176, 84);
    private static Bgra32 Text => Bgra32.FromRgb(224, 230, 236);
    private static Bgra32 Border => Bgra32.FromRgba(120, 134, 146, 150);
    private static Bgra32 Ghost => Bgra32.FromRgba(120, 130, 140, 200);
    private static Bgra32 Suggestion => Bgra32.FromRgb(150, 165, 180);
    private static Bgra32 Shadow => Bgra32.FromRgba(0, 0, 0, 180);
    private static Bgra32 CommandColour => Bgra32.FromRgb(140, 200, 255);
    private static Bgra32 CvarColour => Bgra32.FromRgb(150, 220, 170);
}
