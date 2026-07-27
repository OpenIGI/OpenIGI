namespace OpenIGI.Scripting.Expressions;

/// <summary>
/// What an expression can reach: the names it may read, call and assign to.
/// </summary>
/// <remarks>
/// <para>
/// The original resolves a name through the global script namespace and caches the resolved symbol on
/// the node, so a name is looked up once per compiled expression and every later evaluation reuses it.
/// The caching is an implementation detail; what a host must supply is this.
/// </para>
/// <para>
/// A name may be a native function, a registered constant, or a member path — <c>this.nUserData</c>,
/// <c>MenuScreen_905.nActiveID</c> — naming a property of the owning task or of an exported instance.
/// The context decides which; the evaluator only distinguishes "called" from "read".
/// </para>
/// </remarks>
public interface IScriptExpressionContext
{
    /// <summary>Reads a name's value.</summary>
    /// <param name="name">The name, possibly a dotted member path.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="false"/> when the name is unknown, which the evaluator reports as <c>"Unknown identifier"</c>.</returns>
    bool TryGetValue(string name, out int value);

    /// <summary>Assigns to a name.</summary>
    /// <param name="name">The name, possibly a dotted member path.</param>
    /// <param name="value">The value to store.</param>
    /// <returns><see langword="false"/> when the name is unknown or not assignable.</returns>
    bool TrySetValue(string name, int value);

    /// <summary>Calls a native function.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The already-evaluated arguments, in source order.</param>
    /// <param name="result">Receives the return value.</param>
    /// <returns><see langword="false"/> when no such function exists.</returns>
    bool TryInvoke(string name, ReadOnlySpan<int> arguments, out int result);
}
