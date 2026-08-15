namespace OpenIGI.Core.Configuration;

/// <summary>
/// The type-agnostic face of a console variable.
/// </summary>
/// <remarks>
/// The console, the settings file and the options menu all work through this interface, so none of
/// them needs to know a variable's concrete type. Code that owns a variable holds the typed
/// <see cref="CVar{T}"/> instead and reads <see cref="CVar{T}.Value"/> without any conversion.
/// </remarks>
public interface ICVar
{
    /// <summary>Occurs after the value has changed.</summary>
    event EventHandler<CVarChangedEventArgs>? Changed;

    /// <summary>Gets the registry key, by convention lower case with an underscore-separated prefix.</summary>
    /// <remarks>Prefixes group the registry: <c>r_</c> renderer, <c>snd_</c> audio, <c>ui_</c> shell, <c>g_</c> gameplay.</remarks>
    string Name { get; }

    /// <summary>Gets the one-line description shown by the console and the options menu.</summary>
    string Help { get; }

    /// <summary>Gets the variable's behavioural attributes.</summary>
    CVarOptions Flags { get; }

    /// <summary>Gets the type of the value the variable holds.</summary>
    Type ValueType { get; }

    /// <summary>Gets the current value rendered as text, in the form the settings file stores.</summary>
    string ValueText { get; }

    /// <summary>Gets the default value rendered as text.</summary>
    string DefaultText { get; }

    /// <summary>Gets a human-readable description of what the variable accepts, or an empty string when unconstrained.</summary>
    /// <remarks>For example <c>"0 .. 4"</c> for a bounded number, or <c>"off | low | high"</c> for an enumeration.</remarks>
    string RangeText { get; }

    /// <summary>Gets every value the variable accepts, or an empty list when they cannot be enumerated.</summary>
    /// <remarks>
    /// <para>
    /// What the console completes a half-typed <em>value</em> from, and the type-agnostic counterpart to
    /// <see cref="RangeText"/>: that is prose for a human to read, this is the list a picker can offer.
    /// A boolean yields its two forms and an enumeration its members; a number yields nothing, because a
    /// range is not a set, and the console shows <see cref="RangeText"/> as a hint instead.
    /// </para>
    /// <para>
    /// Defaulted so that adding it costs no existing implementation anything — a variable that cannot
    /// enumerate its values simply says so by staying silent.
    /// </para>
    /// </remarks>
    IReadOnlyList<string> ValueChoices => [];

    /// <summary>Gets a value indicating whether the current value equals the default.</summary>
    bool IsDefault { get; }

    /// <summary>Parses <paramref name="text"/> and assigns it.</summary>
    /// <param name="text">The value to parse.</param>
    /// <param name="reason">Receives why the assignment failed, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when the value was parsed and assigned.</returns>
    /// <remarks>
    /// Out-of-range numbers are clamped rather than rejected, which is what a player dragging a slider
    /// or typing a value expects; only text that does not parse at all is refused.
    /// </remarks>
    bool TrySetText(string text, out string? reason);

    /// <summary>Restores the default value.</summary>
    void Reset();
}
