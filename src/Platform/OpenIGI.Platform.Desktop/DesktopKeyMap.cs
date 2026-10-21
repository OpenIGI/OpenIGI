using OpenIGI.Input;
using SilkKey = Silk.NET.Input.Key;

namespace OpenIGI.Platform.Desktop;

/// <summary>
/// Translates the host toolkit's key identifiers into the DirectInput scan codes the engine indexes
/// by.
/// </summary>
/// <remarks>
/// The engine's key arrays are indexed by <c>DIK_*</c> scan code because that is what the original's
/// configuration files store. Silk.NET reports GLFW-style key identifiers, so every host key has to be
/// mapped rather than cast. Both sets are positional, so the mapping is layout-independent.
/// </remarks>
public static class DesktopKeyMap
{
    private static readonly Dictionary<SilkKey, KeyCode> Map = new()
    {
        [SilkKey.Escape] = KeyCode.Escape,
        [SilkKey.Number1] = KeyCode.D1,
        [SilkKey.Number2] = KeyCode.D2,
        [SilkKey.Number3] = KeyCode.D3,
        [SilkKey.Number4] = KeyCode.D4,
        [SilkKey.Number5] = KeyCode.D5,
        [SilkKey.Number6] = KeyCode.D6,
        [SilkKey.Number7] = KeyCode.D7,
        [SilkKey.Number8] = KeyCode.D8,
        [SilkKey.Number9] = KeyCode.D9,
        [SilkKey.Number0] = KeyCode.D0,
        [SilkKey.Minus] = KeyCode.Minus,
        [SilkKey.Equal] = KeyCode.Equals,
        [SilkKey.Backspace] = KeyCode.Backspace,
        [SilkKey.Tab] = KeyCode.Tab,
        [SilkKey.Q] = KeyCode.Q,
        [SilkKey.W] = KeyCode.W,
        [SilkKey.E] = KeyCode.E,
        [SilkKey.R] = KeyCode.R,
        [SilkKey.T] = KeyCode.T,
        [SilkKey.Y] = KeyCode.Y,
        [SilkKey.U] = KeyCode.U,
        [SilkKey.I] = KeyCode.I,
        [SilkKey.O] = KeyCode.O,
        [SilkKey.P] = KeyCode.P,
        [SilkKey.LeftBracket] = KeyCode.LeftBracket,
        [SilkKey.RightBracket] = KeyCode.RightBracket,
        [SilkKey.Enter] = KeyCode.Return,
        [SilkKey.ControlLeft] = KeyCode.LeftControl,
        [SilkKey.A] = KeyCode.A,
        [SilkKey.S] = KeyCode.S,
        [SilkKey.D] = KeyCode.D,
        [SilkKey.F] = KeyCode.F,
        [SilkKey.G] = KeyCode.G,
        [SilkKey.H] = KeyCode.H,
        [SilkKey.J] = KeyCode.J,
        [SilkKey.K] = KeyCode.K,
        [SilkKey.L] = KeyCode.L,
        [SilkKey.Semicolon] = KeyCode.Semicolon,
        [SilkKey.Apostrophe] = KeyCode.Apostrophe,
        [SilkKey.GraveAccent] = KeyCode.Grave,
        [SilkKey.ShiftLeft] = KeyCode.LeftShift,
        [SilkKey.BackSlash] = KeyCode.Backslash,
        [SilkKey.Z] = KeyCode.Z,
        [SilkKey.X] = KeyCode.X,
        [SilkKey.C] = KeyCode.C,
        [SilkKey.V] = KeyCode.V,
        [SilkKey.B] = KeyCode.B,
        [SilkKey.N] = KeyCode.N,
        [SilkKey.M] = KeyCode.M,
        [SilkKey.Comma] = KeyCode.Comma,
        [SilkKey.Period] = KeyCode.Period,
        [SilkKey.Slash] = KeyCode.Slash,
        [SilkKey.ShiftRight] = KeyCode.RightShift,
        [SilkKey.KeypadMultiply] = KeyCode.NumpadMultiply,
        [SilkKey.AltLeft] = KeyCode.LeftAlt,
        [SilkKey.Space] = KeyCode.Space,
        [SilkKey.CapsLock] = KeyCode.CapsLock,
        [SilkKey.F1] = KeyCode.F1,
        [SilkKey.F2] = KeyCode.F2,
        [SilkKey.F3] = KeyCode.F3,
        [SilkKey.F4] = KeyCode.F4,
        [SilkKey.F5] = KeyCode.F5,
        [SilkKey.F6] = KeyCode.F6,
        [SilkKey.F7] = KeyCode.F7,
        [SilkKey.F8] = KeyCode.F8,
        [SilkKey.F9] = KeyCode.F9,
        [SilkKey.F10] = KeyCode.F10,
        [SilkKey.NumLock] = KeyCode.NumLock,
        [SilkKey.ScrollLock] = KeyCode.ScrollLock,
        [SilkKey.Keypad7] = KeyCode.Numpad7,
        [SilkKey.Keypad8] = KeyCode.Numpad8,
        [SilkKey.Keypad9] = KeyCode.Numpad9,
        [SilkKey.KeypadSubtract] = KeyCode.NumpadSubtract,
        [SilkKey.Keypad4] = KeyCode.Numpad4,
        [SilkKey.Keypad5] = KeyCode.Numpad5,
        [SilkKey.Keypad6] = KeyCode.Numpad6,
        [SilkKey.KeypadAdd] = KeyCode.NumpadAdd,
        [SilkKey.Keypad1] = KeyCode.Numpad1,
        [SilkKey.Keypad2] = KeyCode.Numpad2,
        [SilkKey.Keypad3] = KeyCode.Numpad3,
        [SilkKey.Keypad0] = KeyCode.Numpad0,
        [SilkKey.KeypadDecimal] = KeyCode.NumpadDecimal,
        [SilkKey.F11] = KeyCode.F11,
        [SilkKey.F12] = KeyCode.F12,
        [SilkKey.KeypadEnter] = KeyCode.NumpadEnter,
        [SilkKey.ControlRight] = KeyCode.RightControl,
        [SilkKey.KeypadDivide] = KeyCode.NumpadDivide,
        [SilkKey.PrintScreen] = KeyCode.PrintScreen,
        [SilkKey.AltRight] = KeyCode.RightAlt,
        [SilkKey.Pause] = KeyCode.Pause,
        [SilkKey.Home] = KeyCode.Home,
        [SilkKey.Up] = KeyCode.Up,
        [SilkKey.PageUp] = KeyCode.PageUp,
        [SilkKey.Left] = KeyCode.Left,
        [SilkKey.Right] = KeyCode.Right,
        [SilkKey.End] = KeyCode.End,
        [SilkKey.Down] = KeyCode.Down,
        [SilkKey.PageDown] = KeyCode.PageDown,
        [SilkKey.Insert] = KeyCode.Insert,
        [SilkKey.Delete] = KeyCode.Delete,
        [SilkKey.SuperLeft] = KeyCode.LeftWindows,
        [SilkKey.SuperRight] = KeyCode.RightWindows,
        [SilkKey.Menu] = KeyCode.Application,
    };

    /// <summary>Gets the number of host keys that have a scan code.</summary>
    public static int MappedKeyCount => Map.Count;

    /// <summary>Translates a host key to its scan code.</summary>
    /// <param name="key">The host key.</param>
    /// <returns>The scan code, or <see cref="KeyCode.None"/> for a key the original has no code for.</returns>
    public static KeyCode Translate(SilkKey key) => Map.GetValueOrDefault(key, KeyCode.None);
}
