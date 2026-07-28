namespace OpenIGI.Formats.World;

/// <summary>
/// A model, texture or lightmap id as it appears in a pack, split into the pack it addresses and the
/// index within that pack.
/// </summary>
/// <remarks>
/// <para>
/// Ids below <see cref="CommonBase"/> address the level-local pack; ids at or above it address the
/// per-location common pack after subtracting <see cref="CommonBase"/>. The original does this inline at
/// every use site (<c>0x4CF080</c>); this type keeps the rule in one place.
/// </para>
/// <para>
/// Retail packs never reference the common pack this way — every id observed in the shipped data is
/// level-local — but the split is real code in the original and a mod could use it.
/// </para>
/// </remarks>
/// <param name="Value">The raw id as stored in the pack.</param>
public readonly record struct PackId(uint Value)
{
    /// <summary>The id at which the common pack's index space begins.</summary>
    public const uint CommonBase = 0x2000;

    /// <summary>The id that means "none", stored as <c>0xFFFFFFFF</c>.</summary>
    public const uint NoneValue = 0xFFFFFFFF;

    /// <summary>Gets the id that means "none".</summary>
    public static PackId None => new(NoneValue);

    /// <summary>Gets a value indicating whether this id addresses nothing.</summary>
    public bool IsNone => Value == NoneValue;

    /// <summary>Gets a value indicating whether this id addresses the common pack rather than the level pack.</summary>
    public bool IsCommon => !IsNone && Value >= CommonBase;

    /// <summary>Gets the index within the pack that <see cref="IsCommon"/> selects.</summary>
    /// <exception cref="InvalidOperationException">The id is <see cref="None"/>.</exception>
    public int Index => IsNone
        ? throw new InvalidOperationException("A 'none' pack id has no index.")
        : (int)(IsCommon ? Value - CommonBase : Value);

    /// <inheritdoc/>
    public override string ToString() => IsNone ? "none" : IsCommon ? $"common[{Index}]" : $"level[{Index}]";
}
