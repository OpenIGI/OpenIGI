using System.Text.RegularExpressions;

namespace OpenIGI.Tools.WebGpuBind;

/// <summary>
/// Turns a C type into the C# type the binding uses.
/// </summary>
/// <remarks>
/// <para>
/// Every mapping here is exact in width and blittable, because the point of the binding layer is that
/// nothing is marshalled: a descriptor is written into a C# struct of the same layout and its address
/// is passed. A type mapped a word too narrow does not fail — it reads the next field's bytes, which
/// presents as a descriptor that is mysteriously rejected or, worse, accepted with the wrong values.
/// </para>
/// <para>
/// A pointer becomes a pointer. Const-ness does not survive into C#, which is a real loss and the
/// reason the hand-written layer above this one exists.
/// </para>
/// </remarks>
public sealed partial class TypeMap
{
    private static readonly Dictionary<string, string> Scalars = new(StringComparer.Ordinal)
    {
        ["void"] = "void",
        ["char"] = "byte",
        ["float"] = "float",
        ["double"] = "double",
        ["int"] = "int",
        ["int8_t"] = "sbyte",
        ["int16_t"] = "short",
        ["int32_t"] = "int",
        ["int64_t"] = "long",
        ["uint8_t"] = "byte",
        ["uint16_t"] = "ushort",
        ["uint32_t"] = "uint",
        ["uint64_t"] = "ulong",
        ["size_t"] = "nuint",

        // typedef uint32_t WGPUBool. Not bool: C# marshals that as four bytes only by convention, and
        // the convention differs between contexts. The width is the contract.
        ["WGPUBool"] = "uint",

        // typedef uint64_t WGPUFlags, which is what every flags enumeration is stored in.
        ["WGPUFlags"] = "ulong",
        ["WGPUSubmissionIndex"] = "ulong",
        ["WGPUProc"] = "nint",
    };

    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _callbacks = new(StringComparer.Ordinal);

    /// <summary>Initialises a new instance of the <see cref="TypeMap"/> class.</summary>
    /// <param name="model">The declarations whose names are types in their own right.</param>
    public TypeMap(HeaderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (EnumDeclaration declaration in model.Enums)
        {
            _known.Add(declaration.Name);
        }

        foreach (StructDeclaration declaration in model.Structs)
        {
            _known.Add(declaration.Name);
        }

        foreach (HandleDeclaration declaration in model.Handles)
        {
            _known.Add(declaration.Name);
        }

        foreach (CallbackDeclaration declaration in model.Callbacks)
        {
            _known.Add(declaration.Name);
        }

        // The expanded form of each callback, for the places an alias cannot be used. Built in a second
        // pass because a callback's own signature names types declared above.
        foreach (CallbackDeclaration declaration in model.Callbacks)
        {
            List<string> signature = [];
            foreach (ParameterDeclaration parameter in declaration.Parameters)
            {
                signature.Add(Map(parameter.CType));
            }

            signature.Add(Map(declaration.ReturnCType));
            _callbacks[declaration.Name] = $"delegate* unmanaged[Cdecl]<{string.Join(", ", signature)}>";
        }
    }

    /// <summary>Maps one C type, writing a callback out in full rather than by its alias.</summary>
    /// <param name="cType">The type, as the header spells it.</param>
    /// <returns>The C# type.</returns>
    /// <remarks>
    /// The interop source generator resolves nothing through a using alias: a signature naming one is
    /// skipped outright, and what is left is a partial method with no implementation. So the entry points
    /// spell their callbacks out, and only the structures use the shorter name.
    /// </remarks>
    public string MapExpanded(string cType)
    {
        string mapped = Map(cType);
        return _callbacks.TryGetValue(mapped, out string? expanded) ? expanded : mapped;
    }

    /// <summary>Maps one C type.</summary>
    /// <param name="cType">The type, as the header spells it.</param>
    /// <returns>The C# type.</returns>
    /// <exception cref="WebGpuBindException">The type is one this does not know.</exception>
    public string Map(string cType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cType);

        string text = WhitespacePattern().Replace(cType.Trim(), " ");

        int pointers = 0;
        while (text.EndsWith('*'))
        {
            pointers++;
            text = text[..^1].Trim();
        }

        // Const says nothing a C# pointer can carry, and 'struct' is C's way of naming a type that is
        // declared later in the same header.
        text = text
            .Replace("const", string.Empty, StringComparison.Ordinal)
            .Replace("struct ", string.Empty, StringComparison.Ordinal)
            .Trim();

        // An array parameter is a pointer, whatever the header calls it.
        if (text.EndsWith("[]", StringComparison.Ordinal))
        {
            text = text[..^2].Trim();
            pointers++;
        }

        string mapped = Scalars.TryGetValue(text, out string? scalar)
            ? scalar
            : _known.Contains(text)
                ? text
                : throw new WebGpuBindException($"The type '{cType}' is one this does not know.");

        return mapped + new string('*', pointers);
    }

    /// <summary>Whether a mapped type is a pointer, which decides whether a struct needs unsafe.</summary>
    /// <param name="csharpType">A type this produced.</param>
    /// <returns><see langword="true"/> when it is a pointer.</returns>
    public static bool IsPointer(string csharpType) =>
        csharpType is not null && csharpType.EndsWith('*');

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
