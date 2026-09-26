using System.Text.RegularExpressions;

namespace OpenIGI.Tools.WebGpuBind;

/// <summary>
/// Reads what a WebGPU header declares.
/// </summary>
/// <remarks>
/// <para>
/// Not a C parser, and it does not need to be. These headers are generated from a machine-readable
/// description of the API, so every declaration is in one of six shapes and each is written the same
/// way every time. What this does is recognise those six and refuse anything it does not recognise,
/// which is the property that matters: a header shape that changes between releases fails the run
/// rather than quietly dropping a function from the bindings.
/// </para>
/// <para>
/// Declarations are matched against a copy of the text whose comments are blanked to spaces. The API's
/// own documentation contains braces -- a WGSL snippet, a GLSL layout -- and a brace inside a comment
/// would otherwise end the declaration it sits in, which reads as an enumeration that came out empty.
/// Blanking rather than removing keeps every index the same, so the documentation still reads out of
/// the original text.
/// </para>
/// </remarks>
public static partial class HeaderParser
{
    /// <summary>Reads one header.</summary>
    /// <param name="source">The header's text.</param>
    /// <returns>What it declares.</returns>
    /// <exception cref="WebGpuBindException">A declaration was in a shape this does not know.</exception>
    public static HeaderModel Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string text = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        string code = Blank(text);

        List<EnumDeclaration> enums = [];
        List<StructDeclaration> structs = [];
        List<HandleDeclaration> handles = [];
        List<CallbackDeclaration> callbacks = [];
        List<FunctionDeclaration> functions = [];

        foreach (Match match in EnumPattern().Matches(code))
        {
            enums.Add(ReadEnum(text, match));
        }

        foreach (Match match in FlagsPattern().Matches(code))
        {
            EnumDeclaration? flags = ReadFlags(code, match.Groups["name"].Value);
            if (flags is not null)
            {
                enums.Add(flags);
            }
        }

        foreach ((string name, string body, int index) in ScanStructs(code))
        {
            structs.AddRange(ReadStruct(text, name, body, index));
        }

        foreach (Match match in HandlePattern().Matches(code))
        {
            handles.Add(new HandleDeclaration(match.Groups["name"].Value, DocumentationBefore(text, match.Index)));
        }

        foreach (Match match in CallbackPattern().Matches(code))
        {
            string name = match.Groups["name"].Value;

            // The proc-pointer typedefs are one per function and exist so a caller can resolve by name
            // rather than link. Nothing here resolves by name, so they would be 200 unused signatures.
            if (name.StartsWith("WGPUProc", StringComparison.Ordinal))
            {
                continue;
            }

            callbacks.Add(new CallbackDeclaration(
                name,
                Clean(match.Groups["ret"].Value),
                ReadParameters(match.Groups["args"].Value),
                DocumentationBefore(text, match.Index)));
        }

        foreach (Match match in ExportedFunctionPattern().Matches(code))
        {
            functions.Add(new FunctionDeclaration(
                match.Groups["name"].Value,
                Clean(match.Groups["ret"].Value),
                ReadParameters(match.Groups["args"].Value),
                DocumentationBefore(text, match.Index)));
        }

        foreach (Match match in ExternFunctionPattern().Matches(code))
        {
            string name = match.Groups["name"].Value;
            if (functions.Exists(f => string.Equals(f.Name, name, StringComparison.Ordinal)))
            {
                continue;
            }

            functions.Add(new FunctionDeclaration(
                name,
                Clean(match.Groups["ret"].Value),
                ReadParameters(match.Groups["args"].Value),
                DocumentationBefore(text, match.Index)));
        }

        return new HeaderModel(enums, structs, handles, callbacks, functions);
    }

    /// <summary>Every structure in the text, found by balancing braces rather than by pattern.</summary>
    /// <remarks>
    /// One structure in this API holds an anonymous union, and a pattern that stops at the first closing
    /// brace stops inside it — which makes the whole structure invisible and every use of it an unknown
    /// type. Balancing is the only way to read a body that can nest.
    /// </remarks>
    private static List<(string Name, string Body, int Index)> ScanStructs(string code)
    {
        List<(string Name, string Body, int Index)> found = [];

        foreach (Match match in StructStartPattern().Matches(code))
        {
            int open = code.IndexOf('{', match.Index);
            int depth = 0;
            int close = -1;

            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{')
                {
                    depth++;
                }
                else if (code[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        close = i;
                        break;
                    }
                }
            }

            if (close < 0)
            {
                throw new WebGpuBindException($"{match.Groups["name"].Value}: its body is not closed.");
            }

            found.Add((match.Groups["name"].Value, code[(open + 1)..close], match.Index));
        }

        return found;
    }

    private static EnumDeclaration ReadEnum(string text, Match match)
    {
        string name = match.Groups["name"].Value;
        List<EnumValue> values = [];

        foreach (string line in match.Groups["body"].Value.Split('\n'))
        {
            Match value = EnumValuePattern().Match(line);
            if (!value.Success)
            {
                continue;
            }

            string valueName = value.Groups["name"].Value;

            // The sentinel that forces the C enumeration to 32 bits. C# states its width outright, so
            // carrying it across would only be a member nothing may ever be.
            if (valueName.EndsWith("_Force32", StringComparison.Ordinal))
            {
                continue;
            }

            values.Add(new EnumValue(Shorten(valueName, name), value.Groups["value"].Value.Trim(), []));
        }

        if (values.Count == 0)
        {
            throw new WebGpuBindException($"The enumeration {name} came out with no values.");
        }

        return new EnumDeclaration(name, values, IsFlags: false, "int", DocumentationBefore(text, match.Index));
    }

    private static EnumDeclaration? ReadFlags(string code, string name)
    {
        List<EnumValue> values = [];
        foreach (Match constant in FlagsValuePattern(name).Matches(code))
        {
            string valueName = constant.Groups["name"].Value;

            // As in an enumeration: the sentinel that forces the C type to 32 bits says nothing in C#,
            // and in a flags set it would be a member that turns on every bit at once.
            if (valueName.EndsWith("_Force32", StringComparison.Ordinal))
            {
                continue;
            }

            values.Add(new EnumValue(
                Shorten(valueName, name),
                constant.Groups["value"].Value.Trim(),
                []));
        }

        // WGPUFlags itself is the storage typedef, not a set of flags, and has no constants.
        return values.Count == 0 ? null : new EnumDeclaration(name, values, IsFlags: true, "ulong", []);
    }

    /// <summary>Reads one structure, and any union inside it as a structure of its own.</summary>
    private static List<StructDeclaration> ReadStruct(string text, string name, string body, int index)
    {
        List<StructDeclaration> produced = [];
        List<FieldDeclaration> fields = [];

        string remaining = body;
        while (true)
        {
            int union = remaining.IndexOf("union", StringComparison.Ordinal);
            if (union < 0)
            {
                break;
            }

            int open = remaining.IndexOf('{', union);
            int close = remaining.IndexOf('}', open);
            int end = close < 0 ? -1 : remaining.IndexOf(';', close);
            if (open < 0 || close < 0 || end < 0)
            {
                throw new WebGpuBindException($"{name}: its union is not closed.");
            }

            string fieldName = remaining[(close + 1)..end].Trim();
            string unionName = name + char.ToUpperInvariant(fieldName[0]) + fieldName[1..];

            // The union becomes a structure of its own, laid out with every member at offset zero. C# has
            // no anonymous unions, and a named one is clearer in a binding anyway.
            produced.Add(new StructDeclaration(
                unionName,
                ReadFields(unionName, remaining[(open + 1)..close]),
                [$"The anonymous union <c>{fieldName}</c> of <c>{name}</c>, every member at offset zero."])
            {
                IsUnion = true,
            });

            fields.AddRange(ReadFields(name, remaining[..union]));
            fields.Add(new FieldDeclaration(fieldName, unionName, []));
            remaining = remaining[(end + 1)..];
        }

        fields.AddRange(ReadFields(name, remaining));
        produced.Add(new StructDeclaration(name, fields, DocumentationBefore(text, index)));
        return produced;
    }

    private static List<FieldDeclaration> ReadFields(string owner, string body)
    {
        List<FieldDeclaration> fields = [];

        foreach (string raw in body.Split(';'))
        {
            string line = Clean(raw);
            if (line.Length == 0)
            {
                continue;
            }

            Match field = FieldPattern().Match(line);
            if (!field.Success)
            {
                throw new WebGpuBindException($"{owner}: the field '{line}' is in a shape this does not know.");
            }

            fields.Add(new FieldDeclaration(field.Groups["name"].Value, field.Groups["type"].Value.Trim(), []));
        }

        return fields;
    }

    private static List<ParameterDeclaration> ReadParameters(string arguments)
    {
        string text = arguments.Trim();
        if (text.Length == 0 || string.Equals(text, "void", StringComparison.Ordinal))
        {
            return [];
        }

        List<ParameterDeclaration> parameters = [];
        foreach (string raw in text.Split(','))
        {
            string one = Clean(raw);
            Match match = FieldPattern().Match(one);
            if (!match.Success)
            {
                throw new WebGpuBindException($"The parameter '{one}' is in a shape this does not know.");
            }

            parameters.Add(new ParameterDeclaration(match.Groups["name"].Value, match.Groups["type"].Value.Trim()));
        }

        return parameters;
    }

    /// <summary>The doc comment immediately above a declaration, as plain lines.</summary>
    private static List<string> DocumentationBefore(string text, int index)
    {
        int end = text.LastIndexOf("*/", index, StringComparison.Ordinal);
        if (end < 0)
        {
            return [];
        }

        // Only a comment that is directly above, with nothing but whitespace between.
        if (text.AsSpan(end + 2, index - end - 2).ContainsAnyExcept(WhitespaceChars))
        {
            return [];
        }

        int start = text.LastIndexOf("/**", end, StringComparison.Ordinal);
        if (start < 0)
        {
            return [];
        }

        List<string> lines = [];
        foreach (string raw in text[(start + 3)..end].Split('\n'))
        {
            string line = raw.Trim().TrimStart('*').Trim();
            if (line.Length == 0 && lines.Count == 0)
            {
                continue;
            }

            // Doxygen's own structure means nothing in C# documentation, and its group markers are not
            // about the declaration at all.
            if (line.StartsWith('\\') || line.StartsWith("@{", StringComparison.Ordinal)
                || line.StartsWith("@}", StringComparison.Ordinal))
            {
                continue;
            }

            lines.Add(line
                .Replace("@ref ", string.Empty, StringComparison.Ordinal)
                .Replace("@copydoc ", string.Empty, StringComparison.Ordinal)
                .Replace("@note ", "Note: ", StringComparison.Ordinal));
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    /// <summary>Replaces every comment with spaces, keeping the text's length and its line breaks.</summary>
    private static string Blank(string text)
    {
        char[] blanked = text.ToCharArray();

        foreach (Match comment in AnyCommentPattern().Matches(text))
        {
            for (int i = comment.Index; i < comment.Index + comment.Length; i++)
            {
                if (blanked[i] != '\n')
                {
                    blanked[i] = ' ';
                }
            }
        }

        return new string(blanked);
    }

    /// <summary>Removes the attribute and nullability macros, which carry nothing into C#.</summary>
    private static string Clean(string text) =>
        AttributePattern().Replace(text, " ").Replace('\n', ' ').Trim();

    /// <summary>Takes the enumeration's own prefix off a value's name.</summary>
    private static string Shorten(string valueName, string enumName)
    {
        string prefix = enumName + "_";
        string shortened = valueName.StartsWith(prefix, StringComparison.Ordinal)
            ? valueName[prefix.Length..]
            : valueName;

        // A C# member may not begin with a digit, and several of these are resolutions and formats.
        return char.IsAsciiDigit(shortened[0]) ? "_" + shortened : shortened;
    }

    private static ReadOnlySpan<char> WhitespaceChars => [' ', '\t', '\n', '\r'];

    [GeneratedRegex(@"typedef enum (?<name>WGPU\w+)\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline)]
    private static partial Regex EnumPattern();

    [GeneratedRegex(@"^\s*(?<name>WGPU\w+) = (?<value>[^,\n]+),?\s*$")]
    private static partial Regex EnumValuePattern();

    [GeneratedRegex(@"^typedef WGPUFlags (?<name>WGPU\w+);", RegexOptions.Multiline)]
    private static partial Regex FlagsPattern();

    [GeneratedRegex(@"typedef struct (?<name>WGPU\w+)\s*\{")]
    private static partial Regex StructStartPattern();

    [GeneratedRegex(@"typedef struct WGPU\w+Impl\* (?<name>WGPU\w+) WGPU_OBJECT_ATTRIBUTE;")]
    private static partial Regex HandlePattern();

    [GeneratedRegex(@"typedef (?<ret>[\w \*]+?) \(\*(?<name>WGPU\w+)\)\((?<args>[^)]*)\)( WGPU_FUNCTION_ATTRIBUTE)?;")]
    private static partial Regex CallbackPattern();

    [GeneratedRegex(
        @"WGPU_EXPORT (?<ret>[\w \*]+?) (?<name>wgpu\w+)\((?<args>[^;]*?)\) WGPU_FUNCTION_ATTRIBUTE;",
        RegexOptions.Singleline)]
    private static partial Regex ExportedFunctionPattern();

    [GeneratedRegex(@"^ {4}(?<ret>[\w \*]+?) (?<name>wgpu\w+)\((?<args>[^;]*?)\);", RegexOptions.Multiline)]
    private static partial Regex ExternFunctionPattern();

    [GeneratedRegex(@"^\s*(?<type>.+?[ \*])(?<name>\w+)$", RegexOptions.Singleline)]
    private static partial Regex FieldPattern();

    [GeneratedRegex(@"\bWGPU_(NULLABLE|OBJECT_ATTRIBUTE|ENUM_ATTRIBUTE|STRUCTURE_ATTRIBUTE|FUNCTION_ATTRIBUTE)\b")]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline)]
    private static partial Regex AnyCommentPattern();

    private static Regex FlagsValuePattern(string name) => new(
        $@"static const {Regex.Escape(name)} (?<name>{Regex.Escape(name)}_\w+) = (?<value>[^;]+);",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));
}
