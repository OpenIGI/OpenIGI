namespace OpenIGI.Core.Configuration;

/// <summary>
/// A console variable holding one of a fixed set of named choices.
/// </summary>
/// <typeparam name="TEnum">The enumeration the variable selects from.</typeparam>
/// <remarks>
/// Stored and printed by name in lower case (<c>aces</c>, not <c>2</c>), which makes a settings file
/// readable and keeps it valid when a member is inserted. The numeric form is still accepted on input
/// so scripted configuration and debug tooling can use it.
/// </remarks>
public sealed class EnumCVar<TEnum> : CVar<TEnum>
    where TEnum : struct, Enum
{
    private static readonly TEnum[] Values = Enum.GetValues<TEnum>();

    /// <summary>Initialises a new instance of the <see cref="EnumCVar{TEnum}"/> class.</summary>
    /// <param name="name">The registry key.</param>
    /// <param name="defaultValue">The value the variable holds until something changes it.</param>
    /// <param name="flags">The variable's behavioural attributes.</param>
    /// <param name="help">The one-line description shown by the console and the options menu.</param>
    public EnumCVar(string name, TEnum defaultValue, CVarOptions flags = CVarOptions.None, string help = "")
        : base(name, defaultValue, flags, help)
    {
    }

    /// <summary>Gets the choices the variable accepts, in declaration order.</summary>
    /// <remarks>The options menu binds a dropdown to this rather than parsing <see cref="RangeText"/>.</remarks>
    public IReadOnlyList<TEnum> Choices => Values;

    /// <inheritdoc/>
    public override string RangeText => string.Join(" | ", Array.ConvertAll(Values, Render));

    /// <inheritdoc/>
    protected override string FriendlyTypeName => typeof(TEnum).Name;

    /// <inheritdoc/>
    protected override bool TryParse(string text, out TEnum value) =>
        Enum.TryParse(text.Trim(), ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <inheritdoc/>
    protected override string Format(TEnum value) => Render(value);

    private static string Render(TEnum value) => value.ToString().ToLowerInvariant();
}
