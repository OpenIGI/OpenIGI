using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Menus;

/// <summary>What a menu control asks of the manager it belongs to: its scripts run, its expressions read, its sounds played.</summary>
/// <remarks>
/// The control's field <c>+68</c> in the original, which the base window's constructor threads down from the parent. Each
/// game's manager runs the scripts in its own expression dialect, so a control written once runs under either.
/// </remarks>
public interface IMenuManager
{
    /// <summary>Gets the menu's pointer, which a dragged control follows.</summary>
    MenuPointer Pointer { get; }

    /// <summary>Runs one of a control's scripts, with <c>this</c> bound to the control.</summary>
    /// <param name="source">The script's source, which may be empty.</param>
    /// <param name="self">The control running it.</param>
    void RunAction(string source, GameTask? self = null);

    /// <summary>Evaluates one of a control's expressions as a number, with <c>this</c> bound to the control.</summary>
    /// <param name="source">The expression's source, which may be empty.</param>
    /// <param name="self">The control evaluating it.</param>
    /// <returns>Its value, or 0 for an empty one.</returns>
    double EvaluateReal(string source, GameTask? self = null);

    /// <summary>Plays one of the menu's sounds.</summary>
    /// <param name="name">The sound's name.</param>
    void PlaySound(string name);
}
