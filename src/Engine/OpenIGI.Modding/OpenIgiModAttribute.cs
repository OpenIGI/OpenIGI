using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Modding;

/// <summary>Marks a mod's class. On a <see langword="partial"/> class, the build generates its <see cref="IOpenIgiMod"/>.</summary>
/// <remarks>
/// <para>
/// On a class, the build generates the class's <see cref="IOpenIgiMod.Configure"/> from the attributes on its members and on
/// the assembly's task classes, and names the class to the loader. That is the usual way to write a mod.
/// </para>
/// <para>
/// On the assembly, with a type, it names a class that implements <see cref="IOpenIgiMod"/> by hand. The generated code
/// writes that form for the loader, which reads only it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false)]
public sealed class OpenIgiModAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="OpenIgiModAttribute"/> class, on the mod's class.</summary>
    public OpenIgiModAttribute()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="OpenIgiModAttribute"/> class, on the assembly, naming its mod.</summary>
    /// <param name="modType">The class, which has a public parameterless constructor.</param>
    public OpenIgiModAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type modType) =>
        ModType = modType ?? throw new ArgumentNullException(nameof(modType));

    /// <summary>Gets the class the assembly names, or <see langword="null"/> on a class.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type? ModType { get; }
}
