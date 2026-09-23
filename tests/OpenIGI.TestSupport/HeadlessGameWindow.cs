using OpenIGI.Input;
using OpenIGI.Platform;

namespace OpenIGI.TestSupport;

/// <summary>
/// A game window with nothing behind it: a fixed size, no events of its own, and input only what a test
/// puts there.
/// </summary>
/// <remarks>
/// For driving the whole host in a test. It never closes itself -- the host's own frame limit, or the
/// test calling <see cref="Close"/>, is what ends a run -- and it counts the pumps, which is how a test
/// can tell a step reached the top of a frame.
/// </remarks>
public sealed class HeadlessGameWindow : IGameWindow
{
    /// <summary>Initialises a new instance of the <see cref="HeadlessGameWindow"/> class.</summary>
    /// <param name="width">The client width it reports.</param>
    /// <param name="height">The client height it reports.</param>
    public HeadlessGameWindow(int width = 640, int height = 480)
    {
        Width = width;
        Height = height;
    }

    /// <inheritdoc/>
    public event EventHandler<WindowResizedEventArgs>? Resized;

    /// <inheritdoc/>
    public event EventHandler? CloseRequested;

    /// <inheritdoc/>
    public string Title { get; set; } = string.Empty;

    /// <inheritdoc/>
    public int Width { get; private set; }

    /// <inheritdoc/>
    public int Height { get; private set; }

    /// <inheritdoc/>
    public WindowMode Mode { get; set; } = WindowMode.Windowed;

    /// <inheritdoc/>
    public int RefreshRate => 60;

    /// <inheritdoc/>
    public bool CursorVisible { get; set; } = true;

    /// <inheritdoc/>
    public bool CursorLocked { get; set; }

    /// <inheritdoc/>
    public string ClipboardText { get; set; } = string.Empty;

    /// <inheritdoc/>
    public bool IsFocused => true;

    /// <inheritdoc/>
    public bool IsClosing { get; private set; }

    /// <inheritdoc/>
    public InputState Input { get; } = new();

    /// <summary>Gets how many times the host has pumped this window's events.</summary>
    public int Pumps { get; private set; }

    /// <inheritdoc/>
    public IReadOnlyList<(int Width, int Height, int RefreshRate)> GetDisplayModes() => [(Width, Height, RefreshRate)];

    /// <inheritdoc/>
    public void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        Resized?.Invoke(this, new WindowResizedEventArgs(width, height));
    }

    /// <inheritdoc/>
    public void Show()
    {
    }

    /// <inheritdoc/>
    public void PumpEvents() => Pumps++;

    /// <inheritdoc/>
    public void Close()
    {
        IsClosing = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>A platform whose windows are <see cref="HeadlessGameWindow"/>s.</summary>
public sealed class HeadlessPlatformHost : IPlatformHost
{
    /// <inheritdoc/>
    public string Name => "headless";

    /// <inheritdoc/>
    public IGameWindow CreateWindow(in WindowSettings settings) =>
        new HeadlessGameWindow(Math.Max(settings.Width, 1), Math.Max(settings.Height, 1));
}
