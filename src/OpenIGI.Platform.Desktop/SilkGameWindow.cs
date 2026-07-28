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
    private readonly Vector2D<int> _windowedSize;
    private WindowMode _mode;
    private bool _cursorVisible;
    private bool _cursorLocked;
    private bool _disposed;

    internal SilkGameWindow(in WindowSettings settings)
    {
        // The monitor has to be resolved before the window exists, because borderless is a size and a
        // position rather than a state and both come from it.
        IMonitor monitor = Silk.NET.Windowing.Monitor.GetMainMonitor(null);
        DesktopDisplay display = DesktopDisplay.Resolve(monitor, settings);

        _mode = settings.Mode;
        _windowedSize = settings.Width > 0 && settings.Height > 0
            ? new Vector2D<int>(settings.Width, settings.Height)
            : display.DesktopSize;

        WindowOptions options = WindowOptions.Default with
        {
            Title = settings.Title,
            Size = display.Size,
            WindowState = settings.Mode == WindowMode.ExclusiveFullscreen
                ? WindowState.Fullscreen
                : WindowState.Normal,

            // Borderless is exactly this: no decoration, and a client area placed over the whole of the
            // monitor's desktop area. Silk has no state for it, because as far as the platform is
            // concerned it *is* a normal window.
            WindowBorder = settings.Mode == WindowMode.BorderlessFullscreen
                ? WindowBorder.Hidden
                : WindowOptions.Default.WindowBorder,
            Position = settings.Mode == WindowMode.BorderlessFullscreen
                ? display.Origin
                : WindowOptions.Default.Position,

            // Asked for only when the display mode is actually ours to set. A borderless window that
            // names a video mode is asking the backend to switch the monitor, which is the one thing
            // borderless exists not to do.
            VideoMode = settings.Mode == WindowMode.ExclusiveFullscreen
                ? new VideoMode(display.Size, display.RefreshRate)
                : WindowOptions.Default.VideoMode,
            VSync = settings.VSync,
            API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.ForwardCompatible,
                new APIVersion(3, 3)),
            IsVisible = false,
            ShouldSwapAutomatically = false,
        };

        Display = display;

        try
        {
            _window = Window.Create(options);
            _window.Initialize();
        }
        catch (Exception ex)
        {
            throw new PlatformException(
                $"Could not create a {display.Size.X}x{display.Size.Y} OpenGL 3.3 window. "
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

    /// <summary>Gets what the monitor this window opened on reports, and what was chosen from it.</summary>
    public DesktopDisplay Display { get; }

    /// <summary>Gets the backend's own description of the window, for confirming what it really is.</summary>
    /// <remarks>
    /// Borderless and exclusive look alike from the outside — both cover the screen — but only one of
    /// them takes the display away from the desktop. This reports which actually happened rather than
    /// which was asked for.
    /// </remarks>
    public string BackendState =>
        $"state {_window.WindowState}, border {_window.WindowBorder}, "
            + $"position ({_window.Position.X},{_window.Position.Y}), size {_window.Size.X}x{_window.Size.Y}";

    /// <inheritdoc/>
    public WindowMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            ApplyMode();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Borderless presents through the compositor, so its rate is the desktop's whatever the monitor is
    /// capable of; only an exclusive window gets the mode that was asked for.
    /// </remarks>
    public int RefreshRate => _mode == WindowMode.BorderlessFullscreen
        ? Display.DesktopRefreshRate
        : Display.RefreshRate;

    /// <inheritdoc/>
    public bool CursorVisible
    {
        get => _cursorVisible;
        set
        {
            if (_cursorVisible == value)
            {
                return;
            }

            _cursorVisible = value;
            ApplyCursorMode();
        }
    }

    /// <inheritdoc/>
    public bool CursorLocked
    {
        get => _cursorLocked;
        set
        {
            if (_cursorLocked == value)
            {
                return;
            }

            _cursorLocked = value;
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
    /// <remarks>
    /// Ignored while borderless: the window's size is the desktop's, and shrinking it would leave an
    /// undecorated window floating over the wallpaper with no way to move it.
    /// </remarks>
    public void Resize(int width, int height)
    {
        if (width > 0 && height > 0 && _mode != WindowMode.BorderlessFullscreen)
        {
            _window.Size = new Vector2D<int>(width, height);
        }
    }

    /// <summary>Moves the window between windowed, borderless and exclusive without recreating it.</summary>
    private void ApplyMode()
    {
        switch (_mode)
        {
            case WindowMode.BorderlessFullscreen:
                // Order matters: leaving the fullscreen state first gives the platform back a normal
                // window to undecorate, and the position has to be set after the border is dropped
                // because dropping it moves the client area by the old border's thickness.
                _window.WindowState = WindowState.Normal;
                _window.WindowBorder = WindowBorder.Hidden;
                _window.Position = Display.Origin;
                _window.Size = Display.DesktopSize;
                break;

            case WindowMode.ExclusiveFullscreen:
                _window.WindowBorder = WindowBorder.Resizable;
                _window.WindowState = WindowState.Fullscreen;
                break;

            default:
                _window.WindowState = WindowState.Normal;
                _window.WindowBorder = WindowBorder.Resizable;
                _window.Size = _windowedSize;
                break;
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
            // Raw is the same lock as Disabled but without the desktop's pointer acceleration applied,
            // which is what a look control wants; not every backend offers it, so Disabled is the
            // fallback and both hide the cursor on their own.
            mouse.Cursor.CursorMode = _cursorLocked
                ? (mouse.Cursor.IsSupported(CursorMode.Raw) ? CursorMode.Raw : CursorMode.Disabled)
                : _cursorVisible ? CursorMode.Normal : CursorMode.Hidden;
        }
    }
}
