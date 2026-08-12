using System.Numerics;
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
    // Keep borderless presentation composited by DWM. An exact monitor-sized client area can be
    // classified by the desktop compositor/driver as an exclusive-style surface, which causes the
    // level image to flash when the presentation surface is re-used.
    private const int BorderlessOverhang = 1;

    private readonly IWindow _window;
    private readonly IInputContext _input;
    private readonly Vector2D<int> _windowedSize;
    private WindowMode _mode;
    private bool _cursorVisible;
    private bool _cursorLocked;
    private bool _disposed;

    // What the last pointer report said, and the baseline this frame's motion is measured from. It is
    // deliberately NOT MouseState.X/Y: that is the position game code reads, and it must survive a
    // discontinuity that the motion must not. Null means "no baseline" -- the next report establishes
    // one and contributes no motion, which is how a pointer that has been moved out from under us
    // arrives as a new starting point rather than as a movement the player never made.
    private Vector2? _pointerBaseline;

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
            Size = settings.Mode == WindowMode.BorderlessFullscreen
                ? BorderlessSize(display)
                : display.Size,
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

            // Created hidden so nothing sees a half-initialised window -- except in exclusive
            // fullscreen, which cannot be created hidden at all. The platform grants the fullscreen
            // state against a monitor and will not grant one to an invisible window, so a hidden
            // exclusive window comes up decorated and is never corrected. That matters beyond how it
            // looks: the caller reads Width and Height straight after construction to size the
            // graphics device, so a window still pretending to be 2560x1421 when the framebuffer is
            // really 2560x1440 leaves a strip along one edge that nothing ever renders into.
            IsVisible = settings.Mode == WindowMode.ExclusiveFullscreen,
            ShouldSwapAutomatically = false,
        };

        Display = display;

        // Desktop asks for the newest context it can get and falls back down the list. 4.6 brings
        // compute shaders, shader storage buffers and image load/store, which is what lets the
        // remaster track's fog, ambient occlusion and probe lighting run as compute rather than as
        // fragment passes over a fullscreen quad. 3.3 is the floor and is what the web and mobile
        // backends will target; the game runs on it, only with the cheaper implementations of those
        // effects. Nothing below 3.3 is supported at all.
        APIVersion[] wanted =
        [
            new APIVersion(4, 6),
            new APIVersion(4, 3),
            new APIVersion(3, 3),
        ];

        Exception? lastFailure = null;

        foreach (APIVersion version in wanted)
        {
            WindowOptions attempt = options with
            {
                API = new GraphicsAPI(
                    ContextAPI.OpenGL,
                    ContextProfile.Core,
                    ContextFlags.ForwardCompatible,
                    version),
            };

            try
            {
                _window = Window.Create(attempt);
                _window.Initialize();
                GLVersion = version;
                lastFailure = null;
                break;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                _window = null!;
            }
        }

        if (_window is null || lastFailure is not null)
        {
            string message =
                $"Could not create a {display.Size.X}x{display.Size.Y} OpenGL window. "
                + "A driver supporting OpenGL 3.3 core or newer is required.";

            throw lastFailure is null ? new PlatformException(message) : new PlatformException(message, lastFailure);
        }

        if (_window.GLContext is null)
        {
            _window.Dispose();
            throw new PlatformException("The window was created without an OpenGL context.");
        }

        _input = _window.CreateInput();
        _cursorVisible = settings.CursorVisible;

        // Asked for again now the window exists and is on screen. Creating it with the state set is not
        // enough on its own: the backend reports it back as Normal with a border and a title bar, and
        // the size that comes with that is the one the device would be built against.
        if (_mode == WindowMode.ExclusiveFullscreen)
        {
            ApplyMode();
            _window.DoEvents();
        }
        else if (_mode == WindowMode.Windowed)
        {
            ResizeClientArea(_windowedSize.X, _windowedSize.Y);
        }

        HookEvents();
        ApplyCursorMode();
    }

    /// <inheritdoc/>
    public event EventHandler<WindowResizedEventArgs>? Resized;

    /// <inheritdoc/>
    public event EventHandler? CloseRequested;

    /// <summary>Gets the OpenGL context this window owns, for building a graphics device against.</summary>
    public IGLContext GLContext => _window.GLContext!;

    /// <summary>Gets the context version that was actually created.</summary>
    /// <remarks>
    /// The highest of 4.6, 4.3 and 3.3 the driver would give. The backend branches on this rather than
    /// on a driver string: 4.3 is where compute shaders and image load/store arrive, and 3.3 is the
    /// floor the web and mobile backends share.
    /// </remarks>
    public APIVersion GLVersion { get; private set; } = new APIVersion(3, 3);

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
            + $"position ({_window.Position.X},{_window.Position.Y}), size {_window.Size.X}x{_window.Size.Y}, "
            + $"raw mouse motion {(SupportsRawMouseMotion ? "available" : "UNAVAILABLE")}";

    /// <summary>Gets whether a locked cursor reports the sensor's own counts rather than accelerated ones.</summary>
    /// <remarks>
    /// This is what makes the look a fixed number of radians per mouse count. Without it the lock falls
    /// back to an ordinary hidden-and-captured cursor, the desktop's pointer ballistics stay in the path,
    /// and slow movements are scaled toward zero before they ever reach the game — which is the classic
    /// "small movements do not register" of a mouse look. Reported rather than silently accepted,
    /// because the difference is invisible from inside the game and impossible to guess from outside it.
    /// </remarks>
    public bool SupportsRawMouseMotion =>
        _input.Mice.Count > 0 && _input.Mice[0].Cursor.IsSupported(CursorMode.Raw);

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

        // Exclusive fullscreen has to be asked for again now that the window is on screen.
        //
        // The window is created hidden, and the other two modes survive that because they are not
        // states: borderless is a size, a position and a dropped border, and windowed is a size --
        // all of which a hidden window holds perfectly well and carries into being shown. Fullscreen
        // is a state the platform grants against a monitor, and it will not grant one to a window
        // that is not visible. Asked for at creation it was quietly dropped, and because nothing
        // re-asserted it afterwards -- ApplyMode runs only from the Mode setter, which early-returns
        // when the mode has not changed -- -fullscreen came up as an ordinary window every time.
        if (_mode == WindowMode.ExclusiveFullscreen)
        {
            ApplyMode();
        }
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
            ResizeClientArea(width, height);
        }
    }

    /// <summary>Resizes a decorated window to an exact drawable client area.</summary>
    /// <remarks>
    /// On Win32 Silk's <c>IWindow.Size</c> is the outer rectangle while the engine-facing
    /// <see cref="Width"/> and <see cref="Height"/> are the framebuffer. Applying the requested size
    /// directly therefore lost the title-bar height (2560x1440 became 2560x1421), invalidating native
    /// resolution captures and benchmarks. One correction using the measured decoration delta is
    /// sufficient and remains a no-op on backends where <c>Size</c> already means client size.
    /// </remarks>
    private void ResizeClientArea(int width, int height)
    {
        _window.Size = new Vector2D<int>(width, height);
        _window.DoEvents();

        int widthDelta = width - _window.FramebufferSize.X;
        int heightDelta = height - _window.FramebufferSize.Y;
        if (widthDelta != 0 || heightDelta != 0)
        {
            _window.Size += new Vector2D<int>(widthDelta, heightDelta);
            _window.DoEvents();
        }
    }

    /// <summary>The client size a borderless window covering <paramref name="display"/> is given.</summary>
    private static Vector2D<int> BorderlessSize(in DesktopDisplay display) =>
        display.DesktopSize + new Vector2D<int>(BorderlessOverhang, BorderlessOverhang);

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
                _window.Size = BorderlessSize(Display);
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
            // Losing focus releases a locked cursor and regaining it re-centres one, both of which move
            // the pointer without the player touching it. Neither may be read as motion.
            _pointerBaseline = null;

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
                // Motion is the step from the last report, not from MouseState's position. The two
                // differ exactly when the backend moves the pointer itself: locking the cursor re-centres
                // GLFW's virtual pointer, so measuring against a position carried across that reset turns
                // the reset into a violent flick of the view. Dropping the baseline instead loses one
                // report's worth of motion, which is nothing, and is what ApplyCursorMode does below.
                if (_pointerBaseline is { } previous)
                {
                    Input.Mouse.AddMotion(position.X - previous.X, position.Y - previous.Y);
                }

                _pointerBaseline = position;
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
        Input.MouseButton? mapped = button switch
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
        // Every one of these transitions moves the pointer: locking it re-centres the backend's virtual
        // position, unlocking it restores the pointer to where the desktop left it. The next report is a
        // new starting point, not a movement.
        _pointerBaseline = null;

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
