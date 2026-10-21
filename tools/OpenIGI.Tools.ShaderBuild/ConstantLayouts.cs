using System.Globalization;
using System.Text.Json;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Where each field of a module's constant blocks sits, as one compile laid them out.</summary>
/// <remarks>
/// <para>
/// Slang does not lay a constant buffer out the same way for every target. SPIR-V gets std140 for a uniform
/// buffer and std430 for a push block; DXIL gets HLSL's constant-buffer packing, which leaves the last
/// element of an array unpadded and lets a two-component vector share a register with the scalar before
/// it. <c>float b[4]; int c;</c> puts <c>c</c> at byte 64 past the array's start in SPIR-V and at 52 in
/// DXIL, and every field after it moves too.
/// </para>
/// <para>
/// A host that fills a block by the SPIR-V offsets therefore writes Direct3D 12's fields in the wrong
/// places, and nothing reports it: the screen-space GI read its screen size and its pass number from the
/// wrong bytes and came out black. The build reads both layouts so the reflection can carry the HLSL one
/// where a block is filled by name, and fails where a block is filled from a fixed host structure that
/// only one of them can match.
/// </para>
/// </remarks>
public static class ConstantLayouts
{
    /// <summary>Reads every constant block's fields out of one compile's reflection, descending into structures.</summary>
    /// <param name="slangJson">Slang's reflection JSON.</param>
    /// <returns>Each block's fields, flattened to the scalars, vectors and matrices a host writes, keyed by block name.</returns>
    public static Dictionary<string, IReadOnlyList<ShaderUniformField>> Read(string slangJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(slangJson);

        Dictionary<string, IReadOnlyList<ShaderUniformField>> blocks = new(StringComparer.Ordinal);
        using JsonDocument document = JsonDocument.Parse(slangJson);
        if (!document.RootElement.TryGetProperty("parameters", out JsonElement parameters))
        {
            return blocks;
        }

        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            if (parameter.GetProperty("name").GetString() is not { } name
                || !parameter.TryGetProperty("type", out JsonElement type)
                || !type.TryGetProperty("kind", out JsonElement kind)
                || kind.GetString() != "constantBuffer"
                || !type.TryGetProperty("elementType", out JsonElement element)
                || !element.TryGetProperty("fields", out JsonElement fields))
            {
                continue;
            }

            List<ShaderUniformField> flattened = [];
            Flatten(fields, 0, string.Empty, intoStructures: true, flattened);
            blocks[name] = flattened;
        }

        return blocks;
    }

    /// <summary>Lists a block's fields as a host addresses them.</summary>
    /// <param name="fields">The block's <c>fields</c> array.</param>
    /// <param name="baseOffset">Where the enclosing structure starts in the block.</param>
    /// <param name="prefix">The enclosing structure's name and a dot, or empty at the top.</param>
    /// <param name="intoStructures">
    /// Whether a structure field is listed by its members; otherwise it is one field, as a block filled by name
    /// sets it.
    /// </param>
    /// <param name="into">Receives the fields.</param>
    /// <remarks>
    /// An array is listed per element, under the indexed name a host sets it by, because the compute seam sets
    /// one parameter per call: every host that fills one writes <c>uThing[0]</c>, <c>uThing[1]</c> and so on.
    /// Each element's size is its stride, which is what the layout gives it.
    /// </remarks>
    public static void Flatten(JsonElement fields, int baseOffset, string prefix, bool intoStructures, List<ShaderUniformField> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (JsonElement field in fields.EnumerateArray())
        {
            JsonElement placement = field.GetProperty("binding");
            int offset = baseOffset + (placement.TryGetProperty("offset", out JsonElement at) ? at.GetInt32() : 0);
            int length = placement.TryGetProperty("size", out JsonElement bytes) ? bytes.GetInt32() : 0;
            string name = prefix + (field.GetProperty("name").GetString() ?? string.Empty);
            JsonElement type = field.GetProperty("type");
            string kind = type.TryGetProperty("kind", out JsonElement kindValue) ? kindValue.GetString() ?? string.Empty : string.Empty;
            int stride = placement.TryGetProperty("elementStride", out JsonElement step) ? step.GetInt32() : 0;

            if (kind == "array" && stride > 0)
            {
                JsonElement element = type.GetProperty("elementType");
                bool structured = intoStructures
                    && element.TryGetProperty("kind", out JsonElement elementKind)
                    && elementKind.GetString() == "struct";

                for (int index = 0; index * stride < length; index++)
                {
                    string indexed = string.Create(CultureInfo.InvariantCulture, $"{name}[{index}]");
                    int at2 = offset + (index * stride);
                    if (structured)
                    {
                        Flatten(element.GetProperty("fields"), at2, indexed + ".", intoStructures, into);
                    }
                    else
                    {
                        into.Add(new ShaderUniformField(indexed, at2, stride));
                    }
                }

                continue;
            }

            if (kind == "struct" && intoStructures && type.TryGetProperty("fields", out JsonElement members))
            {
                Flatten(members, offset, name + ".", intoStructures, into);
                continue;
            }

            into.Add(new ShaderUniformField(name, offset, length));
        }
    }

    /// <summary>Gives the reflection the HLSL layout of its named-parameter block, and checks every other block agrees.</summary>
    /// <param name="module">The module, for messages.</param>
    /// <param name="variant">The variant, for messages.</param>
    /// <param name="reflection">The reflection the SPIR-V compile produced.</param>
    /// <param name="spirv">The SPIR-V compile's constant blocks.</param>
    /// <param name="dxil">The DXIL compile's.</param>
    /// <returns>The reflection, <see cref="ShaderReflection.Globals"/> carrying each field's HLSL offset.</returns>
    /// <exception cref="ShaderBuildException">
    /// A block other than the named-parameter one is laid out differently by the two targets, or the DXIL
    /// form has no placement for one of the named parameters.
    /// </exception>
    public static ShaderReflection Reconcile(
        ShaderModuleId module,
        string variant,
        ShaderReflection reflection,
        IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformField>> spirv,
        IReadOnlyDictionary<string, IReadOnlyList<ShaderUniformField>> dxil)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        ArgumentNullException.ThrowIfNull(spirv);
        ArgumentNullException.ThrowIfNull(dxil);

        ShaderUniformBlock? globals = reflection.Globals;
        List<string> disagreements = [];

        foreach ((string block, IReadOnlyList<ShaderUniformField> spirvFields) in spirv)
        {
            // The named-parameter block is the one whose layout may differ: its host writes each field at the
            // offset its target's table gives, so it is placed below rather than checked.
            if (globals is not null && block == globals.Name)
            {
                continue;
            }

            if (!dxil.TryGetValue(block, out IReadOnlyList<ShaderUniformField>? dxilFields))
            {
                continue;
            }

            Dictionary<string, int> placed = dxilFields.ToDictionary(static field => field.Name, static field => field.Offset, StringComparer.Ordinal);
            foreach (ShaderUniformField field in spirvFields)
            {
                if (placed.TryGetValue(field.Name, out int offset) && offset != field.Offset)
                {
                    disagreements.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{block}.{field.Name} at {field.Offset} in SPIR-V and {offset} in DXIL"));
                }
            }
        }

        if (disagreements.Count > 0)
        {
            throw new ShaderBuildException(
                $"{module} ({variant}): its constant blocks are laid out differently for Vulkan and Direct3D 12, and the "
                + "host fills them from one structure: " + string.Join("; ", disagreements) + ". Reorder or pad the "
                + "fields so both layouts agree -- four-component vectors, or scalars grouped in fours, never an array "
                + "of scalars or a two-component vector after an odd scalar.");
        }

        if (globals is null)
        {
            return reflection;
        }

        // Matched by name. The named-parameter blocks hold no structures, so the two tables name their fields
        // alike; one that grew a structure would find no match here and say so rather than misplace it.
        Dictionary<string, ShaderUniformField> byName = dxil.TryGetValue(globals.Name, out IReadOnlyList<ShaderUniformField>? declared)
            ? declared.ToDictionary(static field => field.Name, StringComparer.Ordinal)
            : [];
        List<ShaderUniformField> fields = [];
        int size = 0;
        foreach (ShaderUniformField field in globals.Fields)
        {
            if (!byName.TryGetValue(field.Name, out ShaderUniformField? hlsl))
            {
                throw new ShaderBuildException($"{module} ({variant}): the parameter '{field.Name}' has no place in the DXIL form's {globals.Name}.");
            }

            fields.Add(field with { HlslOffset = hlsl.Offset });
            size = Math.Max(size, hlsl.Offset + hlsl.Size);
        }

        return reflection with
        {
            Globals = globals with { Fields = fields, HlslSize = (size + 15) & ~15 },
        };
    }
}
