using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Audio;
using OpenIGI.Core.Graphics;
using OpenIGI.Input;
using OpenIGI.Rendering;
using OpenIGI.Game.Menus;

namespace OpenIGI.Presentation.Igi1.Menus;

/// <summary>The retail menu system's shell: the screens drawn through a <see cref="MenuRenderer"/>, the pointer and keys read, the sounds played.</summary>
/// <remarks>
/// The menu manager, being a task under the flow, is updated and rendered by the frame walk; this only supplies input
/// and drawing, and says where leaving the menus goes.
/// </remarks>
public sealed class RetailMenuHost : IRetailMenuPresenter
{
    private const string LogCategory = "Menu";

    private readonly List<MenuInputEvent> _events = [];
    private readonly List<char> _typed = [];
    private readonly IOpenIgiLogger _logger;
    private readonly InputState _input;
    private readonly SpriteBatch _batch;
    private readonly Func<int> _screenWidth;
    private readonly Func<int> _screenHeight;
    private readonly Action<MenuManagerState> _leave;
    private readonly SoundSystem? _sounds;
    private MenuRenderer? _renderer;

    /// <summary>Initializes a new instance of the <see cref="RetailMenuHost"/> class.</summary>
    /// <param name="request">The menu's files, text, input, batch, scale and sounds, and where leaving goes.</param>
    /// <exception cref="ArgumentNullException">A required part of the request is <see langword="null"/>.</exception>
    public RetailMenuHost(in RetailMenuPresentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Input);
        ArgumentNullException.ThrowIfNull(request.Batch);
        ArgumentNullException.ThrowIfNull(request.ScreenWidth);
        ArgumentNullException.ThrowIfNull(request.ScreenHeight);
        ArgumentNullException.ThrowIfNull(request.Leave);
        ArgumentNullException.ThrowIfNull(request.Logger);

        _input = request.Input;
        _batch = request.Batch;
        _screenWidth = request.ScreenWidth;
        _screenHeight = request.ScreenHeight;
        _leave = request.Leave;
        _sounds = request.Sounds;
        _logger = request.Logger;
        Scale = request.Scale;
        _renderer = new MenuRenderer(
            request.Device,
            request.FileSystem,
            request.ResourcePath,
            request.Strings,
            request.ExtraArchives)
        {
            Scale = request.Scale,
        };
    }

    /// <inheritdoc/>
    public bool IsKeyDown(int scanCode) => scanCode is > 0 and <= byte.MaxValue && _input.Keyboard.IsDown((KeyCode)scanCode);

    /// <inheritdoc/>
    public int MouseButtons
    {
        get
        {
            int buttons = 0;
            for (int i = 0; i < MouseState.ButtonCount; i++)
            {
                if (_input.Mouse.IsDown((MouseButton)i))
                {
                    buttons |= 1 << i;
                }
            }

            float wheel = _input.Mouse.WheelDelta;
            return buttons | (wheel > 0f ? 0x100 : wheel < 0f ? 0x200 : 0);
        }
    }

    /// <inheritdoc/>
    public bool TryGetPointer(out int x, out int y, out bool buttonDown)
    {
        (int authoredX, int authoredY) = ToAuthored((int)_input.Mouse.X, (int)_input.Mouse.Y);
        x = authoredX;
        y = authoredY;
        buttonDown = _input.Mouse.IsDown(MouseButton.Left);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Drains the keyboard's press <em>queue</em> and maps each press onto the abstract code the
    /// manager consumes, which is what <c>MenuManager_HandleKeys</c> (<c>0x418580</c>) does with
    /// <c>Input_HasEvent</c>/<c>Input_NextEvent</c>.
    /// </para>
    /// <para>
    /// It has to be a queue rather than a per-frame edge test. The window dispatches key events at the
    /// display's rate and the manager reads them from a task update at the simulation's 30 Hz, so a
    /// press that begins and ends between two ticks — or simply one whose frame is not a tick frame —
    /// has its edge overwritten by the next frame's shadow before any tick can see it. That is what
    /// made the arrow keys and enter appear dead.
    /// </para>
    /// </remarks>
    public IEnumerable<MenuInputEvent> DrainEvents()
    {
        _events.Clear();

        while (_input.Keyboard.TryDequeuePress(out KeyCode key))
        {
            if (Map(key) is { } mapped)
            {
                _events.Add(mapped);
            }
        }

        return _events;
    }

    /// <summary>Maps a key onto the manager's abstract code.</summary>
    /// <param name="key">The key that was pressed.</param>
    /// <returns>The code, or <see langword="null"/> for a key the menu does not use.</returns>
    private static MenuInputEvent? Map(KeyCode key) => key switch
    {
        KeyCode.Down or KeyCode.Numpad2 => MenuInputEvent.Next,
        KeyCode.Up or KeyCode.Numpad8 => MenuInputEvent.Previous,
        KeyCode.Left or KeyCode.Numpad4 => MenuInputEvent.Left,
        KeyCode.Right or KeyCode.Numpad6 => MenuInputEvent.Right,
        KeyCode.Return or KeyCode.NumpadEnter => MenuInputEvent.Enter,
        KeyCode.Escape => MenuInputEvent.Escape,
        KeyCode.Backspace => MenuInputEvent.Backspace,
        KeyCode.Delete => MenuInputEvent.Delete,
        KeyCode.Home => MenuInputEvent.Home,
        KeyCode.End => MenuInputEvent.End,
        _ => null,
    };

    /// <inheritdoc/>
    public void PrepareWindow(MenuWindowTask window) => _renderer?.Prepare(window);

    /// <inheritdoc/>
    public void DrawScreen(MenuScreenTask screen) =>
        _renderer?.Draw(_batch, screen, _screenWidth(), _screenHeight());

    /// <inheritdoc/>
    public void DrawPopup(DialogWindowTask popup, int alpha) =>
        _renderer?.DrawPopup(_batch, popup, alpha, _screenWidth(), _screenHeight());

    /// <inheritdoc/>
    /// <remarks>Drained from the window's own character queue, which is separate from the key queue.</remarks>
    public IEnumerable<char> DrainTypedText()
    {
        _typed.Clear();
        while (_input.Keyboard.TryDequeueCharacter(out char typed))
        {
            _typed.Add(typed);
        }

        return _typed;
    }

    /// <inheritdoc/>
    public void DrawPointer(int x, int y) =>
        _renderer?.DrawPointer(_batch, x, y, _screenWidth(), _screenHeight());

    /// <inheritdoc/>
    /// <remarks>
    /// Menu sounds are flat and centred: they belong to the interface rather than to a place in the
    /// world, so nothing about them is positional. They come from the menu system's own archive,
    /// mounted apart from any mission.
    /// </remarks>
    public void PlaySound(string name)
    {
        if (_sounds is null || string.IsNullOrEmpty(name))
        {
            return;
        }

        // Priority above the default so a click is never the voice a busy pool decides to drop -- the
        // interface going silent reads as the interface having stopped responding.
        _sounds.Play(name, VoiceParameters.Default with { Category = SoundCategory.Effects, Priority = 100 });
    }

    /// <inheritdoc/>
    public void LeaveMenus(MenuManagerState state)
    {
        _logger.Information(LogCategory, $"Leaving the menus: {state}.");
        _leave(state);
    }

    // The exact inverse of MenuLayout.ToScreen, magnification included -- if this and the renderer
    // disagreed about the scale the cursor would sit somewhere other than what it is pointing at.
    private static (int X, int Y) ToAuthoredCore(int x, int y, int width, int height, int scale)
    {
        int s = scale < UiScale.Native ? UiScale.Native : scale;
        return (
            ((x - (width >> 1)) / s) + (MenuLayout.AuthoredWidth / 2),
            ((y - (height >> 1)) / s) + (MenuLayout.AuthoredHeight / 2));
    }

    private (int X, int Y) ToAuthored(int x, int y)
    {
        int width = _screenWidth();
        int height = _screenHeight();
        return ToAuthoredCore(x, y, width, height, UiScale.Resolve(Scale, width, height));
    }

    /// <summary>Gets or sets the magnification the menu is drawn at; <see cref="UiScale.Automatic"/> derives one.</summary>
    /// <remarks>It must match the renderer's, because this is the hit-test's half of the same mapping.</remarks>
    public int Scale { get; }

    /// <inheritdoc/>
    public void CloseScreens()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    /// <inheritdoc/>
    public void Dispose() => CloseScreens();
}
