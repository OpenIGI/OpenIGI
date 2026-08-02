using OpenIGI.Core.Configuration;
using OpenIGI.Input;

namespace OpenIGI.Desktop;

/// <summary>
/// Turns key presses into console editing.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from <see cref="ConsoleModel"/>, which knows nothing about input devices, and from
/// <see cref="ConsoleUiRenderer"/>, which knows nothing about editing. This is the only piece that has
/// to know a key code from a caret.
/// </para>
/// <para>
/// Once it has read a frame's input it sets <see cref="InputState.Captured"/>, so every other reader
/// — the player, the cameras, the menu — sees an idle keyboard and mouse for the rest of that frame.
/// That is why typing <c>r_fog 0</c> at the prompt cannot also walk the player forwards, and why no
/// call site had to learn about the console.
/// </para>
/// </remarks>
/// <param name="model">The console state to drive.</param>
public sealed class ConsoleInputController(ConsoleModel model)
{
    /// <summary>How long the caret spends in each blink phase, in seconds.</summary>
    private const float BlinkPeriod = 0.53f;

    private readonly ConsoleModel _model = model ?? throw new ArgumentNullException(nameof(model));
    private float _blink;

    /// <summary>Gets a value indicating whether the caret is in its visible phase.</summary>
    public bool CaretVisible { get; private set; } = true;

    /// <summary>Gets the key that opens and closes the console.</summary>
    public static KeyCode ToggleKey => KeyCode.Grave;

    /// <summary>Advances the caret blink.</summary>
    /// <param name="elapsedSeconds">How long since the last call.</param>
    public void Advance(float elapsedSeconds)
    {
        if (!_model.IsOpen)
        {
            _blink = 0f;
            CaretVisible = true;
            return;
        }

        _blink += elapsedSeconds;
        while (_blink >= BlinkPeriod)
        {
            _blink -= BlinkPeriod;
            CaretVisible = !CaretVisible;
        }
    }

    /// <summary>Applies a frame's input to the console.</summary>
    /// <param name="input">The input state for this frame.</param>
    /// <param name="visibleLines">How many lines the view shows, used to clamp page scrolling.</param>
    /// <returns><see langword="true"/> when the console consumed the input and the game should ignore it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    public bool Update(InputState input, int visibleLines)
    {
        ArgumentNullException.ThrowIfNull(input);

        KeyboardState keyboard = input.Keyboard;

        if (keyboard.WasPressed(ToggleKey))
        {
            _model.IsOpen = !_model.IsOpen;
            ResetBlink();

            // The key that opened the console also queues a '`' character; dropping the queue stops it
            // being typed into the prompt.
            DrainCharacters(keyboard);
            input.Captured = true;
            return true;
        }

        if (!_model.IsOpen)
        {
            return false;
        }

        if (keyboard.WasPressed(KeyCode.Escape))
        {
            _model.IsOpen = false;
            DrainCharacters(keyboard);
            input.Captured = true;
            return true;
        }

        bool control = keyboard.IsDown(KeyCode.LeftControl) || keyboard.IsDown(KeyCode.RightControl);

        HandleEditingKeys(keyboard, control, visibleLines);
        HandleTypedCharacters(keyboard);

        // Set only now that the console has read what it needs: from here on every other reader --
        // the player, the cameras, the menu -- sees an idle keyboard and mouse for this frame.
        input.Captured = true;
        return true;
    }

    private void HandleEditingKeys(KeyboardState keyboard, bool control, int visibleLines)
    {
        if (keyboard.WasPressed(KeyCode.Return) || keyboard.WasPressed(KeyCode.NumpadEnter))
        {
            _model.Submit();
            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Tab))
        {
            _model.Complete();
            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Backspace))
        {
            _model.Backspace();
            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Delete))
        {
            _model.Delete();
            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Left))
        {
            if (control)
            {
                _model.MoveCaretByWord(-1);
            }
            else
            {
                _model.MoveCaret(-1);
            }

            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Right))
        {
            if (control)
            {
                _model.MoveCaretByWord(1);
            }
            else
            {
                _model.MoveCaret(1);
            }

            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Home))
        {
            _model.CaretToStart();
            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.End))
        {
            _model.CaretToEnd();
            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Up))
        {
            _model.HistoryPrevious();
            ResetBlink();
        }

        if (keyboard.WasPressed(KeyCode.Down))
        {
            _model.HistoryNext();
            ResetBlink();
        }

        // A page is one screen less a couple of lines, so there is always overlap to read against.
        int page = Math.Max(1, visibleLines - 2);

        if (keyboard.WasPressed(KeyCode.PageUp))
        {
            _model.Scroll(page, visibleLines);
        }

        if (keyboard.WasPressed(KeyCode.PageDown))
        {
            _model.Scroll(-page, visibleLines);
        }
    }

    private void HandleTypedCharacters(KeyboardState keyboard)
    {
        while (keyboard.TryDequeueCharacter(out char typed))
        {
            // The toggle key's own character never reaches the prompt, so closing and reopening the
            // console does not leave a backtick in the line.
            if (typed is '`' or '~' or '\r' or '\n' or '\t' or '\b')
            {
                continue;
            }

            _model.Insert(typed.ToString());
            ResetBlink();
        }
    }

    private static void DrainCharacters(KeyboardState keyboard)
    {
        while (keyboard.TryDequeueCharacter(out _))
        {
            // Discarded on purpose.
        }
    }

    private void ResetBlink()
    {
        _blink = 0f;
        CaretVisible = true;
    }
}
