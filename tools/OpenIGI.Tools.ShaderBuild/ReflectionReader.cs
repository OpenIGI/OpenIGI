using System.Text.Json;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>
/// Reduces Slang's own reflection JSON to the compact table the backends read.
/// </summary>
/// <remarks>
/// Slang reports a full type graph; a descriptor-set or bind-group layout needs only what a resource
/// is and where it sits. Reducing at build time means nothing at run time walks a type graph, and the
/// committed table is small enough to read in review.
/// </remarks>
public static class ReflectionReader
{
    /// <summary>Reads Slang's reflection for one compiled variant.</summary>
    /// <param name="module">The module being described.</param>
    /// <param name="variant">The variant being described.</param>
    /// <param name="slangJson">Slang's reflection JSON.</param>
    /// <returns>The compact reflection.</returns>
    /// <exception cref="ShaderBuildException">The reflection could not be understood.</exception>
    public static ShaderReflection Read(ShaderModuleId module, string variant, string slangJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(variant);
        ArgumentException.ThrowIfNullOrEmpty(slangJson);

        using JsonDocument document = JsonDocument.Parse(slangJson);
        JsonElement root = document.RootElement;

        List<ShaderEntryPoint> entryPoints = ReadEntryPoints(root);

        List<ShaderBinding> bindings = [];
        List<ShaderSpecializationConstant> constants = [];
        ShaderPushBlock? pushBlock = null;

        if (root.TryGetProperty("parameters", out JsonElement parameters))
        {
            foreach (JsonElement parameter in parameters.EnumerateArray())
            {
                string name = parameter.GetProperty("name").GetString()
                    ?? throw new ShaderBuildException($"{module}: a reflected parameter has no name.");
                // A subpass input carries TWO placements -- its descriptor slot and its attachment
                // index -- so Slang reports a "bindings" array where everything else reports one
                // "binding". The slot is the one a descriptor set is built from; the attachment index
                // belongs to the render pass and the host already knows it.
                JsonElement binding = parameter.TryGetProperty("binding", out JsonElement single)
                    ? single
                    : DescriptorSlotOf(module, name, parameter);
                string kind = binding.GetProperty("kind").GetString() ?? string.Empty;

                switch (kind)
                {
                    case "pushConstantBuffer":
                        pushBlock = ReadPushBlock(module, name, parameter);
                        break;

                    case "specializationConstant":
                        constants.Add(new ShaderSpecializationConstant(name, ReadIndex(binding)));
                        break;

                    case "descriptorTableSlot":
                        bindings.Add(new ShaderBinding(
                            name,
                            ReadSpace(binding),
                            ReadIndex(binding),
                            ReadBindingKind(module, name, parameter.GetProperty("type"))));
                        break;

                    case "uniform":
                        // A loose uniform outside any block: the shader library does not use them,
                        // and letting one through would produce a layout no backend could build.
                        throw new ShaderBuildException(
                            $"{module}: '{name}' is a loose uniform. Declare it inside a push block or a "
                            + "constant buffer so both targets can bind it.");

                    default:
                        throw new ShaderBuildException(
                            $"{module}: '{name}' has the unhandled binding kind '{kind}'.");
                }
            }
        }

        bindings.Sort(static (left, right) => left.Group != right.Group
            ? left.Group.CompareTo(right.Group)
            : left.Binding.CompareTo(right.Binding));
        constants.Sort(static (left, right) => left.Id.CompareTo(right.Id));

        return new ShaderReflection(module, variant, entryPoints, bindings, pushBlock, constants);
    }

    private static List<ShaderEntryPoint> ReadEntryPoints(JsonElement root)
    {
        List<ShaderEntryPoint> entryPoints = [];
        if (!root.TryGetProperty("entryPoints", out JsonElement elements))
        {
            return entryPoints;
        }

        foreach (JsonElement element in elements.EnumerateArray())
        {
            string name = element.GetProperty("name").GetString() ?? string.Empty;
            string stage = element.GetProperty("stage").GetString() ?? string.Empty;

            List<int>? workgroupSize = null;
            if (element.TryGetProperty("threadGroupSize", out JsonElement size))
            {
                workgroupSize = [.. size.EnumerateArray().Select(static value => value.GetInt32())];
            }

            entryPoints.Add(new ShaderEntryPoint(name, ParseStage(name, stage), workgroupSize));
        }

        return entryPoints;
    }

    private static ShaderStage ParseStage(string entryPoint, string stage) => stage switch
    {
        "vertex" => ShaderStage.Vertex,
        "fragment" or "pixel" => ShaderStage.Fragment,
        "compute" => ShaderStage.Compute,
        _ => throw new ShaderBuildException($"Entry point '{entryPoint}' has the unhandled stage '{stage}'."),
    };

    private static ShaderPushBlock ReadPushBlock(ShaderModuleId module, string name, JsonElement parameter)
    {
        JsonElement type = parameter.GetProperty("type");
        if (!type.TryGetProperty("elementType", out JsonElement element)
            || !element.TryGetProperty("fields", out JsonElement fields))
        {
            throw new ShaderBuildException($"{module}: the push block '{name}' has no fields.");
        }

        // The block's size is where its last field ends: Slang reports each field's uniform offset
        // and size but no total for the block itself.
        int size = 0;
        foreach (JsonElement field in fields.EnumerateArray())
        {
            if (!field.TryGetProperty("binding", out JsonElement binding))
            {
                continue;
            }

            int offset = binding.TryGetProperty("offset", out JsonElement offsetValue) ? offsetValue.GetInt32() : 0;
            int fieldSize = binding.TryGetProperty("size", out JsonElement sizeValue) ? sizeValue.GetInt32() : 0;
            size = Math.Max(size, offset + fieldSize);
        }

        if (size == 0)
        {
            throw new ShaderBuildException($"{module}: the push block '{name}' measured zero bytes.");
        }

        return new ShaderPushBlock(name, size, PushConstantGroup, 0);
    }

    /// <summary>
    /// The bind group the WGSL target places a push block in. Must match
    /// <c>OPENIGI_PUSH_CONSTANT_GROUP</c> in <c>Source/Common/PushConstants.slang</c>.
    /// </summary>
    private const int PushConstantGroup = 3;

    /// <summary>The descriptor slot out of a parameter that carries several placements.</summary>
    /// <param name="module">The module being read, for the diagnostic.</param>
    /// <param name="name">The parameter's name, for the diagnostic.</param>
    /// <param name="parameter">The reflected parameter.</param>
    /// <returns>The <c>descriptorTableSlot</c> placement.</returns>
    /// <exception cref="ShaderBuildException">The parameter has no descriptor slot.</exception>
    private static JsonElement DescriptorSlotOf(ShaderModuleId module, string name, JsonElement parameter)
    {
        if (parameter.TryGetProperty("bindings", out JsonElement placements))
        {
            foreach (JsonElement placement in placements.EnumerateArray())
            {
                if (placement.TryGetProperty("kind", out JsonElement kind)
                    && kind.GetString() == "descriptorTableSlot")
                {
                    return placement;
                }
            }
        }

        throw new ShaderBuildException($"{module}: '{name}' has no descriptor slot to bind.");
    }

    private static int ReadIndex(JsonElement binding) =>
        binding.TryGetProperty("index", out JsonElement index) ? index.GetInt32() : 0;

    private static int ReadSpace(JsonElement binding) =>
        binding.TryGetProperty("space", out JsonElement space) ? space.GetInt32() : 0;

    private static ShaderBindingKind ReadBindingKind(ShaderModuleId module, string name, JsonElement type)
    {
        string kind = type.TryGetProperty("kind", out JsonElement kindValue) ? kindValue.GetString() ?? string.Empty : string.Empty;

        switch (kind)
        {
            case "samplerState":
                return ShaderBindingKind.Sampler;

            case "constantBuffer":
                return ShaderBindingKind.UniformBuffer;

            case "resource":
                break;

            default:
                throw new ShaderBuildException($"{module}: '{name}' has the unhandled type kind '{kind}'.");
        }

        string shape = type.TryGetProperty("baseShape", out JsonElement shapeValue)
            ? shapeValue.GetString() ?? string.Empty
            : string.Empty;
        bool readWrite = type.TryGetProperty("access", out JsonElement access)
            && access.GetString() is "readWrite" or "write";

        if (shape == "structuredBuffer" || shape == "byteAddressBuffer")
        {
            return ShaderBindingKind.StorageBuffer;
        }

        if (shape == "accelerationStructure")
        {
            return ShaderBindingKind.AccelerationStructure;
        }

        if (shape == "subpassInput")
        {
            return ShaderBindingKind.InputAttachment;
        }

        if (shape.StartsWith("texture", StringComparison.Ordinal))
        {
            if (readWrite)
            {
                return ShaderBindingKind.StorageTexture;
            }

            // Slang says so outright when a declaration is one of the GLSL-style combined types, which
            // is the only way to tell: the shape and the result type are the same either way.
            bool combined = type.TryGetProperty("combined", out JsonElement value)
                && value.ValueKind == JsonValueKind.True;

            return combined ? ShaderBindingKind.CombinedImageSampler : ShaderBindingKind.Texture;
        }

        throw new ShaderBuildException($"{module}: '{name}' has the unhandled resource shape '{shape}'.");
    }
}
