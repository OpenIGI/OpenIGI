namespace OpenIGI.UI.Widgets;

/// <summary>A viewport the mouse wheel scrolls while the pointer is over it.</summary>
/// <remarks>
/// The screen offers the wheel to the innermost one under the pointer first; one with nothing left to scroll
/// declines, and the one enclosing it is asked next.
/// </remarks>
public interface IWheelScrollable
{
    /// <summary>Scrolls in response to the mouse wheel.</summary>
    /// <param name="notches">The wheel movement, positive away from the user.</param>
    /// <returns><see langword="true"/> when this viewport had somewhere to scroll and took the wheel.</returns>
    bool ScrollByWheel(float notches);
}
