using OpenIGI.Input;

namespace OpenIGI.Platform.Web;

/// <summary>
/// The game's window in a browser: a canvas, and the input the page hands it each frame.
/// </summary>
/// <remarks>
/// <para>
/// The page owns everything a desktop window would: its size is the canvas's, laid out by the page; its
/// input is delivered to the page's handlers between frames and queued there; the pointer lock and
/// fullscreen are the browser's to grant. So this window reads a queue rather than dispatching events,
/// and asks rather than sets.
/// </para>
/// <para>
/// <b>A lost pointer lock is an Escape.</b> Escape is how a browser takes the lock back, and it keeps
/// the key to itself when it does. A game that asked for the lock has lost it in the middle of play, and
/// the one thing a player pressing Escape then expects is the pause menu -- so a lock lost while the game
/// still wants it is handed on as that key going down. The page then says when it comes up: at once for a tap, and
/// for as long as the key is held otherwise, which is what lets a cutscene be skipped by holding it.
/// </para>
/// <para>
/// <b>Every press lasts a frame.</b> The page queues events for as long as a frame takes, and a quick click
/// or tap can go down and up inside one: applied in order it would never be seen down at all. A release
/// that arrives in the frame its press did is held until the next.
/// </para>
/// </remarks>
public sealed class WebGameWindow : IGameWindow
{
    private readonly IBrowserPage _page;
    private readonly List<KeyCode> _releaseNextFrame = [];
    private readonly List<MouseButton> _releaseButtonNextFrame = [];
    private readonly HashSet<KeyCode> _pressedThisFrame = [];
    private readonly HashSet<MouseButton> _buttonsPressedThisFrame = [];
    private string _title;
    private string _clipboard = string.Empty;
    private (int Width, int Height) _size;
    private WindowMode _mode;
    private bool _cursorVisible;
    private bool _cursorLocked;
    private bool _gyroWanted;
    private bool _textWanted;
    private string? _textLine;
    private (float X, float Y, float Width, float Height)? _textTrigger;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="WebGameWindow"/> class.</summary>
    /// <param name="page">The page it runs in.</param>
    /// <param name="settings">
    /// What the game asked for. The title and the cursor are applied; the size is not, because the page
    /// lays the canvas out, and a fullscreen mode is only asked for when a click allows it.
    /// </param>
    public WebGameWindow(IBrowserPage page, in WindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(page);

        _page = page;
        _title = settings.Title;
        _cursorVisible = settings.CursorVisible;
        _size = page.CanvasSize;
        PixelDensity = Density(page);
        IsFocused = page.HasFocus;

        _page.SetTitle(_title);
        _page.SetPointer(_cursorVisible, _cursorLocked);
    }

    /// <inheritdoc/>
    public event EventHandler<WindowResizedEventArgs>? Resized;

    /// <inheritdoc/>
    public event EventHandler? CloseRequested;

    /// <inheritdoc/>
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            _page.SetTitle(_title);
        }
    }

    /// <inheritdoc/>
    public int Width => Math.Max(1, _size.Width);

    /// <inheritdoc/>
    public int Height => Math.Max(1, _size.Height);

    /// <inheritdoc/>
    /// <remarks>Fullscreen is asked of the page, which grants it only during a click.</remarks>
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
            _page.RequestFullscreen(value != WindowMode.Windowed);
        }
    }

    /// <inheritdoc/>
    /// <remarks>The page's device pixel ratio, read again whenever the canvas changes size: a zoom or a move to another screen does both.</remarks>
    public float PixelDensity { get; private set; }

    /// <inheritdoc/>
    /// <remarks>Read from the page each time: turning the phone moves the notch from one side to the other.</remarks>
    public (float Left, float Top, float Right, float Bottom) SafeInsets =>
        (Inset(0), Inset(1), Inset(2), Inset(3));

    private float Inset(int side)
    {
        double inset = _page.SafeInset(side);
        return inset > 0d && double.IsFinite(inset) ? (float)inset : 0f;
    }

    private static float Density(IBrowserPage page)
    {
        double ratio = page.PixelRatio;
        return ratio > 0d && double.IsFinite(ratio) ? (float)ratio : 1f;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Nothing tells a page its display's rate. The browser paces frames to it regardless, so this is the
    /// common rate, reported and not relied on.
    /// </remarks>
    public int RefreshRate => 60;

    /// <inheritdoc/>
    public bool CursorVisible
    {
        get => _cursorVisible;
        set
        {
            if (_cursorVisible != value)
            {
                _cursorVisible = value;
                _page.SetPointer(_cursorVisible, _cursorLocked);
            }
        }
    }

    /// <inheritdoc/>
    public bool CursorLocked
    {
        get => _cursorLocked;
        set
        {
            if (_cursorLocked != value)
            {
                _cursorLocked = value;
                _page.SetPointer(_cursorVisible, _cursorLocked);
            }
        }
    }

    /// <summary>Gets whether the browser has granted the pointer lock the game asked for.</summary>
    public bool PointerLockGranted { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// A page may write to the clipboard but not read it without asking the person, so what this reads is
    /// what the game last wrote.
    /// </remarks>
    public string ClipboardText
    {
        get => _clipboard;
        set
        {
            _clipboard = value ?? string.Empty;
            _page.WriteClipboard(_clipboard);
        }
    }

    /// <inheritdoc/>
    public bool IsFocused { get; private set; }

    /// <inheritdoc/>
    public bool IsClosing { get; private set; }

    /// <inheritdoc/>
    public InputState Input { get; } = new();

    /// <inheritdoc/>
    /// <remarks>The one mode there is: the canvas at the size the page shows it.</remarks>
    public IReadOnlyList<(int Width, int Height, int RefreshRate)> GetDisplayModes() => [(Width, Height, RefreshRate)];

    /// <inheritdoc/>
    /// <remarks>Ignored: the page lays the canvas out, and a resize arrives from there.</remarks>
    public void Resize(int width, int height)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Asked of the page only when it changes: the host says it every frame.</remarks>
    public void SetMotionSensors(bool wanted)
    {
        if (_gyroWanted != wanted && !_disposed)
        {
            _gyroWanted = wanted;
            _page.SetGyro(wanted);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Asked of the page only when it changes: the host says it every frame.</remarks>
    public void SetTextEntry(bool wanted)
    {
        if (_textWanted != wanted && !_disposed)
        {
            _textWanted = wanted;
            _page.SetTextEntry(wanted);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Asked of the page only when it changes: the host says it every frame.</remarks>
    public void SetTextLine(string? line)
    {
        if (_textLine != line && !_disposed)
        {
            _textLine = line;
            _page.SetTextLine(line);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Asked of the page only when it changes: the host says it every frame.</remarks>
    public void SetTextTrigger((float X, float Y, float Width, float Height)? area)
    {
        if (_textTrigger != area && !_disposed)
        {
            _textTrigger = area;
            (float x, float y, float width, float height) = area ?? default;
            _page.SetTextTrigger(x, y, width, height);
        }
    }

    /// <inheritdoc/>
    public float TextEntryInset { get; private set; }

    /// <inheritdoc/>
    /// <remarks>The canvas is on the page already.</remarks>
    public void Show() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc/>
    public void PumpEvents()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Shadow first, then apply: the events this call delivers belong to the frame about to run.
        Input.BeginFrame();

        foreach (KeyCode key in _releaseNextFrame)
        {
            Input.Keyboard.SetKey(key, down: false);
        }

        foreach (MouseButton button in _releaseButtonNextFrame)
        {
            Input.Mouse.SetButton(button, down: false);
        }

        _releaseNextFrame.Clear();
        _releaseButtonNextFrame.Clear();
        _pressedThisFrame.Clear();
        _buttonsPressedThisFrame.Clear();

        foreach (string line in _page.TakeEvents())
        {
            Apply(WebInputEvent.Parse(line));
        }
    }

    /// <inheritdoc/>
    /// <remarks>A page cannot close its own tab; the game is told it is closing, and stops.</remarks>
    public void Close()
    {
        if (!_disposed && !IsClosing)
        {
            IsClosing = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
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
        _page.SetPointer(visible: true, locked: false);
        if (_gyroWanted)
        {
            _page.SetGyro(false);
        }
    }

    private void Apply(in WebInputEvent input)
    {
        switch (input.Kind)
        {
            case WebInputEventKind.Key:
                KeyCode key = WebKeyMap.Translate(input.Code);
                if (key == KeyCode.None)
                {
                    break;
                }

                if (input.Down)
                {
                    _pressedThisFrame.Add(key);
                    Input.Keyboard.SetKey(key, down: true);
                }
                else if (_pressedThisFrame.Contains(key))
                {
                    _releaseNextFrame.Add(key);
                }
                else
                {
                    Input.Keyboard.SetKey(key, down: false);
                }

                break;

            case WebInputEventKind.Line:
                Input.Keyboard.ReplaceLine(input.Code);
                break;

            case WebInputEventKind.Inset:
                TextEntryInset = Math.Max(0f, input.X);
                break;

            case WebInputEventKind.Character:
                foreach (char character in input.Code)
                {
                    Input.Keyboard.EnqueueCharacter(character);
                }

                break;

            case WebInputEventKind.Move:
                Input.Mouse.AddMotion(input.X, input.Y);
                Input.Mouse.SetPosition(input.Z, input.W);
                break;

            case WebInputEventKind.Button:
                if (Button((int)input.X) is not { } button)
                {
                    break;
                }

                if (input.Down)
                {
                    _buttonsPressedThisFrame.Add(button);
                    Input.Mouse.SetButton(button, down: true);
                }
                else if (_buttonsPressedThisFrame.Contains(button))
                {
                    _releaseButtonNextFrame.Add(button);
                }
                else
                {
                    Input.Mouse.SetButton(button, down: false);
                }

                break;

            case WebInputEventKind.Wheel:
                Input.Mouse.AddWheel(input.X);
                break;

            case WebInputEventKind.Resize:
                (int Width, int Height) size = ((int)input.X, (int)input.Y);
                if (size != _size && size.Width > 0 && size.Height > 0)
                {
                    PixelDensity = Density(_page);
                    _size = size;
                    Resized?.Invoke(this, new WindowResizedEventArgs(size.Width, size.Height));
                }

                break;

            case WebInputEventKind.Focus:
                IsFocused = input.Down;
                if (!input.Down)
                {
                    // As the desktop: keys still held when the page lost the keyboard never report
                    // their release, and would stay latched down until pressed again.
                    Input.Keyboard.Release();
                    Input.Mouse.Reset();
                    Input.Touch.Release();
                }

                break;

            case WebInputEventKind.PointerLock:
                bool lost = PointerLockGranted && !input.Down;
                PointerLockGranted = input.Down;
                if (lost && _cursorLocked)
                {
                    // Down from here; the page says when it comes up (platform.js, escapeTaken), as it does for any key --
                    // in this same frame for a tap, which the frame's press then outlasts like any other.
                    _pressedThisFrame.Add(KeyCode.Escape);
                    Input.Keyboard.SetKey(KeyCode.Escape, down: true);
                }

                break;

            case WebInputEventKind.Close:
                Close();
                break;

            case WebInputEventKind.Touch:
                int finger = (int)input.Z;
                switch (input.Code)
                {
                    case "down":
                        Input.Touch.Down(finger, input.X, input.Y);
                        break;
                    case "move":
                        Input.Touch.Move(finger, input.X, input.Y);
                        break;
                    default:
                        Input.Touch.Up(finger, input.X, input.Y);
                        break;
                }

                break;

            case WebInputEventKind.Gyro:
                Input.Motion.Add(input.X, input.Y);
                break;
        }
    }

    /// <summary>A DOM mouse button number as the engine's button.</summary>
    private static MouseButton? Button(int number) => number switch
    {
        0 => MouseButton.Left,
        1 => MouseButton.Middle,
        2 => MouseButton.Right,
        3 => MouseButton.Extra1,
        4 => MouseButton.Extra2,
        _ => null,
    };
}
