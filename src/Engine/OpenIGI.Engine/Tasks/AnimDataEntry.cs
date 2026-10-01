namespace OpenIGI.Engine.Tasks;

/// <summary>
/// One entry of an <c>AnimData</c> parameter: an opcode and its value.
/// </summary>
/// <param name="Code">The entry's opcode, stored by the original as a single signed byte.</param>
/// <param name="Value">The entry's value, truncated toward zero when <paramref name="Code"/> is 32.</param>
/// <remarks>
/// <c>AnimData</c> is the only parameter type whose wire length depends on its own data: the parse
/// callback (<c>0x4F3570</c>) reads a count and then two arguments per entry. What the opcodes mean is
/// animation-system work and is not yet reverse-engineered; the entries are carried verbatim so the
/// argument cursor stays correct, which is what the surrounding tree depends on.
/// </remarks>
public readonly record struct AnimDataEntry(int Code, float Value);
