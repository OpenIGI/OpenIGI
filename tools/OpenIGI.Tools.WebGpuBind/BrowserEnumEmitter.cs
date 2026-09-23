using System.Globalization;
using System.Text;

namespace OpenIGI.Tools.WebGpuBind;

/// <summary>
/// Writes the browser bridge's enum tables: every header value the bridge passes on, and the string the
/// JavaScript WebGPU API names it by.
/// </summary>
/// <remarks>
/// <para>
/// The C API numbers its enumerations; the JavaScript API spells them (<c>WGPUCompareFunction_LessEqual</c>
/// is <c>'less-equal'</c>). The browser build implements the C API over the JavaScript one, so every value
/// crosses that line, and a hand-kept table is a table that drifts from the header it mirrors.
/// </para>
/// <para>
/// So the header supplies the values and this supplies only the spellings, straight from the WebGPU
/// specification's IDL, and the two are matched by name with the case, the dashes and the underscores
/// ignored -- <c>RGBA8UnormSrgb</c> is <c>rgba8unorm-srgb</c>, <c>2DArray</c> is <c>2d-array</c>. A header
/// value no IDL string matches is left out, which the bridge reads as "not given" -- the same as the
/// header's own <c>Undefined</c>.
/// </para>
/// </remarks>
public static class BrowserEnumEmitter
{
    /// <summary>The file it writes, relative to the browser bridge's directory.</summary>
    public const string FileName = "wgpu-enums.g.js";

    // The WebGPU IDL's own strings, by the header enumeration they spell.
    private static readonly Dictionary<string, string[]> Idl = new(StringComparer.Ordinal)
    {
        ["WGPUTextureFormat"] =
        [
            "r8unorm", "r8snorm", "r8uint", "r8sint", "r16unorm", "r16snorm", "r16uint", "r16sint", "r16float",
            "rg8unorm", "rg8snorm", "rg8uint", "rg8sint", "r32uint", "r32sint", "r32float",
            "rg16unorm", "rg16snorm", "rg16uint", "rg16sint", "rg16float",
            "rgba8unorm", "rgba8unorm-srgb", "rgba8snorm", "rgba8uint", "rgba8sint", "bgra8unorm", "bgra8unorm-srgb",
            "rgb9e5ufloat", "rgb10a2uint", "rgb10a2unorm", "rg11b10ufloat",
            "rg32uint", "rg32sint", "rg32float", "rgba16unorm", "rgba16snorm", "rgba16uint", "rgba16sint", "rgba16float",
            "rgba32uint", "rgba32sint", "rgba32float",
            "stencil8", "depth16unorm", "depth24plus", "depth24plus-stencil8", "depth32float", "depth32float-stencil8",
            "bc1-rgba-unorm", "bc1-rgba-unorm-srgb", "bc2-rgba-unorm", "bc2-rgba-unorm-srgb", "bc3-rgba-unorm",
            "bc3-rgba-unorm-srgb", "bc4-r-unorm", "bc4-r-snorm", "bc5-rg-unorm", "bc5-rg-snorm", "bc6h-rgb-ufloat",
            "bc6h-rgb-float", "bc7-rgba-unorm", "bc7-rgba-unorm-srgb",
        ],
        ["WGPUTextureDimension"] = ["1d", "2d", "3d"],
        ["WGPUTextureViewDimension"] = ["1d", "2d", "2d-array", "cube", "cube-array", "3d"],
        ["WGPUTextureAspect"] = ["all", "stencil-only", "depth-only"],
        ["WGPUAddressMode"] = ["clamp-to-edge", "repeat", "mirror-repeat"],
        ["WGPUFilterMode"] = ["nearest", "linear"],
        ["WGPUMipmapFilterMode"] = ["nearest", "linear"],
        ["WGPUCompareFunction"] = ["never", "less", "equal", "less-equal", "greater", "not-equal", "greater-equal", "always"],
        ["WGPUBufferBindingType"] = ["uniform", "storage", "read-only-storage"],
        ["WGPUSamplerBindingType"] = ["filtering", "non-filtering", "comparison"],
        ["WGPUTextureSampleType"] = ["float", "unfilterable-float", "depth", "sint", "uint"],
        ["WGPUStorageTextureAccess"] = ["write-only", "read-only", "read-write"],
        ["WGPUVertexStepMode"] = ["vertex", "instance"],
        ["WGPUVertexFormat"] =
        [
            "uint8", "uint8x2", "uint8x4", "sint8", "sint8x2", "sint8x4", "unorm8", "unorm8x2", "unorm8x4",
            "snorm8", "snorm8x2", "snorm8x4", "uint16", "uint16x2", "uint16x4", "sint16", "sint16x2", "sint16x4",
            "unorm16", "unorm16x2", "unorm16x4", "snorm16", "snorm16x2", "snorm16x4", "float16", "float16x2",
            "float16x4", "float32", "float32x2", "float32x3", "float32x4", "uint32", "uint32x2", "uint32x3",
            "uint32x4", "sint32", "sint32x2", "sint32x3", "sint32x4", "unorm10-10-10-2", "unorm8x4-bgra",
        ],
        ["WGPUPrimitiveTopology"] = ["point-list", "line-list", "line-strip", "triangle-list", "triangle-strip"],
        ["WGPUIndexFormat"] = ["uint16", "uint32"],
        ["WGPUFrontFace"] = ["ccw", "cw"],
        ["WGPUCullMode"] = ["none", "front", "back"],
        ["WGPUBlendOperation"] = ["add", "subtract", "reverse-subtract", "min", "max"],
        ["WGPUBlendFactor"] =
        [
            "zero", "one", "src", "one-minus-src", "src-alpha", "one-minus-src-alpha", "dst", "one-minus-dst",
            "dst-alpha", "one-minus-dst-alpha", "src-alpha-saturated", "constant", "one-minus-constant",
            "src1", "one-minus-src1", "src1-alpha", "one-minus-src1-alpha",
        ],
        ["WGPUStencilOperation"] =
        [
            "keep", "zero", "replace", "invert", "increment-clamp", "decrement-clamp", "increment-wrap", "decrement-wrap",
        ],
        ["WGPULoadOp"] = ["load", "clear"],
        ["WGPUStoreOp"] = ["store", "discard"],
        ["WGPUQueryType"] = ["occlusion", "timestamp"],
        ["WGPUFeatureName"] =
        [
            "core-features-and-limits", "depth-clip-control", "depth32float-stencil8", "texture-compression-bc",
            "texture-compression-bc-sliced-3d", "texture-compression-etc2", "texture-compression-astc",
            "texture-compression-astc-sliced-3d", "timestamp-query", "indirect-first-instance", "shader-f16",
            "rg11b10ufloat-renderable", "bgra8unorm-storage", "float32-filterable", "float32-blendable",
            "clip-distances", "dual-source-blending", "subgroups", "texture-formats-tier1", "texture-formats-tier2",
            "primitive-index",
        ],
        ["WGPUCompositeAlphaMode"] = ["opaque", "premultiplied"],
        ["WGPUPowerPreference"] = ["low-power", "high-performance"],
    };

    /// <summary>Writes the tables.</summary>
    /// <param name="model">The parsed headers.</param>
    /// <param name="version">The wgpu-native version the headers came from.</param>
    /// <returns>The module's text.</returns>
    /// <exception cref="WebGpuBindException">An enumeration the tables spell is missing from the headers.</exception>
    public static string Emit(HeaderModel model, string version)
    {
        ArgumentNullException.ThrowIfNull(model);

        StringBuilder text = new();
        text.Append("// <auto-generated>\n");
        text.Append("//\n");
        text.Append("// The webgpu.h enumerations the browser bridge passes on, value to JavaScript string and back.\n");
        text.Append("//\n");
        text.Append(CultureInfo.InvariantCulture, $"// Produced by tools/OpenIGI.Tools.WebGpuBind from the headers of wgpu-native {version}.\n");
        text.Append("// Do not edit: regenerate instead, and commit what comes out.\n");
        text.Append("// </auto-generated>\n\n");
        text.Append("export const E = {\n");

        foreach ((string name, string[] spellings) in Idl)
        {
            EnumDeclaration declaration = model.Enums.FirstOrDefault(e => e.Name == name)
                ?? throw new WebGpuBindException($"The browser tables spell {name}, which the headers do not declare.");

            Dictionary<string, string> byKey = spellings.ToDictionary(Key, s => s, StringComparer.Ordinal);
            text.Append(CultureInfo.InvariantCulture, $"  {name[4..]}: {{");

            bool first = true;
            foreach (EnumValue value in declaration.Values)
            {
                if (!byKey.TryGetValue(Key(value.Name), out string? spelling))
                {
                    continue;
                }

                text.Append(first ? " " : ", ");
                text.Append(CultureInfo.InvariantCulture, $"{Parse(value.Value)}: '{spelling}'");
                first = false;
            }

            text.Append(" },\n");
        }

        text.Append("};\n\n");
        text.Append("/** The same tables the other way: a JavaScript string to its header value. */\n");
        text.Append("export const V = Object.fromEntries(Object.entries(E).map(([name, table]) =>\n");
        text.Append("  [name, Object.fromEntries(Object.entries(table).map(([value, spelling]) => [spelling, Number(value)]))]));\n");
        return text.ToString();
    }

    private static string Key(string name) =>
        name.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static long Parse(string value)
    {
        string trimmed = value.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(trimmed, CultureInfo.InvariantCulture);
    }
}
