using System.Diagnostics;
using OpenIGI.Input;
using OpenIGI.Launcher.Services;
using OpenIGI.Platform.Desktop;
using OpenIGI.UI;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;
using OpenIGI.UI.Screens;
using OpenIGI.UI.Skia;
using OpenIGI.UI.Widgets;
using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using SkiaSharp;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace OpenIGI.Launcher;

/// <summary>
/// The launcher's window and frame loop: a borderless OpenGL window, a Skia surface on its back buffer,
/// and the shared interface drawn into it once a frame.
/// </summary>
/// <remarks>
/// <para>
/// Lightweight on purpose. One GLFW window and one GL context on every desktop, Skia's GPU backend
/// drawing into it, and the same <see cref="ScreenStack"/>, widgets and focus model the game's menus use —
/// no UI framework of its own. On macOS the context is a forward-compatible core profile, the only kind
/// Apple offers.
/// </para>
/// <para>
/// The window has no system frame: the design draws its own chrome, and a drag on its empty part moves the
/// window, which is what a title bar does. A drag on any edge or corner resizes it, which is what the
/// frame's border would do.
/// </para>
/// <para>
/// The interface is always drawn at 100% of the display's scale, never shrunk or grown with the window: a
/// resize changes how much room the layout has, not how large the text is. The window opens at the
/// design's own size, made smaller only if the screen needs it.
/// </para>
/// </remarks>
internal sealed class LauncherApp : ILauncherWindow, IDisposable
{
    /// <summary>The size the design is drawn for, in design units.</summary>
    private static readonly UiVec2 DesignSize = new(1600f, 900f);

    /// <summary>The smallest the window may be made, in design units; below it the layout stops fitting.</summary>
    private static readonly UiVec2 MinimumDesignSize = new(1280f, 720f);

    /// <summary>How far in from an edge, in window units, a press grabs the edge to resize.</summary>
    private const float EdgeGrip = 6f;

    /// <summary>How far along an edge from a corner a press grabs the corner instead.</summary>
    private const float CornerGrip = 16f;

    private readonly IWindow _window;
    private readonly InputState _input = new();
    private readonly ScreenStack _stack = new() { PointerSuppressed = true };
    private readonly LauncherLog _log = LauncherLog.Start();
    private readonly LauncherModel _model;
    private readonly SkiaImageStore _images = new();
    private readonly Dictionary<string, BackdropPicture?> _pictures = new(StringComparer.Ordinal);
    private readonly SkiaFontCache _fonts = new();
    private readonly DesktopNotifier _notifier;

    private IInputContext? _inputContext;
    private GL? _gl;
    private GRContext? _gr;
    private GRBackendRenderTarget? _target;
    private SKSurface? _surface;
    private IgiTypography? _typography;
    private LauncherScreen? _screen;
    private Vector2D<int> _surfaceSize;
    private bool _dragging;
    private Vector2D<float> _dragOrigin;
    private IMouse? _mouse;

    /// <summary>Until when the window draws every frame; after it, the loop sleeps until something happens.</summary>
    private long _activeUntil;

    /// <summary>The model's revision as last drawn, so a change made in the background wakes the window.</summary>
    private int _drawnRevision = -1;
    private Edges _resizing;
    private Vector2D<float> _resizeOrigin;
    private Vector2D<int> _minimumSize = new((int)MinimumDesignSize.X, (int)MinimumDesignSize.Y);

    /// <summary>The display's scale, 1 at 100%; the interface is always drawn at exactly this scale.</summary>
    private float _contentScale = 1f;

    [Flags]
    private enum Edges
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
    }

    public LauncherApp()
    {
        _model = new LauncherModel(_log);
        GlfwWindowing.RegisterPlatform();
        GlfwInput.RegisterPlatform();

        WindowOptions options = WindowOptions.Default with
        {
            Title = "OpenIGI Launcher",
            Size = new Vector2D<int>((int)DesignSize.X, (int)DesignSize.Y),
            WindowBorder = WindowBorder.Hidden,

            // Hidden until it has been sized to the screen, so it never shows at the wrong size first.
            IsVisible = false,
            VSync = true,
            PreferredStencilBufferBits = 8,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += _ =>
        {
            ReleaseSurface();
            MarkActive();
        };
        _window.StateChanged += _ => MarkActive();
        _window.Closing += ReleaseGraphics;

        // Coming back to the window is when a player expects to see a release that came out meanwhile; while
        // it is away, a release is announced by a desktop notification instead.
        _window.FocusChanged += focused =>
        {
            MarkActive();
            _model.WindowFocused = focused;
            if (focused)
            {
                _model.CheckSoon();
            }
        };

        _notifier = new DesktopNotifier(() => _window.Native?.Win32?.Hwnd ?? 0);
        _model.Notify = _notifier.Show;
    }

    /// <summary>How long after the last thing that changed the window keeps drawing every frame.</summary>
    /// <remarks>Long enough for a hover fade, a page transition or a focus ring to finish easing.</remarks>
    private static readonly long ActiveTicks = Stopwatch.Frequency * 2;

    /// <summary>The most frames a second the launcher draws, whatever the display's refresh rate.</summary>
    /// <remarks>Smooth fades and progress at 60, and no more on a faster display (144 Hz would be 2.4 times the work).</remarks>
    private const int FramesPerSecond = 60;

    private static readonly long FrameTicks = Stopwatch.Frequency / FramesPerSecond;

    /// <summary>The longest step, in seconds, the interface's animations advance by in one update.</summary>
    private const double MaximumStep = 0.05;

    /// <summary>
    /// Runs the window until it closes, drawing only while something on it can be changing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A launcher spends nearly all its life open and untouched, and redrawing an unchanging picture sixty times
    /// a second is what made it the busiest program in Task Manager while doing nothing. So the loop has two
    /// speeds. While active it updates and draws at <see cref="FramesPerSecond"/>, waiting for events between
    /// frames (the swap interval stays on, so a frame never tears).
    /// It is active for a couple of seconds after anything that can change the picture: input, focus, a resize, a
    /// minimise or restore, the model's state changing (a release seen, an install finishing), and for as long as
    /// a download is running.
    /// </para>
    /// <para>
    /// The rest of the time it blocks in the system's event wait, which the OS ends the moment there is input,
    /// and wakes twice a second (once a second while minimised) only to tick the model: update checks, finished
    /// background work. Nothing is drawn unless that tick changed something, so an idle launcher costs nothing.
    /// </para>
    /// </remarks>
    public unsafe void Run()
    {
        _window.Initialize();
        Glfw glfw = GlfwProvider.GLFW.Value;
        MarkActive();

        long nextFrame = Stopwatch.GetTimestamp();
        while (!_window.IsClosing)
        {
            if (IsActive)
            {
                // At most FramesPerSecond: the time to the next frame is spent waiting for events, which the
                // OS ends early for input (handled at once, drawn at the frame).
                long now = Stopwatch.GetTimestamp();
                if (now < nextFrame)
                {
                    glfw.WaitEventsTimeout((double)(nextFrame - now) / Stopwatch.Frequency);
                    _window.DoEvents();
                    continue;
                }

                nextFrame = Math.Max(nextFrame + FrameTicks, now);
                _window.DoEvents();
            }
            else
            {
                glfw.WaitEventsTimeout(_window.WindowState == WindowState.Minimized ? 1.0 : 0.5);
                _window.DoEvents();
                nextFrame = Stopwatch.GetTimestamp();
            }

            if (_window.IsClosing)
            {
                break;
            }

            _window.DoUpdate();
            if (_model.Revision != _drawnRevision || _model.Job is not null)
            {
                _drawnRevision = _model.Revision;
                MarkActive();
            }

            if (IsActive && _window.WindowState != WindowState.Minimized)
            {
                _window.DoRender();
            }
        }

        _window.DoEvents();
        _window.Reset();
    }

    private bool IsActive => Stopwatch.GetTimestamp() < _activeUntil;

    private void MarkActive() => _activeUntil = Stopwatch.GetTimestamp() + ActiveTicks;

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    public void ToggleMaximize() =>
        _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public void Close() => _window.Close();

    public string ClipboardText
    {
        get => _inputContext?.Keyboards.Count > 0 ? _inputContext.Keyboards[0].ClipboardText ?? string.Empty : string.Empty;
        set
        {
            if (_inputContext?.Keyboards.Count > 0)
            {
                _inputContext.Keyboards[0].ClipboardText = value;
            }
        }
    }

    public BackdropPicture? Picture(string path)
    {
        // A pack's picture is named by its content, so a path decoded once is that picture for good.
        if (!_pictures.TryGetValue(path, out BackdropPicture? picture))
        {
            SkiaSharp.SKImage? image = File.Exists(path) ? SkiaSharp.SKImage.FromEncodedData(path) : null;
            picture = image is null ? null : new BackdropPicture(_images.Add(image), image.Width / (float)image.Height);
            _pictures[path] = picture;
        }

        return picture;
    }

    public void Dispose()
    {
        ReleaseGraphics();
        _notifier.Dispose();
        _model.Dispose();
        _typography?.Dispose();
        _fonts.Dispose();
        _images.Dispose();
        _inputContext?.Dispose();
        _window.Dispose();
        _log.Dispose();
    }

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        // Skia probes optional entry points (EGL, extensions) and expects null for a missing one; Silk's
        // GetProcAddress throws instead, so the lookup goes through the non-throwing form.
        IGLContext context = _window.GLContext!;
        GRGlInterface glInterface = GRGlInterface.Create(name => context.TryGetProcAddress(name, out nint address) ? address : 0)
            ?? throw new InvalidOperationException("Skia could not bind to the window's OpenGL context.");
        _gr = GRContext.CreateGl(glInterface)
            ?? throw new InvalidOperationException("Skia could not create a GPU context on the window.");

        _typography = LauncherFonts.Load();
        UiTheme theme = _typography.CreateTheme();

        // The launcher's settings rows are the design's taller, ruled kind, with a larger caption.
        UiTheme rows = _typography.CreateTheme();
        rows.RowHeight = 54f;
        rows.RowDividers = true;
        rows.BodyTextSize = 18f;

        BackdropPicture? picture = LauncherBackdrop.LoadPicture(_images);
        _screen = new LauncherScreen(theme, rows, _model, this, picture);
        _stack.Push(_screen, animate: true);

        _inputContext = _window.CreateInput();
        foreach (IKeyboard keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) =>
            {
                MarkActive();
                SetKey(key, down: true);
            };
            keyboard.KeyUp += (_, key, _) =>
            {
                MarkActive();
                SetKey(key, down: false);
            };
            keyboard.KeyChar += (_, character) =>
            {
                MarkActive();
                _input.Keyboard.EnqueueCharacter(character);
            };
        }

        foreach (IMouse mouse in _inputContext.Mice)
        {
            mouse.MouseDown += (_, button) =>
            {
                MarkActive();
                OnMouseButton(button, down: true);
            };
            mouse.MouseUp += (_, button) =>
            {
                MarkActive();
                OnMouseButton(button, down: false);
            };
            mouse.MouseMove += (_, position) =>
            {
                MarkActive();
                OnMouseMove(position);
            };
            mouse.Scroll += (_, wheel) =>
            {
                MarkActive();
                _input.Mouse.AddWheel(wheel.Y);
            };
            mouse.DoubleClick += (_, button, _) =>
            {
                MarkActive();
                OnDoubleClick(button);
            };
            _mouse ??= mouse;
        }

        FitToScreen();
        _window.Center();
        _window.IsVisible = true;
    }

    /// <summary>
    /// Sizes the window to the design at the display's scale, shrunk to fit the screen's work area, and sets
    /// the smallest size a resize may reach.
    /// </summary>
    private unsafe void FitToScreen()
    {
        Glfw glfw = GlfwProvider.GLFW.Value;
        Silk.NET.GLFW.Monitor* monitor = glfw.GetPrimaryMonitor();
        if (monitor is null || _window.Native?.Glfw is not { } handle)
        {
            return;
        }

        glfw.GetMonitorWorkarea(monitor, out _, out _, out int areaWidth, out int areaHeight);
        glfw.GetMonitorContentScale(monitor, out float dpi, out _);
        dpi = dpi > 0f ? dpi : 1f;
        _contentScale = dpi;

        // A margin round the window, so it never opens covering the whole screen.
        float fit = MathF.Min(1f, MathF.Min(areaWidth * 0.92f / (DesignSize.X * dpi), areaHeight * 0.92f / (DesignSize.Y * dpi)));
        var size = new Vector2D<int>((int)(DesignSize.X * dpi * fit), (int)(DesignSize.Y * dpi * fit));
        _minimumSize = new Vector2D<int>(
            Math.Min(size.X, (int)(MinimumDesignSize.X * dpi)),
            Math.Min(size.Y, (int)(MinimumDesignSize.Y * dpi)));

        glfw.SetWindowSizeLimits((WindowHandle*)handle, _minimumSize.X, _minimumSize.Y, Glfw.DontCare, Glfw.DontCare);
        _window.Size = size;
    }

    private void OnUpdate(double elapsed)
    {
        if (_screen is null)
        {
            return;
        }

        // Coming out of an idle wait, the time since the last update is however long the window slept (up to a
        // second). Handed to the interface whole, every transition a click starts would jump to its end in
        // one frame; capped at a few frames, it starts from where it should and eases as it does when busy.
        LayoutContext context = Layout();
        _stack.Update(context, _input, (float)Math.Min(elapsed, MaximumStep));
        _input.BeginFrame();
    }

    private void OnRender(double elapsed)
    {
        if (_gr is null || _gl is null)
        {
            return;
        }

        Vector2D<int> size = _window.FramebufferSize;
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }

        EnsureSurface(size);
        SKCanvas canvas = _surface!.Canvas;
        canvas.Clear(new SKColor(10, 12, 13));

        using (var renderer = new SkiaUiRenderer(canvas, _images, _fonts))
        {
            var painter = new UiPainter(renderer, Layout());
            _stack.Draw(painter);
            renderer.Flush();
        }

        canvas.Flush();
        _gr.Flush();
    }

    private LayoutContext Layout()
    {
        // A desktop window, not a game screen: the interface stays at the display's scale whatever the
        // window's size, and a larger window gives the layout more room rather than larger type. The context
        // fits the design to the window, so the user scale undoes that fit and applies the display's instead.
        Vector2D<int> size = _window.FramebufferSize;
        float width = MathF.Max(1, size.X);
        float height = MathF.Max(1, size.Y);
        var fitted = new LayoutContext(width, height, DesignSize, match: 0.5f);
        return new LayoutContext(width, height, DesignSize, match: 0.5f, userScale: _contentScale / fitted.Scale);
    }

    private void EnsureSurface(Vector2D<int> size)
    {
        if (_surface is not null && size == _surfaceSize)
        {
            return;
        }

        ReleaseSurface();
        _gl!.Viewport(0, 0, (uint)size.X, (uint)size.Y);

        // The window's own back buffer, framebuffer zero, as Skia's render target.
        const uint Rgba8 = 0x8058;
        _target = new GRBackendRenderTarget(size.X, size.Y, 0, 8, new GRGlFramebufferInfo(0, Rgba8));
        _surface = SKSurface.Create(_gr, _target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888)
            ?? throw new InvalidOperationException("Skia could not wrap the window's back buffer.");
        _surfaceSize = size;
    }

    private void ReleaseSurface()
    {
        _surface?.Dispose();
        _surface = null;
        _target?.Dispose();
        _target = null;
    }

    private void ReleaseGraphics()
    {
        ReleaseSurface();
        _gr?.Dispose();
        _gr = null;
    }

    private void SetKey(Key key, bool down)
    {
        KeyCode code = DesktopKeyMap.Translate(key);
        if (code != KeyCode.None)
        {
            _input.Keyboard.SetKey(code, down);
        }
    }

    private void OnMouseButton(SilkMouseButton button, bool down)
    {
        Input.MouseButton? mapped = button switch
        {
            SilkMouseButton.Left => Input.MouseButton.Left,
            SilkMouseButton.Right => Input.MouseButton.Right,
            SilkMouseButton.Middle => Input.MouseButton.Middle,
            _ => null,
        };

        if (button == SilkMouseButton.Left)
        {
            // An edge takes the press before the interface does: the border belongs to the window.
            Edges edges = down ? EdgeAt(_lastMouse) : Edges.None;
            if (edges != Edges.None)
            {
                _resizing = edges;
                _resizeOrigin = WindowPoint(_lastMouse);
                return;
            }

            if (!down && _resizing != Edges.None)
            {
                _resizing = Edges.None;
                return;
            }
        }

        if (mapped is { } m)
        {
            _input.Mouse.SetButton(m, down);
        }

        if (button == SilkMouseButton.Left)
        {
            if (down && _screen is not null && _screen.IsDragRegion(new UiVec2(_input.Mouse.X, _input.Mouse.Y)))
            {
                _dragging = true;
                _dragOrigin = WindowPoint(_lastMouse);
            }
            else if (!down)
            {
                _dragging = false;
            }
        }
    }

    /// <summary>A double click on the title bar maximises the window, or restores it, as a system title bar does.</summary>
    private void OnDoubleClick(SilkMouseButton button)
    {
        if (button == SilkMouseButton.Left
            && _resizing == Edges.None
            && _screen is not null
            && _screen.IsDragRegion(new UiVec2(_input.Mouse.X, _input.Mouse.Y)))
        {
            _dragging = false;
            ToggleMaximize();
        }
    }

    private System.Numerics.Vector2 _lastMouse;

    private void OnMouseMove(System.Numerics.Vector2 position)
    {
        _lastMouse = position;
        if (_resizing != Edges.None)
        {
            Resize(position);
            return;
        }

        ShowEdgeCursor(EdgeAt(position));
        Vector2D<float> scale = FramebufferScale();
        float x = position.X * scale.X;
        float y = position.Y * scale.Y;
        _input.Mouse.AddMotion(x - _input.Mouse.X, y - _input.Mouse.Y);
        _input.Mouse.SetPosition(x, y);

        if (_dragging && _window.WindowState == WindowState.Normal)
        {
            // Window-relative coordinates: moving the window by the pointer's offset from where the drag
            // began brings the pointer back to that same spot, so the window follows it.
            Vector2D<float> now = WindowPoint(position);
            Vector2D<float> delta = now - _dragOrigin;
            _window.Position += new Vector2D<int>((int)MathF.Round(delta.X), (int)MathF.Round(delta.Y));
        }
    }

    /// <summary>Finds which edges of the window a point is near enough to grab.</summary>
    /// <param name="position">The point, in window units.</param>
    /// <returns>The edges; none when the window is not in its normal state or the point is inside.</returns>
    private Edges EdgeAt(System.Numerics.Vector2 position)
    {
        if (_window.WindowState != WindowState.Normal)
        {
            return Edges.None;
        }

        Vector2D<int> size = _window.Size;
        bool left = position.X < EdgeGrip;
        bool right = position.X >= size.X - EdgeGrip;
        bool top = position.Y < EdgeGrip;
        bool bottom = position.Y >= size.Y - EdgeGrip;

        // Close to a corner along an edge, the corner is the likelier target.
        if (left || right)
        {
            top |= position.Y < CornerGrip;
            bottom |= position.Y >= size.Y - CornerGrip;
        }

        if (top || bottom)
        {
            left |= position.X < CornerGrip;
            right |= position.X >= size.X - CornerGrip;
        }

        return (left ? Edges.Left : right ? Edges.Right : Edges.None)
            | (top ? Edges.Top : bottom ? Edges.Bottom : Edges.None);
    }

    private void ShowEdgeCursor(Edges edges)
    {
        if (_mouse is null)
        {
            return;
        }

        StandardCursor shape = edges switch
        {
            Edges.Left or Edges.Right => StandardCursor.HResize,
            Edges.Top or Edges.Bottom => StandardCursor.VResize,
            Edges.Left | Edges.Top or Edges.Right | Edges.Bottom => StandardCursor.NwseResize,
            Edges.Right | Edges.Top or Edges.Left | Edges.Bottom => StandardCursor.NeswResize,
            _ => StandardCursor.Default,
        };

        if (_mouse.Cursor.StandardCursor != shape)
        {
            _mouse.Cursor.StandardCursor = shape;
        }
    }

    /// <summary>Follows a resize drag: the grabbed edges track the pointer and the others stay put.</summary>
    /// <param name="position">The pointer, in window units.</param>
    /// <remarks>
    /// A right or bottom edge follows the pointer directly, and the grab point moves with it. A left or top
    /// edge moves the window too; moving it by the pointer's offset from the grab brings the pointer back to
    /// the same window-relative spot, the same trick the title-bar drag uses.
    /// </remarks>
    private void Resize(System.Numerics.Vector2 position)
    {
        Vector2D<int> origin = _window.Position;
        Vector2D<int> size = _window.Size;
        Vector2D<int> newOrigin = origin;
        Vector2D<int> newSize = size;

        if ((_resizing & Edges.Right) != 0)
        {
            newSize.X = Math.Max(_minimumSize.X, size.X + (int)MathF.Round(position.X - _resizeOrigin.X));
            _resizeOrigin.X += newSize.X - size.X;
        }
        else if ((_resizing & Edges.Left) != 0)
        {
            newSize.X = Math.Max(_minimumSize.X, size.X - (int)MathF.Round(position.X - _resizeOrigin.X));
            newOrigin.X += size.X - newSize.X;
        }

        if ((_resizing & Edges.Bottom) != 0)
        {
            newSize.Y = Math.Max(_minimumSize.Y, size.Y + (int)MathF.Round(position.Y - _resizeOrigin.Y));
            _resizeOrigin.Y += newSize.Y - size.Y;
        }
        else if ((_resizing & Edges.Top) != 0)
        {
            newSize.Y = Math.Max(_minimumSize.Y, size.Y - (int)MathF.Round(position.Y - _resizeOrigin.Y));
            newOrigin.Y += size.Y - newSize.Y;
        }

        if (newOrigin != origin)
        {
            _window.Position = newOrigin;
        }

        if (newSize != size)
        {
            _window.Size = newSize;
        }
    }

    private static Vector2D<float> WindowPoint(System.Numerics.Vector2 position) => new(position.X, position.Y);

    private Vector2D<float> FramebufferScale()
    {
        Vector2D<int> window = _window.Size;
        Vector2D<int> framebuffer = _window.FramebufferSize;
        return window.X <= 0 || window.Y <= 0
            ? new Vector2D<float>(1f, 1f)
            : new Vector2D<float>(framebuffer.X / (float)window.X, framebuffer.Y / (float)window.Y);
    }
}
