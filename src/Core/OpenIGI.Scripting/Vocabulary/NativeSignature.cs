namespace OpenIGI.Scripting.Vocabulary;

/// <summary>Whether calling a native changes anything.</summary>
public enum NativeEffect
{
    /// <summary>It only reads: a pure node with no exec pins, safe to evaluate any number of times.</summary>
    Pure,

    /// <summary>It does something: an exec node in an event graph, an action in a mission's run-at-start string.</summary>
    Action,
}

/// <summary>Which kinds of script a native can be called from.</summary>
[Flags]
public enum ScriptContexts
{
    /// <summary>Nowhere.</summary>
    None = 0,

    /// <summary>A mission's level expressions: conditions and run-at-start/stop strings.</summary>
    Mission = 1,

    /// <summary>A soldier's AI event script.</summary>
    Ai = 2,

    /// <summary>A squad's AI event script.</summary>
    Squad = 4,

    /// <summary>A configuration script of <c>Define*</c> calls.</summary>
    Config = 8,

    /// <summary>The objects script itself, which runs once at load.</summary>
    Objects = 16,
}

/// <summary>What runs a native or task type: the original game, or only OpenIGI.</summary>
public enum ScriptRuntime
{
    /// <summary>The original executable has it; a mission made for retail may use it.</summary>
    Retail,

    /// <summary>Only OpenIGI has it (an engine or mod addition); a retail-target mission must not use it.</summary>
    OpenIgi,
}

/// <summary>One parameter of a native.</summary>
/// <param name="Name">The name shown on the pin.</param>
/// <param name="Type">What the native does with the value.</param>
/// <param name="Doc">One line on what it means, or <see langword="null"/>.</param>
public sealed record NativeParameter(string Name, ScriptType Type, string? Doc = null);

/// <summary>Everything the editor needs to know about calling a native: its pins, effect, where it runs and what it does.</summary>
/// <param name="Name">The name scripts call it by.</param>
/// <param name="Parameters">The parameters in call order.</param>
/// <param name="Returns">The result's type, or <see langword="null"/> for a call whose value nothing reads.</param>
/// <param name="Effect">Whether it changes anything.</param>
/// <param name="Contexts">Where it can be called from.</param>
/// <remarks>
/// A signature is written from how the native's body reads its arguments, then checked against every retail call site, so a
/// graph built from it compiles to calls the game accepts.
/// </remarks>
public sealed record NativeSignature(
    string Name,
    IReadOnlyList<NativeParameter> Parameters,
    ScriptType? Returns,
    NativeEffect Effect,
    ScriptContexts Contexts)
{
    /// <summary>Gets which runtime has the native.</summary>
    public ScriptRuntime Runtime { get; init; } = ScriptRuntime.Retail;

    /// <summary>Gets a value indicating whether calls may repeat the last parameter any number of times.</summary>
    public bool IsVariadic { get; init; }

    /// <summary>Gets how many trailing parameters a call may leave out, as a callback that counts its arguments allows.</summary>
    public int OptionalCount { get; init; }

    /// <summary>Gets the fewest arguments a call may pass.</summary>
    public int MinimumArity => Math.Max(0, Arity - OptionalCount);

    /// <summary>Gets the explanation shown in the editor, or <see langword="null"/>.</summary>
    public string? Doc { get; init; }

    /// <summary>Gets the palette category, such as <c>AI|Actions</c>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Gets the number of declared parameters.</summary>
    public int Arity => Parameters.Count;

    /// <summary>Whether a call with this many arguments matches the signature.</summary>
    /// <param name="count">The number of arguments a call site passes.</param>
    /// <returns><see langword="true"/> when the count fits.</returns>
    public bool Accepts(int count) => count >= MinimumArity && (IsVariadic || count <= Arity);
}
