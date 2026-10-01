using System.Globalization;

namespace OpenIGI.Platform.Web;

/// <summary>What happened on the page, as one of its queued events says.</summary>
public enum WebInputEventKind
{
    /// <summary>Nothing the window understands.</summary>
    Unknown,

    /// <summary>A key went down or up: <see cref="WebInputEvent.Code"/> and <see cref="WebInputEvent.Down"/>.</summary>
    Key,

    /// <summary>A character was typed: <see cref="WebInputEvent.Code"/> holds it.</summary>
    Character,

    /// <summary>The pointer moved: a motion in X and Y, then a position in Z and W.</summary>
    Move,

    /// <summary>A mouse button went down or up: its DOM number in X, and <see cref="WebInputEvent.Down"/>.</summary>
    Button,

    /// <summary>The wheel turned: notches in X, positive away from the player.</summary>
    Wheel,

    /// <summary>The canvas is shown at a new size: the width in X and the height in Y, in device pixels.</summary>
    Resize,

    /// <summary>The page gained or lost the keyboard: <see cref="WebInputEvent.Down"/>.</summary>
    Focus,

    /// <summary>The pointer lock was granted or lost: <see cref="WebInputEvent.Down"/>.</summary>
    PointerLock,

    /// <summary>The page is going away.</summary>
    Close,
}

/// <summary>One event from the page's queue.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Code">The key's <c>KeyboardEvent.code</c>, or the typed character.</param>
/// <param name="Down">Down, gained or granted; otherwise up, lost or refused.</param>
/// <param name="X">The first number the event carries.</param>
/// <param name="Y">The second.</param>
/// <param name="Z">The third.</param>
/// <param name="W">The fourth.</param>
/// <remarks>
/// The page writes each event as one line of words -- <c>key KeyW 1</c>, <c>move 3 -1 640 360</c>,
/// <c>char a</c> -- because a line crosses from JavaScript as a string without a layout either side has
/// to agree on, and a frame queues a handful of them.
/// </remarks>
public readonly record struct WebInputEvent(
    WebInputEventKind Kind,
    string Code = "",
    bool Down = false,
    float X = 0f,
    float Y = 0f,
    float Z = 0f,
    float W = 0f)
{
    /// <summary>Reads one event the page wrote.</summary>
    /// <param name="line">The event.</param>
    /// <returns>It, or an <see cref="WebInputEventKind.Unknown"/> event for a line this does not read.</returns>
    public static WebInputEvent Parse(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return new WebInputEvent(WebInputEventKind.Unknown);
        }

        int space = line.IndexOf(' ', StringComparison.Ordinal);
        string kind = space < 0 ? line : line[..space];
        string rest = space < 0 ? string.Empty : line[(space + 1)..];

        // A typed character is everything after the word, and may itself be a space.
        if (kind == "char")
        {
            return new WebInputEvent(WebInputEventKind.Character, rest);
        }

        string[] words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return kind switch
        {
            "key" when words.Length == 2 => new WebInputEvent(WebInputEventKind.Key, words[0], Flag(words[1])),
            "move" when words.Length == 4 => new WebInputEvent(
                WebInputEventKind.Move, X: Number(words[0]), Y: Number(words[1]), Z: Number(words[2]), W: Number(words[3])),
            "button" when words.Length == 2 => new WebInputEvent(WebInputEventKind.Button, Down: Flag(words[1]), X: Number(words[0])),
            "wheel" when words.Length == 1 => new WebInputEvent(WebInputEventKind.Wheel, X: Number(words[0])),
            "resize" when words.Length == 2 => new WebInputEvent(WebInputEventKind.Resize, X: Number(words[0]), Y: Number(words[1])),
            "focus" when words.Length == 1 => new WebInputEvent(WebInputEventKind.Focus, Down: Flag(words[0])),
            "lock" when words.Length == 1 => new WebInputEvent(WebInputEventKind.PointerLock, Down: Flag(words[0])),
            "close" => new WebInputEvent(WebInputEventKind.Close),
            _ => new WebInputEvent(WebInputEventKind.Unknown, line),
        };
    }

    private static bool Flag(string word) => word == "1";

    private static float Number(string word) =>
        float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
}
