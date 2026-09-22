using System.Globalization;
using System.Text;

namespace OpenIGI.Tools.WebGpuBind;

/// <summary>One generated file.</summary>
/// <param name="FileName">Its name under <c>Generated/</c>.</param>
/// <param name="Text">Its contents.</param>
public sealed record GeneratedFile(string FileName, string Text);

/// <summary>
/// Writes the C# binding for what a header declares.
/// </summary>
/// <remarks>
/// The C names are kept. A binding layer is read next to the header it came from, and renaming
/// <c>wgpuDeviceCreateBuffer</c> to something more idiomatic only means a reader has to hold two names
/// for one function. The layer above this one is where this API becomes the renderer's.
/// </remarks>
public sealed class BindingEmitter
{
    private const string Namespace = "OpenIGI.Rendering.WebGpu.Native";

    private readonly HeaderModel _model;
    private readonly TypeMap _types;
    private readonly string _version;

    /// <summary>Initialises a new instance of the <see cref="BindingEmitter"/> class.</summary>
    /// <param name="model">What the headers declare.</param>
    /// <param name="version">The wgpu-native release they came from.</param>
    public BindingEmitter(HeaderModel model, string version)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        _model = model;
        _types = new TypeMap(model);
        _version = version;
    }

    /// <summary>Writes every file.</summary>
    /// <returns>The files, by name.</returns>
    public IReadOnlyList<GeneratedFile> Emit() =>
    [
        new("WebGpuEnums.g.cs", EmitEnums()),
        new("WebGpuHandles.g.cs", EmitHandles()),
        new("WebGpuStructs.g.cs", EmitStructs()),
        new("WebGpuCallbacks.g.cs", EmitCallbacks()),
        new("WebGpu.g.cs", EmitFunctions()),
    ];

    private string EmitEnums()
    {
        StringBuilder text = new();
        Header(text, "The enumerations and flag sets.");

        foreach (EnumDeclaration declaration in _model.Enums.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            Documentation(text, declaration.Documentation, $"The C enumeration <c>{declaration.Name}</c>.");

            if (declaration.IsFlags)
            {
                text.AppendLine("[Flags]");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"public enum {declaration.Name} : {declaration.UnderlyingType}");
            text.AppendLine("{");

            bool first = true;
            foreach (EnumValue value in declaration.Values)
            {
                if (!first)
                {
                    text.AppendLine();
                }

                first = false;
                text.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>The C value <c>{declaration.Name}_{value.Name.TrimStart('_')}</c>.</summary>");
                text.AppendLine(CultureInfo.InvariantCulture, $"    {value.Name} = {Literal(value.Value, declaration.UnderlyingType)},");
            }

            text.AppendLine("}");
            text.AppendLine();
        }

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    private string EmitHandles()
    {
        StringBuilder text = new();
        Header(
            text,
            "The opaque object handles.",
            "Each is one machine word, and each is its own type: the C API takes them as distinct pointer",
            "types, and collapsing them to one would let a texture be passed where a buffer was meant and",
            "the mistake reach the driver rather than the compiler.");

        foreach (HandleDeclaration declaration in _model.Handles.OrderBy(h => h.Name, StringComparer.Ordinal))
        {
            Documentation(text, declaration.Documentation, $"The C object handle <c>{declaration.Name}</c>.");
            text.AppendLine(CultureInfo.InvariantCulture, $"public readonly record struct {declaration.Name}(nint Handle)");
            text.AppendLine("{");
            text.AppendLine("    /// <summary>Gets the null handle, which every API returning one uses for failure.</summary>");
            text.AppendLine(CultureInfo.InvariantCulture, $"    public static {declaration.Name} Null => default;");
            text.AppendLine();
            text.AppendLine("    /// <summary>Gets a value indicating whether this is not the null handle.</summary>");
            text.AppendLine("    public bool IsValid => Handle != 0;");
            text.AppendLine("}");
            text.AppendLine();
        }

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    private string EmitStructs()
    {
        StringBuilder text = new();
        Header(
            text,
            "The structures, laid out exactly as the C header declares them.",
            "Sequential and blittable throughout, so a descriptor is filled in place and its address passed.",
            "Nothing here is marshalled.");

        foreach (StructDeclaration declaration in _model.Structs.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            List<(string Type, string Name)> fields = [];
            foreach (FieldDeclaration field in declaration.Fields)
            {
                fields.Add((_types.Map(field.CType), field.Name));
            }

            bool unsafeStruct = fields.Exists(f => TypeMap.IsPointer(f.Type));

            Documentation(text, declaration.Documentation, $"The C structure <c>{declaration.Name}</c>.");
            text.AppendLine("[StructLayout(LayoutKind.Sequential)]");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"public {(unsafeStruct ? "unsafe " : string.Empty)}struct {declaration.Name}");
            text.AppendLine("{");

            bool first = true;
            foreach ((string type, string name) in fields)
            {
                if (!first)
                {
                    text.AppendLine();
                }

                first = false;
                text.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>The C field <c>{name}</c>.</summary>");
                text.AppendLine(CultureInfo.InvariantCulture, $"    public {type} {Identifier(name)};");
            }

            if (fields.Count == 0)
            {
                text.AppendLine("    // The C structure has no fields.");
            }

            text.AppendLine("}");
            text.AppendLine();
        }

        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    private string EmitCallbacks()
    {
        StringBuilder text = new();

        text.AppendLine("// <auto-generated>");
        text.AppendLine("//");
        text.AppendLine("// The callback signatures, as global aliases for unmanaged function pointers.");
        text.AppendLine("//");
        text.AppendLine("// Function pointers rather than delegates: a delegate has to be kept alive by the managed side for");
        text.AppendLine("// as long as the native side might call it, and a missed root is a crash that arrives minutes later");
        text.AppendLine("// in unrelated code. A static method's address needs no rooting at all.");
        text.AppendLine("//");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"// Produced by tools/OpenIGI.Tools.WebGpuBind from the headers in native/, wgpu-native {_version}.");
        text.AppendLine("// Do not edit: regenerate instead, and commit what comes out.");
        text.AppendLine("// </auto-generated>");
        text.AppendLine();

        foreach (CallbackDeclaration declaration in _model.Callbacks.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            List<string> signature = [];
            foreach (ParameterDeclaration parameter in declaration.Parameters)
            {
                signature.Add(_types.Map(parameter.CType));
            }

            signature.Add(_types.Map(declaration.ReturnCType));

            text.AppendLine(CultureInfo.InvariantCulture,
                $"global using unsafe {declaration.Name} = delegate* unmanaged[Cdecl]<{string.Join(", ", signature.Select(Qualify))}>;");
        }

        return text.ToString();
    }

    private string EmitFunctions()
    {
        StringBuilder text = new();
        Header(
            text,
            "The exported functions.",
            "LibraryImport rather than DllImport, so the marshalling stubs are generated at build time and",
            "nothing is emitted at run time -- which is what lets this publish trimmed and ahead-of-time",
            "compiled, and therefore what lets it run in a browser at all.");

        text.AppendLine("/// <summary>The wgpu-native entry points.</summary>");
        text.AppendLine("public static unsafe partial class WebGpu");
        text.AppendLine("{");
        text.AppendLine("    /// <summary>The name the native library is loaded by.</summary>");
        text.AppendLine("    /// <remarks>Resolved to a real file by <c>WgpuLibrary</c>, which is what handles the platform's own naming.</remarks>");
        text.AppendLine("    public const string LibraryName = \"wgpu_native\";");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"    /// <summary>The wgpu-native release these bindings were generated from.</summary>");
        text.AppendLine(CultureInfo.InvariantCulture, $"    public const string Version = \"{_version}\";");

        foreach (FunctionDeclaration declaration in _model.Functions.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            List<string> parameters = [];
            foreach (ParameterDeclaration parameter in declaration.Parameters)
            {
                parameters.Add($"{_types.MapExpanded(parameter.CType)} {Identifier(parameter.Name)}");
            }

            text.AppendLine();
            Documentation(text, declaration.Documentation, $"The C function <c>{declaration.Name}</c>.", "    ");
            foreach (ParameterDeclaration parameter in declaration.Parameters)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"    /// <param name=\"{Identifier(parameter.Name)}\">The C parameter <c>{parameter.Name}</c>.</param>");
            }

            if (!string.Equals(declaration.ReturnCType, "void", StringComparison.Ordinal))
            {
                text.AppendLine("    /// <returns>What the C function returns.</returns>");
            }

            text.AppendLine(CultureInfo.InvariantCulture,
                $"    [LibraryImport(LibraryName, EntryPoint = \"{declaration.Name}\")]");
            text.AppendLine("    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"    public static partial {_types.MapExpanded(declaration.ReturnCType)} {declaration.Name}({string.Join(", ", parameters)});");
        }

        text.AppendLine("}");
        return text.ToString();
    }

    /// <summary>Qualifies a type of this assembly, which a global alias cannot resolve any other way.</summary>
    private static string Qualify(string type)
    {
        int pointer = type.IndexOf('*', StringComparison.Ordinal);
        string bare = pointer < 0 ? type : type[..pointer];
        return bare.StartsWith("WGPU", StringComparison.Ordinal) ? $"{Namespace}.{type}" : type;
    }

    private static string Summary(IReadOnlyList<ParameterDeclaration> parameters) =>
        parameters.Count == 0
            ? "it takes nothing."
            : "it takes " + string.Join(", ", parameters.Select(p => $"<c>{p.Name}</c>")) + ".";

    private void Header(StringBuilder text, params string[] lines)
    {
        text.AppendLine("// <auto-generated>");
        text.AppendLine("//");
        foreach (string line in lines)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"// {line}");
        }

        text.AppendLine("//");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"// Produced by tools/OpenIGI.Tools.WebGpuBind from the headers in native/, wgpu-native {_version}.");
        text.AppendLine("// Do not edit: regenerate instead, and commit what comes out.");
        text.AppendLine("// </auto-generated>");
        text.AppendLine();
        text.AppendLine("using System.Runtime.CompilerServices;");
        text.AppendLine("using System.Runtime.InteropServices;");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"namespace {Namespace};");
        text.AppendLine();
    }

    private static void Documentation(
        StringBuilder text,
        IReadOnlyList<string> lines,
        string fallback,
        string indent = "")
    {
        List<string> body = [.. lines.Where(line => line.Length > 0).Select(Escape)];
        if (body.Count == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// <summary>{fallback}</summary>");
            return;
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// <summary>{body[0]}</summary>");
        if (body.Count > 1)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// <remarks>");
            foreach (string line in body.Skip(1))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// {line}");
            }

            text.AppendLine(CultureInfo.InvariantCulture, $"{indent}/// </remarks>");
        }
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>Escapes a C name that happens to be a C# keyword.</summary>
    private static string Identifier(string name) => name switch
    {
        "base" or "ref" or "out" or "in" or "params" or "object" or "string" or "event" or "default"
            or "fixed" or "lock" or "operator" or "checked" or "unchecked" => "@" + name,
        _ => name,
    };

    /// <summary>Writes a C constant as a C# one of the enumeration's own width.</summary>
    /// <remarks>
    /// Most are plain literals and take a suffix. A few are expressions -- a flag set naming the flags it
    /// is the union of -- and a suffix on the last term of one of those is a syntax error, so those are
    /// cast whole instead.
    /// </remarks>
    private static string Literal(string value, string underlyingType)
    {
        string text = value.Trim();
        bool wide = string.Equals(underlyingType, "ulong", StringComparison.Ordinal);

        bool plain = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? text[2..].All(char.IsAsciiHexDigit)
            : text.All(char.IsAsciiDigit);

        if (!plain)
        {
            return wide ? $"(ulong)({text})" : text;
        }

        // The header writes a flag as a 64-bit hexadecimal constant, which is wider than an int literal
        // may be even when the value itself fits.
        if (!wide && text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && text.Length > 10)
        {
            string digits = text[2..].TrimStart('0');
            text = "0x" + (digits.Length == 0 ? "0" : digits);
        }

        return wide ? text + "UL" : text;
    }
}
