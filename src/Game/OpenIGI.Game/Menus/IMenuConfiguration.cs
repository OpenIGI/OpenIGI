using OpenIGI.Scripting.Expressions;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Menus;

/// <summary>The configuration functions a game's menu expressions may call (its <c>Config_*</c> natives).</summary>
/// <remarks>
/// Each game's settings are its own; the menu manager only hands them the control a fill call writes into and asks them
/// whether a name is one of theirs.
/// </remarks>
public interface IMenuConfiguration
{
    /// <summary>Gets or sets the control a fill call is currently targeting, if any.</summary>
    /// <remarks>The manager sets it before running the control's expression.</remarks>
    MenuWindowTask? Target { get; set; }

    /// <summary>Handles a call, if it is one of these.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The unevaluated arguments.</param>
    /// <param name="result">Receives the result.</param>
    /// <returns><see langword="false"/> when the name is not one of these functions.</returns>
    bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result);
}
