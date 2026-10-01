using OpenIGI.Scripting.Runtime;

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
/// <para>
/// Values are typed, because the language is: a symbol carries a type tag of 1, 2 or 3 — integer,
/// <c>double</c> or string — and an assignment picks its evaluator from the <em>target's</em> tag.
/// </para>
/// </remarks>
public interface IScriptExpressionContext
{
    /// <summary>Reads a name's value, in whatever type its storage declares.</summary>
    /// <param name="name">The name, possibly a dotted member path.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns><see langword="false"/> when the name is unknown, which the evaluator reports as <c>"Unknown identifier"</c>.</returns>
    bool TryGetValue(string name, out QvmValue value);

    /// <summary>Gets the type a name's storage declares.</summary>
    /// <param name="name">The name, possibly a dotted member path.</param>
    /// <returns>
    /// The tag, or <see cref="QvmValueKind.None"/> when the name is unknown. An assignment runs the
    /// evaluator this names, so an unknown target falls back to the integer path.
    /// </returns>
    QvmValueKind GetValueKind(string name);

    /// <summary>Assigns to a name.</summary>
    /// <param name="name">The name, possibly a dotted member path.</param>
    /// <param name="value">The value to store, already evaluated in the target's type.</param>
    /// <returns><see langword="false"/> when the name is unknown or not assignable.</returns>
    bool TrySetValue(string name, QvmValue value);

    /// <summary>Calls a native function.</summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The <em>unevaluated</em> arguments.</param>
    /// <param name="result">Receives the return value.</param>
    /// <returns><see langword="false"/> when no such function exists.</returns>
    /// <remarks>
    /// Arguments arrive unevaluated because that is how the original works: a native is registered with
    /// an arity and no parameter types, and its body pulls each argument through <c>Arg_GetInt</c>,
    /// <c>Arg_GetFloat</c> or <c>Arg_GetString</c> as it wants it. An argument a native never reads is
    /// never evaluated, and the order of two arguments' side effects is the callee's to choose.
    /// </remarks>
    bool TryInvoke(string name, ScriptArguments arguments, out QvmValue result);
}
