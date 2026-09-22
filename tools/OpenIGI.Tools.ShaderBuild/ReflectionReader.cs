using System.Globalization;
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
        ShaderUniformBlock? globals = null;

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
                        JsonElement type = parameter.GetProperty("type");
                        bindings.Add(new ShaderBinding(
                            name,
                            ReadSpace(binding),
                            ReadIndex(binding),
                            ReadBindingKind(module, name, type),
                            ReadDimensions(type),
                            ReadStorageFormat(module, name, parameter),
                            StorageAccess: ReadStorageAccess(type),
                            Arrayed: ReadArrayed(type)));
                        globals ??= ReadGlobals(name, binding, type);
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

        return new ShaderReflection(
            module, variant, entryPoints, bindings, pushBlock, constants, globals);
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

    /// <summary>What an unbounded resource array holds.</summary>
    /// <param name="module">The module, for the diagnostic.</param>
    /// <param name="name">The parameter's name, for the diagnostic.</param>
    /// <param name="type">The reflected array type.</param>
    /// <returns>The kind the array is of.</returns>
    /// <exception cref="ShaderBuildException">The array holds something the library has no kind for.</exception>
    private static ShaderBindingKind ReadArrayKind(ShaderModuleId module, string name, JsonElement type)
    {
        if (!type.TryGetProperty("elementType", out JsonElement element))
        {
            throw new ShaderBuildException($"{module}: the array '{name}' names no element type.");
        }

        ShaderBindingKind kind = ReadBindingKind(module, name, element);
        if (kind == ShaderBindingKind.AccelerationStructure)
        {
            return ShaderBindingKind.AccelerationStructureArray;
        }

        throw new ShaderBuildException(
            $"{module}: '{name}' is an unbounded array of {kind}, which the library has no binding "
            + "kind for. Only the acceleration structures are bound through a bindless heap.");
    }

    /// <summary>What one texel of a storage texture holds, as the source declared it.</summary>
    /// <param name="module">The module, for the diagnostic.</param>
    /// <param name="name">The parameter's name, for the diagnostic.</param>
    /// <param name="parameter">The reflected parameter.</param>
    /// <returns>The format, or <see cref="ShaderStorageFormat.None"/> for anything that is not one.</returns>
    /// <exception cref="ShaderBuildException">The format is one this does not know.</exception>
    /// <remarks>
    /// Slang reports it as the name the <c>[[vk::image_format]]</c> attribute used. Refused rather than
    /// defaulted when unrecognised: a storage format is what a WebGPU bind group layout is validated
    /// against, and a wrong one binds nothing while reporting neither the module nor the texture.
    /// </remarks>
    private static ShaderStorageFormat ReadStorageFormat(ShaderModuleId module, string name, JsonElement parameter)
    {
        if (!parameter.TryGetProperty("format", out JsonElement format))
        {
            return ShaderStorageFormat.None;
        }

        string text = format.GetString() ?? string.Empty;
        return ParseStorageFormat(text)
            ?? throw new ShaderBuildException(
                $"{module}: '{name}' declares the storage format '{text}', which the build does not know. "
                + "Add it to ShaderStorageFormat, or the backends will bind nothing to it.");
    }

    /// <summary>How a storage binding may be reached, as the module's own type says.</summary>
    /// <param name="type">The parameter's type, as Slang reports it.</param>
    /// <returns>The access, or none where the binding is not a storage one.</returns>
    /// <remarks>
    /// Slang reports "write" for a write-only texture and "readWrite" for one that is both. Anything
    /// else is not a storage binding at all, and says so rather than guessing at read-write — a layout
    /// that overstates the access binds nothing.
    /// </remarks>
    private static ShaderStorageAccess ReadStorageAccess(JsonElement type)
    {
        if (!type.TryGetProperty("access", out JsonElement access))
        {
            return ShaderStorageAccess.None;
        }

        return access.GetString() switch
        {
            "write" => ShaderStorageAccess.WriteOnly,
            "readWrite" => ShaderStorageAccess.ReadWrite,
            _ => ShaderStorageAccess.None,
        };
    }

    /// <summary>One storage format's name, in either the Slang spelling or the WGSL one.</summary>
    /// <param name="text">The name.</param>
    /// <returns>The format, or null when it is not one this knows.</returns>
    /// <remarks>
    /// Both spellings, because the same value is read out of two places: Slang's reflection writes
    /// <c>rg16f</c> and the emitted WGSL writes <c>rg16float</c>.
    /// </remarks>
    public static ShaderStorageFormat? ParseStorageFormat(string text) => text switch
    {
        "r8" or "r8unorm" => ShaderStorageFormat.R8Unorm,
        "rg8" or "rg8unorm" => ShaderStorageFormat.Rg8Unorm,
        "rgba8" or "rgba8unorm" => ShaderStorageFormat.Rgba8Unorm,
        "r16f" or "r16float" => ShaderStorageFormat.R16Float,
        "rg16f" or "rg16float" => ShaderStorageFormat.Rg16Float,
        "rgba16f" or "rgba16float" => ShaderStorageFormat.Rgba16Float,
        "r32f" or "r32float" => ShaderStorageFormat.R32Float,
        "r32ui" or "r32uint" => ShaderStorageFormat.R32Uint,
        "rg32f" or "rg32float" => ShaderStorageFormat.Rg32Float,
        "rgba32f" or "rgba32float" => ShaderStorageFormat.Rgba32Float,
        "rgb10_a2" or "rgb10a2unorm" => ShaderStorageFormat.Rgb10A2Unorm,
        "r11f_g11f_b10f" or "rg11b10float" or "rg11b10ufloat" => ShaderStorageFormat.Rg11B10Ufloat,
        _ => null,
    };

    /// <summary>How many dimensions a texture resource has, or nought for anything else.</summary>
    /// <param name="type">The reflected type.</param>
    /// <returns>1, 2 or 3 for a texture; 0 otherwise.</returns>
    private static int ReadDimensions(JsonElement type)
    {
        string shape = type.TryGetProperty("baseShape", out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;

        return shape switch
        {
            "texture1D" => 1,
            "texture2D" => 2,
            "texture3D" => 3,
            _ => 0,
        };
    }

    /// <summary>Whether a texture resource is an array of slices rather than one image.</summary>
    /// <param name="type">The reflected type.</param>
    /// <returns><see langword="true"/> for an arrayed texture.</returns>
    /// <remarks>
    /// Slang says so plainly, and it has to be carried: a bind group layout states the view dimension
    /// and binds nothing when it is wrong, while a descriptor set says nothing about it at all — so
    /// this is a difference only the browser would ever have found.
    /// </remarks>
    private static bool ReadArrayed(JsonElement type) =>
        type.TryGetProperty("array", out JsonElement array)
        && array.ValueKind == JsonValueKind.True;

    /// <summary>The field table of a block the module named <c>Globals</c>, or nothing.</summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="binding">Its descriptor slot.</param>
    /// <param name="type">Its reflected type.</param>
    /// <returns>The block, or <see langword="null"/> where this is not one.</returns>
    /// <remarks>
    /// Only that one name, because the table exists for hosts that set their parameters one at a time by
    /// name and every other constant buffer is filled as a struct. Emitting a field table for all of them
    /// would be a second description of a layout nothing reads.
    /// </remarks>
    private static ShaderUniformBlock? ReadGlobals(string name, JsonElement binding, JsonElement type)
    {
        if (!type.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "constantBuffer")
        {
            return null;
        }

        if (!type.TryGetProperty("elementType", out JsonElement element))
        {
            return null;
        }

        // Either spelling counts. A struct handed to ConstantBuffer<> reports the struct's name and the
        // parameter's is whatever the variable was called; a cbuffer block reports the block's name as
        // the parameter and leaves the element type anonymous. The modules use the second, because its
        // members are in scope unqualified and the shared includes name them directly.
        bool named = name == "Globals"
            || (element.TryGetProperty("name", out JsonElement structName)
                && structName.GetString() == "Globals");

        if (!named)
        {
            return null;
        }

        List<ShaderUniformField> fields = [];
        int size = 0;
        if (element.TryGetProperty("fields", out JsonElement declared))
        {
            foreach (JsonElement field in declared.EnumerateArray())
            {
                JsonElement placement = field.GetProperty("binding");
                int offset = placement.TryGetProperty("offset", out JsonElement at) ? at.GetInt32() : 0;
                int length = placement.TryGetProperty("size", out JsonElement bytes) ? bytes.GetInt32() : 0;
                string fieldName = field.GetProperty("name").GetString() ?? string.Empty;
                size = Math.Max(size, offset + length);

                // An ARRAY is listed per element, under the indexed name the host sets it by. The seam
                // sets one parameter per call and has no way to hand over a whole array, so every host
                // that fills one writes "uThing[0]", "uThing[1]" and so on; a single entry for the
                // whole field would match none of them and the array would stay at whatever the buffer
                // held.
                JsonElement fieldType = field.GetProperty("type");
                bool isArray = fieldType.TryGetProperty("kind", out JsonElement fieldKind)
                    && fieldKind.GetString() == "array";
                int stride = placement.TryGetProperty("elementStride", out JsonElement step)
                    ? step.GetInt32()
                    : 0;

                if (isArray && stride > 0)
                {
                    for (int index = 0; index * stride < length; index++)
                    {
                        fields.Add(new ShaderUniformField(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{fieldName}[{index}]"),
                            offset + (index * stride),
                            stride));
                    }

                    continue;
                }

                fields.Add(new ShaderUniformField(fieldName, offset, length));
            }
        }

        // Rounded to a vector, which is what the layout rules already did to every field but the last.
        size = (size + 15) & ~15;
        return new ShaderUniformBlock(name, ReadSpace(binding), ReadIndex(binding), size, fields);
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

            case "array":
                // A bindless heap: an unbounded array of one resource kind, indexed in the shader.
                // Only the acceleration structures are bound that way, and the backend that binds them
                // needs to know which binding the heap occupies rather than how long it is.
                return ReadArrayKind(module, name, type);

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
