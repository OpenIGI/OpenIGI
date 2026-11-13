using OpenIGI.Shaders;
using Silk.NET.SPIRV.Cross;
using CrossApi = Silk.NET.SPIRV.Cross.Cross;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>The OpenGL unit every resource of one variant is bound at, the same in every stage.</summary>
/// <remarks>
/// <para>
/// Numbered in the order the reflection lists the bindings -- set, then binding -- within each of OpenGL's
/// namespaces: uniform buffers, storage buffers, image units, texture units. The push block follows the
/// module's own buffers of its kind, being in the highest set.
/// </para>
/// <para>
/// Texture units go one per texture-and-sampler pair any stage reads, so a texture read through two samplers
/// takes two. The spike over the whole library found no stage above seven pairs or four images, inside the
/// sixteen texture units and eight image units every OpenGL 4.3 device has.
/// </para>
/// </remarks>
internal sealed unsafe class GlslUnits
{
    /// <summary>Gets the resource kinds numbered by their own placement rather than as a pair.</summary>
    internal static readonly ResourceType[] BufferAndImageKinds =
    [
        ResourceType.UniformBuffer,
        ResourceType.StorageBuffer,
        ResourceType.StorageImage,
    ];

    private readonly ShaderReflection _reflection;
    private readonly Dictionary<(ResourceType Type, int Group, int Binding), int> _units;
    private readonly Dictionary<(string Texture, string? Sampler), int> _pairs;

    private GlslUnits(
        ShaderReflection reflection,
        Dictionary<(ResourceType, int, int), int> units,
        Dictionary<(string, string?), int> pairs)
    {
        _reflection = reflection;
        _units = units;
        _pairs = pairs;
    }

    /// <summary>Numbers what every stage of a variant binds.</summary>
    /// <param name="api">SPIRV-Cross.</param>
    /// <param name="stages">The variant's stages, with their textures already joined to samplers.</param>
    /// <param name="reflection">The variant's reflection, which names every binding.</param>
    /// <param name="where">The module and variant, for messages.</param>
    /// <returns>The units.</returns>
    public static GlslUnits Assign(
        CrossApi api,
        IReadOnlyList<GlslTranslator.StageCompiler> stages,
        ShaderReflection reflection,
        string where)
    {
        GlslUnits names = new(reflection, [], []);

        HashSet<(ResourceType Type, int Group, int Binding)> declared = [];
        HashSet<(string Texture, string? Sampler)> pairs = [];
        foreach (GlslTranslator.StageCompiler stage in stages)
        {
            foreach (ResourceType type in BufferAndImageKinds)
            {
                foreach (ReflectedResource resource in stage.Resources(api, type, where))
                {
                    (int group, int binding) = stage.Placement(api, resource.Id);
                    names.NameOf(group, binding, where);
                    declared.Add((type, group, binding));
                }
            }

            foreach (CombinedImageSampler pair in stage.Pairs(api, where))
            {
                pairs.Add(names.PairOf(stage, api, pair, where));
            }
        }

        Dictionary<(ResourceType, int, int), int> units = [];
        foreach (IGrouping<ResourceType, (ResourceType Type, int Group, int Binding)> kind in declared.GroupBy(d => d.Type))
        {
            int next = 0;
            foreach ((ResourceType Type, int Group, int Binding) resource in kind.OrderBy(r => r.Group).ThenBy(r => r.Binding))
            {
                units[resource] = next++;
            }
        }

        Dictionary<(string, string?), int> textureUnits = [];
        int unit = 0;
        foreach ((string Texture, string? Sampler) pair in pairs
            .OrderBy(p => names.IndexOf(p.Texture))
            .ThenBy(p => p.Sampler is null ? -1 : names.IndexOf(p.Sampler)))
        {
            textureUnits[pair] = unit++;
        }

        return new GlslUnits(reflection, units, textureUnits);
    }

    /// <summary>Gets the unit of a buffer or image.</summary>
    public int UnitOf(ResourceType type, (int Group, int Binding) placement, string where) =>
        _units.TryGetValue((type, placement.Group, placement.Binding), out int unit)
            ? unit
            : throw new ShaderBuildException($"{where}: no GLSL unit was given to {type} at set {placement.Group} binding {placement.Binding}.");

    /// <summary>Gets the texture unit of a pair.</summary>
    public int UnitOf((string Texture, string? Sampler) pair, string where) =>
        _pairs.TryGetValue(pair, out int unit)
            ? unit
            : throw new ShaderBuildException($"{where}: no texture unit was given to {pair.Texture} with {pair.Sampler ?? "no sampler"}.");

    /// <summary>Names the texture and sampler SPIRV-Cross joined, by the reflection's names for them.</summary>
    public (string Texture, string? Sampler) PairOf(
        GlslTranslator.StageCompiler stage,
        CrossApi api,
        CombinedImageSampler pair,
        string where)
    {
        (int group, int binding) = stage.Placement(api, pair.ImageId);
        string texture = NameOf(group, binding, where);

        if (pair.SamplerId == stage.DummySampler)
        {
            return (texture, null);
        }

        (int samplerGroup, int samplerBinding) = stage.Placement(api, pair.SamplerId);
        return (texture, NameOf(samplerGroup, samplerBinding, where));
    }

    /// <summary>Writes the units into the reflection.</summary>
    /// <param name="reflection">The reflection.</param>
    /// <returns>It, with every buffer's and image's unit, the push block's, and the texture units.</returns>
    public ShaderReflection Apply(ShaderReflection reflection)
    {
        List<ShaderBinding> bindings = [];
        foreach (ShaderBinding binding in reflection.Bindings)
        {
            ResourceType? type = binding.Kind switch
            {
                ShaderBindingKind.UniformBuffer => ResourceType.UniformBuffer,
                ShaderBindingKind.StorageBuffer => ResourceType.StorageBuffer,
                ShaderBindingKind.StorageTexture => ResourceType.StorageImage,
                _ => null,
            };

            int? unit = type is { } kind && _units.TryGetValue((kind, binding.Group, binding.Binding), out int found) ? found : null;
            bindings.Add(binding with { GlslUnit = unit });
        }

        ShaderPushBlock? push = reflection.PushBlock;
        if (push is not null)
        {
            int? unit = null;
            foreach (ResourceType kind in (ResourceType[])[ResourceType.UniformBuffer, ResourceType.StorageBuffer])
            {
                if (_units.TryGetValue((kind, push.Group, push.Binding), out int found))
                {
                    unit = found;
                }
            }

            push = push with { GlslUnit = unit };
        }

        List<ShaderGlslSampler> samplers = [.. _pairs
            .OrderBy(p => p.Value)
            .Select(p => new ShaderGlslSampler(p.Key.Texture, p.Key.Sampler, p.Value))];

        return reflection with { Bindings = bindings, PushBlock = push, GlslSamplers = samplers };
    }

    private int IndexOf(string name)
    {
        for (int i = 0; i < _reflection.Bindings.Count; i++)
        {
            if (string.Equals(_reflection.Bindings[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private string NameOf(int group, int binding, string where)
    {
        foreach (ShaderBinding candidate in _reflection.Bindings)
        {
            if (candidate.Group == group && candidate.Binding == binding)
            {
                return candidate.Name;
            }
        }

        if (_reflection.PushBlock is { } push && push.Group == group && push.Binding == binding)
        {
            return push.Name;
        }

        throw new ShaderBuildException(
            $"{where}: the GLSL form binds set {group} binding {binding}, which the reflection does not name.");
    }
}
