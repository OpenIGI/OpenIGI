namespace OpenIGI.Game.Weapons;

/// <summary>What kind of argument a weapon task class reads.</summary>
public enum WeaponTaskParameterKind
{
    /// <summary>An integer.</summary>
    Int32,

    /// <summary>A single-precision number.</summary>
    Real32,

    /// <summary>A short string — a model name.</summary>
    String16,
}

/// <summary>What a weapon task class does to an argument before storing it.</summary>
public enum WeaponTaskParameterTransform
{
    /// <summary>The value is stored as read.</summary>
    None,

    /// <summary>
    /// The value is a field of view in whole degrees, stored as the tangent of its half-angle.
    /// </summary>
    HalfAngleTangent,
}

/// <summary>One argument a weapon task class reads.</summary>
/// <param name="Kind">How the argument is evaluated.</param>
/// <param name="Transform">What the class does to it.</param>
/// <param name="StoreIndex">
/// Which slot of the parameter block it lands in. Usually the reading order, but the binoculars store
/// their third angle first.
/// </param>
public sealed record WeaponTaskParameterSlot(
    WeaponTaskParameterKind Kind,
    WeaponTaskParameterTransform Transform,
    int StoreIndex);

/// <summary>
/// A task class named by a weapon, together with the parameters it read from the same call.
/// </summary>
/// <remarks>
/// The original allocates a small block per weapon and stores the pointer beside the task type —
/// <c>+1760</c> for the projectile's and <c>+1764</c> for the weapon's. This carries both halves.
/// </remarks>
public sealed class WeaponTaskBinding
{
    internal WeaponTaskBinding(
        string typeName,
        int typeId,
        IReadOnlyList<float> values,
        string text,
        int argumentCount)
    {
        TypeName = typeName;
        TypeId = typeId;
        Values = values;
        Text = text;
        ArgumentCount = argumentCount;
    }

    /// <summary>Gets the <c>TASKTYPE_*</c> constant the script named.</summary>
    public string TypeName { get; }

    /// <summary>Gets the value that constant resolved to.</summary>
    public int TypeId { get; }

    /// <summary>Gets the decoded numeric parameters, in the order the class stores them.</summary>
    public IReadOnlyList<float> Values { get; }

    /// <summary>Gets the string parameter, or an empty string when the class reads none.</summary>
    public string Text { get; }

    /// <summary>Gets how many arguments the class consumed.</summary>
    public int ArgumentCount { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{TypeName} (+{ArgumentCount} args)";
}
