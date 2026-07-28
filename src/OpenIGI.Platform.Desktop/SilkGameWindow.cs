using OpenIGI.Input;
using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace OpenIGI.Platform.Desktop;

/// <summary>
/// A desktop game window backed by Silk.NET windowing, with its input events fed into an
/// <see cref="InputState"/>.
/// </summary>
/// <remarks>
/// <para>
/// The window is created and initialised eagerly so that a GL context exists before the caller builds
/// a graphics device from <see cref="GLContext"/>. It does <em>not</em> run Silk's own render loop:
/// the game loop owns the frame, exactly as the original's <c>WinMain</c> message loop does, and
/// <see cref="PumpEvents"/> is the message pump.
/// </para>
/// <para>
/// Presentation is <see cref="SwapBuffers"/>, which the composition root hands to the graphics device
/// as its present callback.
/// </para>
/// </remarks>
public sealed class SilkGameWindow : IGameWindow
{
    private readonly IWindow _window;
    private readonly IInputContext _input;
    private bool _cursorVisible;
    private bool _disposed;

    internal SilkGameWindow(in WindowSettings settings)
    {
        WindowOptions options = WindowOptions.Default with
        {
            Title = settings.Title,
            Size = new Vector2D<int>(settings.Width, settings.Height),
            WindowState = settings.Fullscreen ? WindowState.Fullscreen : WindowState.Normal,
            VSync = settings.VSync,
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
            IsVisible = false,
            ShouldSwapAutomatically = false,
        };

        try
        {
            _window = Window.Create(options);
            _window.Initialize();
        }
        catch (Exception ex)
        {
            throw new PlatformException(
                $"Could not create a {settings.Width}x{settings.Height} OpenGL 3.3 window. "
                + "A driver supporting OpenGL 3.3 core is required.",
                ex);
        }

        if (_window.GLContext is null)
        {
            _window.Dispose();
            throw new PlatformException("The window was created without an OpenGL context.");
        }

        _input = _window.CreateInput();
        _cursorVisible = settings.CursorVisible;

        HookEvents();
        ApplyCursorMode();
    }

    /// <inheritdoc/>
    public event EventHandler<WindowResizedEventArgs>? Resized;

    /// <inheritdoc/>
    public event EventHandler? CloseRequested;

    /// <summary>Gets the OpenGL context this window owns, for building a graphics device against.</summary>
    public IGLContext GLContext => _window.GLContext!;

    /// <inheritdoc/>
    public string Title
    {
        get => _window.Title;
        set => _window.Title = value;
    }

    /// <inheritdoc/>
    public int Width => _window.FramebufferSize.X;

    /// <inheritdoc/>
    public int Height => _window.FramebufferSize.Y;

    /// <inheritdoc/>
    public bool IsFullscreen
    {
        get => _window.WindowState == WindowState.Fullscreen;
        set => _window.WindowState = value ? WindowState.Fullscreen : WindowState.Normal;
    }

    /// <inheritdoc/>
    public bool CursorVisible
    {
        get => _cursorVisible;
        set
        {
            _cursorVisible = value;
            ApplyCursorMode();
        }
    }

    /// <inheritdoc/>
    public bool IsFocused { get; private set; } = true;

    /// <inheritdoc/>
    public bool IsClosing => _window.IsClosing;

    /// <inheritdoc/>
    public InputState Input { get; } = new();

    /// <inheritdoc/>
    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.IsVisible = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Every mode the monitor reports, from <c>IMonitor.GetAllVideoModes</c>. The caller
    /// de-duplicates by size, because the menu's list is of resolutions rather than of modes and two
    /// entries differing only in refresh rate would read as a repeat.
    /// </remarks>
    public IReadOnlyList<(int Width, int Height, int RefreshRate)> GetDisplayModes()
    {
        IMonitor monitor = _window.Monitor ?? Silk.NET.Windowing.Monitor.GetMainMonitor(_window);
        List<(int Width, int Height, int RefreshRate)> modes = [];

        foreach (VideoMode mode in monitor.GetAllVideoModes())
        {
            if (mode.Resolution is { } size)
            {
                modes.Add((size.X, size.Y, mode.RefreshRate ?? 0));
            }
        }

        // A monitor that reports nothing still has to offer the mode it is on.
        return modes.Count > 0 ? modes : [(Width, Height, 0)];
    }

    /// <inheritdoc/>
    public void Resize(int width, int height)
    {
        if (width > 0 && height > 0)
        {
            _window.Size = new Vector2D<int>(width, height);
        }
    }

    /// <inheritdoc/>
    public void PumpEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Shadow first, then dispatch: the events this call delivers belong to the frame about to run.
        Input.BeginFrame();
        _window.DoEvents();
    }

    /// <summary>Presents the back buffer.</summary>
    /// <remarks>Hand this to the graphics device as its present callback; it is the <c>Flip</c>/<c>Blt</c> of the original's swap.</remarks>
    public void SwapBuffers()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.GLContext!.SwapBuffers();
    }

    /// <inheritdoc/>
    public void Close()
    {
        if (!_disposed && !_window.IsClosing)
        {
            _window.Close();
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
        _input.Dispose();
        _window.Dispose();
    }

    private void HookEvents()
    {
        _window.Closing += () => CloseRequested?.Invoke(this, EventArgs.Empty);

        _window.FramebufferResize += size =>
            Resized?.Invoke(this, new WindowResizedEventArgs(size.X, size.Y));

        _window.FocusChanged += focused =>
        {
            IsFocused = focused;
            if (!focused)
            {
                // The original's device read fails when it loses focus and it zeroes the whole key
                // array; anything else leaves keys latched down for as long as the window is away.
                Input.Keyboard.Release();
                Input.Mouse.Reset();
            }
        };

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) => SetKey(key, down: true);
            keyboard.KeyUp += (_, key, _) => SetKey(key, down: false);
            keyboard.KeyChar += (_, character) => Input.Keyboard.EnqueueCharacter(character);
        }

        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseDown += (_, button) => SetButton(button, down: true);
            mouse.MouseUp += (_, button) => SetButton(button, down: false);
            mouse.MouseMove += (_, position) =>
            {
                Input.Mouse.AddMotion(position.X - Input.Mouse.X, position.Y - Input.Mouse.Y);
                Input.Mouse.SetPosition(position.X, position.Y);
            };
            mouse.Scroll += (_, wheel) => Input.Mouse.AddWheel(wheel.Y);
        }
    }

    private void SetKey(Key key, bool down)
    {
        KeyCode code = DesktopKeyMap.Translate(key);
        if (code != KeyCode.None)
        {
            Input.Keyboard.SetKey(code, down);
        }
    }

    private void SetButton(SilkMouseButton button, bool down)
    {
        OpenIGI.Input.MouseButton? mapped = button switch
        {
            SilkMouseButton.Left => OpenIGI.Input.MouseButton.Left,
            SilkMouseButton.Right => OpenIGI.Input.MouseButton.Right,
            SilkMouseButton.Middle => OpenIGI.Input.MouseButton.Middle,
            SilkMouseButton.Button4 => OpenIGI.Input.MouseButton.Extra1,
            SilkMouseButton.Button5 => OpenIGI.Input.MouseButton.Extra2,
            _ => null,
        };

        if (mapped is not null)
        {
            Input.Mouse.SetButton(mapped.Value, down);
        }
    }

    private void ApplyCursorMode()
    {
        foreach (IMouse mouse in _input.Mice)
        {
            mouse.Cursor.CursorMode = _cursorVisible ? CursorMode.Normal : CursorMode.Hidden;
        }
    }
}
