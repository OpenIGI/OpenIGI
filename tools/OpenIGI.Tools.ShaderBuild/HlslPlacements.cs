using System.Text.Json;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Where one DXIL compile placed every resource: HLSL registers by name, and loose uniforms by offset.</summary>
/// <param name="Resources">Each resource's register, keyed by its declared name.</param>
/// <param name="Uniforms">Each loose uniform's byte offset in the global constant buffer, keyed by name.</param>
/// <param name="Constants">Each constant block's fields as HLSL packs them, keyed by block name; see <see cref="ConstantLayouts"/>.</param>
public sealed record HlslPlacements(
    IReadOnlyDictionary<string, ShaderHlslRegister> Resources,
    IReadOnlyDictionary<string, int> Uniforms,
    IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformField>> Constants)
{
    /// <summary>The register Slang gives its implicit global constant buffer when a module has loose uniforms.</summary>
    /// <remarks>
    /// Slang reports the uniforms' offsets but not the buffer itself. It reserves the first constant-buffer
    /// register for it, which <see cref="Merge"/> checks rather than trusts: nothing else may sit there.
    /// </remarks>
    public static readonly ShaderHlslRegister GlobalConstants = new(ShaderHlslClass.ConstantBuffer, 0);

    /// <summary>Reads the placements out of Slang's reflection of a DXIL compile.</summary>
    /// <param name="module">The module, for messages.</param>
    /// <param name="slangJson">The reflection JSON.</param>
    /// <returns>The placements.</returns>
    public static HlslPlacements Read(ShaderModuleId module, string slangJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(slangJson);

        Dictionary<string, ShaderHlslRegister> resources = new(StringComparer.Ordinal);
        Dictionary<string, int> uniforms = new(StringComparer.Ordinal);

        Dictionary<string, IReadOnlyList<ShaderUniformField>> constants = ConstantLayouts.Read(slangJson);

        using JsonDocument document = JsonDocument.Parse(slangJson);
        if (!document.RootElement.TryGetProperty("parameters", out JsonElement parameters))
        {
            return new HlslPlacements(resources, uniforms, constants);
        }

        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            string name = parameter.GetProperty("name").GetString()
                ?? throw new ShaderBuildException($"{module}: a DXIL parameter has no name.");

            if (!parameter.TryGetProperty("binding", out JsonElement binding))
            {
                throw new ShaderBuildException($"{module}: the DXIL parameter '{name}' has more than one placement.");
            }

            string kind = binding.GetProperty("kind").GetString() ?? string.Empty;
            if (kind == "uniform")
            {
                uniforms[name] = binding.GetProperty("offset").GetInt32();
                continue;
            }

            ShaderHlslClass registerClass = kind switch
            {
                "shaderResource" => ShaderHlslClass.ShaderResource,
                "unorderedAccess" => ShaderHlslClass.UnorderedAccess,
                "constantBuffer" => ShaderHlslClass.ConstantBuffer,
                "samplerState" => ShaderHlslClass.Sampler,
                _ => throw new ShaderBuildException($"{module}: the DXIL parameter '{name}' has the unhandled kind '{kind}'."),
            };

            int space = binding.TryGetProperty("space", out JsonElement spaceValue) ? spaceValue.GetInt32() : 0;
            resources[name] = new ShaderHlslRegister(
                registerClass, binding.GetProperty("index").GetInt32(), space, StructuredStride(module, name, parameter));
        }

        return new HlslPlacements(resources, uniforms, constants);
    }

    /// <summary>The element size of a structured buffer, as its view must state it; nought for anything else.</summary>
    /// <remarks>
    /// Only what the library declares: thirty-two-bit scalars and vectors of them. A structure would need
    /// HLSL's structured layout worked out here, which nothing has needed yet, so it fails rather than guesses.
    /// </remarks>
    private static int StructuredStride(ShaderModuleId module, string name, JsonElement parameter)
    {
        if (!parameter.TryGetProperty("type", out JsonElement type)
            || !type.TryGetProperty("baseShape", out JsonElement shape)
            || shape.GetString() != "structuredBuffer")
        {
            return 0;
        }

        JsonElement element = type.GetProperty("resultType");
        int count = 1;
        if (element.GetProperty("kind").GetString() == "vector")
        {
            count = element.GetProperty("elementCount").GetInt32();
            element = element.GetProperty("elementType");
        }

        string? scalar = element.TryGetProperty("scalarType", out JsonElement scalarType) ? scalarType.GetString() : null;
        return scalar is "float32" or "int32" or "uint32"
            ? count * 4
            : throw new ShaderBuildException(
                $"{module}: the structured buffer '{name}' holds {element.GetRawText()}, whose HLSL stride the build "
                + "does not work out. Hold thirty-two-bit scalars or vectors, or teach StructuredStride the layout.");
    }

    /// <summary>Checks that two entry points' compiles placed every resource identically.</summary>
    /// <param name="module">The module, for messages.</param>
    /// <param name="first">One entry point's placements.</param>
    /// <param name="second">Another's.</param>
    /// <exception cref="ShaderBuildException">They disagree, which one root signature cannot serve.</exception>
    public static void RequireSame(ShaderModuleId module, HlslPlacements first, HlslPlacements second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        bool same = first.Resources.Count == second.Resources.Count
            && first.Uniforms.Count == second.Uniforms.Count
            && first.Resources.All(pair => second.Resources.TryGetValue(pair.Key, out ShaderHlslRegister? other) && other == pair.Value)
            && first.Uniforms.All(pair => second.Uniforms.TryGetValue(pair.Key, out int other) && other == pair.Value)
            && first.Constants.Count == second.Constants.Count
            && first.Constants.All(pair => second.Constants.TryGetValue(pair.Key, out IReadOnlyList<ShaderUniformField>? other)
                && other.SequenceEqual(pair.Value));

        if (!same)
        {
            throw new ShaderBuildException(
                $"{module}: its entry points were compiled to DXIL with different register layouts, and one "
                + "pipeline has one root signature.");
        }
    }

    /// <summary>Writes the placements onto the reflection the SPIR-V compile produced, matching by name.</summary>
    /// <param name="reflection">The reflection.</param>
    /// <returns>The reflection, every binding, the push block and each specialisation constant placed.</returns>
    /// <exception cref="ShaderBuildException">
    /// The two compiles do not describe the same resources one to one, or Slang put something where its
    /// global constant buffer was expected.
    /// </exception>
    public ShaderReflection Merge(ShaderReflection reflection)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        ShaderModuleId module = reflection.Module;
        HashSet<string> claimed = new(StringComparer.Ordinal);

        List<ShaderBinding> bindings = [];
        foreach (ShaderBinding binding in reflection.Bindings)
        {
            ShaderHlslRegister placed = Take(module, binding.Name, claimed);
            RequireClass(module, binding, placed);
            bindings.Add(binding with { Hlsl = placed });
        }

        ShaderPushBlock? push = reflection.PushBlock;
        if (push is not null)
        {
            ShaderHlslRegister placed = Take(module, push.Name, claimed);
            if (placed.Class != ShaderHlslClass.ConstantBuffer)
            {
                throw new ShaderBuildException($"{module}: the push block '{push.Name}' is not a constant buffer in DXIL.");
            }

            push = push with { Hlsl = placed };
        }

        List<ShaderSpecializationConstant> constants = [];
        foreach (ShaderSpecializationConstant constant in reflection.SpecializationConstants)
        {
            if (!Uniforms.TryGetValue(constant.Name, out int offset))
            {
                throw new ShaderBuildException($"{module}: the specialisation constant '{constant.Name}' has no DXIL placement.");
            }

            constants.Add(constant with { HlslOffset = offset });
        }

        if (Uniforms.Count != constants.Count)
        {
            throw new ShaderBuildException(
                $"{module}: its DXIL form has loose uniforms that are not specialisation constants: "
                + string.Join(", ", Uniforms.Keys.Except(constants.Select(c => c.Name), StringComparer.Ordinal)));
        }

        List<string> unmatched = [.. Resources.Keys.Where(name => !claimed.Contains(name))];
        if (unmatched.Count > 0)
        {
            throw new ShaderBuildException(
                $"{module}: its DXIL form binds resources the SPIR-V form does not: {string.Join(", ", unmatched)}.");
        }

        ShaderHlslRegister? globals = null;
        if (constants.Count > 0)
        {
            globals = GlobalConstants;
            if (Resources.Values.Contains(GlobalConstants))
            {
                throw new ShaderBuildException(
                    $"{module}: a resource sits in the register Slang keeps for its global constant buffer.");
            }
        }

        return reflection with
        {
            Bindings = bindings,
            PushBlock = push,
            SpecializationConstants = constants,
            HlslConstants = globals,
        };
    }

    private ShaderHlslRegister Take(ShaderModuleId module, string name, HashSet<string> claimed)
    {
        if (!Resources.TryGetValue(name, out ShaderHlslRegister? placed))
        {
            throw new ShaderBuildException($"{module}: '{name}' has no DXIL placement, so the two forms disagree.");
        }

        claimed.Add(name);
        return placed;
    }

    private static void RequireClass(ShaderModuleId module, ShaderBinding binding, ShaderHlslRegister placed)
    {
        bool fits = binding.Kind switch
        {
            ShaderBindingKind.Texture or ShaderBindingKind.AccelerationStructure => placed.Class == ShaderHlslClass.ShaderResource,
            ShaderBindingKind.Sampler => placed.Class == ShaderHlslClass.Sampler,
            ShaderBindingKind.StorageTexture => placed.Class == ShaderHlslClass.UnorderedAccess,
            ShaderBindingKind.UniformBuffer => placed.Class == ShaderHlslClass.ConstantBuffer,
            ShaderBindingKind.StorageBuffer => placed.Class == (binding.StorageAccess == ShaderStorageAccess.ReadOnly
                ? ShaderHlslClass.ShaderResource
                : ShaderHlslClass.UnorderedAccess),
            _ => false,
        };

        if (!fits)
        {
            throw new ShaderBuildException(
                $"{module}: '{binding.Name}' is a {binding.Kind} but its DXIL form binds it as {placed.Class}.");
        }
    }
}
