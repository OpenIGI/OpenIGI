using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using OpenIGI.Core;

namespace OpenIGI.Formats.World;

/// <summary>
/// A decoded <c>.mtp</c> resource pack: the name tables, the material table and the instance placements
/// that bind a level's models to its textures.
/// </summary>
/// <remarks>
/// <para>
/// This is the data half of the original's <c>Pack_Open</c>/<c>Pack_Bind</c> pair (<c>0x4CEFA0</c> →
/// <c>0x4CF080</c>). Loading the sibling <c>models/</c> and <c>textures/</c> archives is a resource-system
/// job and lives outside this type.
/// </para>
/// <para>
/// The sibling <c>.dat</c> file is <em>not</em> part of the runtime path — see <see cref="DatIndex"/>.
/// </para>
/// </remarks>
public sealed class MtpPack
{
    private MtpPack(
        MtpFile file,
        IReadOnlyList<string> animationSetNames,
        IReadOnlyList<string> soundNames,
        IReadOnlyList<string> volumeNames,
        IReadOnlyList<string> modelNames,
        IReadOnlyList<string> textureNames,
        IReadOnlyList<string> paletteNames,
        IReadOnlyList<MtpMaterial> materials,
        IReadOnlyList<MtpInstance> instances)
    {
        File = file;
        AnimationSetNames = animationSetNames;
        SoundNames = soundNames;
        VolumeNames = volumeNames;
        ModelNames = modelNames;
        TextureNames = textureNames;
        PaletteNames = paletteNames;
        Materials = materials;
        Instances = instances;
    }

    /// <summary>Gets the underlying chunk list.</summary>
    public MtpFile File { get; }

    /// <summary>Gets the animation set names from <c>BANM</c> — each loads <c>anims/&lt;name&gt;.iff</c>.</summary>
    /// <remarks><c>location0</c> names the six shipped sets; no retail level pack names any.</remarks>
    public IReadOnlyList<string> AnimationSetNames { get; }

    /// <summary>Gets the sound names from <c>SNDS</c>. Empty in every retail pack.</summary>
    public IReadOnlyList<string> SoundNames { get; }

    /// <summary>Gets the dev-only volume model names from <c>SVOL</c>. Empty in every retail pack.</summary>
    /// <remarks>
    /// The original loads these through a separate IFF-based container (<c>0x4CF690</c>, chunks
    /// <c>SMES</c>/<c>SFAC</c>/<c>SVTX</c>/<c>EDGE</c>) that no shipped <c>.mef</c> uses. OpenIGI parses the
    /// names so a mod is visible, but does not implement that container.
    /// </remarks>
    public IReadOnlyList<string> VolumeNames { get; }

    /// <summary>Gets the model names from <c>MODS</c>. <strong>This order is the model id space.</strong></summary>
    /// <remarks>
    /// It is not the order of the sibling <c>models/&lt;pack&gt;.res</c> archive; indexing the archive
    /// instead mismatched 1625 of 6795 retail models.
    /// </remarks>
    public IReadOnlyList<string> ModelNames { get; }

    /// <summary>Gets the texture names from <c>TEXF</c>. This order is the texture id space.</summary>
    public IReadOnlyList<string> TextureNames { get; }

    /// <summary>Gets the palette names from <c>PALF</c>. Empty in every retail pack.</summary>
    public IReadOnlyList<string> PaletteNames { get; }

    /// <summary>Gets the material table from <c>GTT </c>, one entry per texture.</summary>
    public IReadOnlyList<MtpMaterial> Materials { get; }

    /// <summary>Gets the instance placements, one per <c>VNAM</c> name.</summary>
    public IReadOnlyList<MtpInstance> Instances { get; }

    /// <summary>Parses a pack.</summary>
    /// <param name="data">The <c>.mtp</c> bytes.</param>
    /// <returns>The decoded pack.</returns>
    /// <exception cref="PackFormatException">The pack is malformed or a required chunk is missing.</exception>
    public static MtpPack Read(ReadOnlyMemory<byte> data) => FromFile(MtpFile.Read(data));

    /// <summary>Decodes an already-parsed chunk list.</summary>
    /// <param name="file">The chunk list.</param>
    /// <returns>The decoded pack.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    /// <exception cref="PackFormatException">A chunk is malformed or a required chunk is missing.</exception>
    public static MtpPack FromFile(MtpFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        ReadOnlyCollection<string> instanceNames = ReadInstanceNames(file);
        ReadOnlyCollection<MtpMaterial> materials = ReadMaterials(file);
        ReadOnlyCollection<string> modelNames = ReadNames(file, MtpTags.ModelNames, required: true);
        ReadOnlyCollection<string> textureNames = ReadNames(file, MtpTags.TextureNames, required: true);

        return new MtpPack(
            file,
            ReadNames(file, MtpTags.AnimationSetNames, required: false),
            ReadNames(file, MtpTags.SoundNames, required: false),
            ReadNames(file, MtpTags.VolumeNames, required: false),
            modelNames,
            textureNames,
            ReadNames(file, MtpTags.PaletteNames, required: false),
            materials,
            ReadInstances(file, instanceNames, materials.Count));
    }

    /// <summary>Resolves an instance's material slot to the material it draws.</summary>
    /// <param name="instance">The instance the draw record belongs to.</param>
    /// <param name="materialSlot">The slot from a model's draw record.</param>
    /// <param name="material">Receives the material when the slot addresses one.</param>
    /// <returns>
    /// <see langword="false"/> when <paramref name="materialSlot"/> is
    /// <see cref="MefDrawRecord.NoMaterialSlot"/> or out of range for the instance, which means the record
    /// draws untextured.
    /// </returns>
    public bool TryGetMaterial(MtpInstance instance, int materialSlot, out MtpMaterial material)
    {
        if (materialSlot < 0 || materialSlot >= instance.SlotCount)
        {
            material = default;
            return false;
        }

        int lookup = instance.MaterialIndices.Span[materialSlot];
        if (lookup < 0 || lookup >= Materials.Count)
        {
            material = default;
            return false;
        }

        // INST carries a GTT index, and GTT carries the texture and the palette for it -- see
        // MTP_ParseMTP (igi2.exe 0x4F5DE0), which reads both halves as gtt[2 * slot] and
        // gtt[2 * slot + 1]. An earlier reading here took the INST value as a TEXF index directly and
        // only indexed GTT for the palette, which is self-inconsistent; it produced the right answer
        // because GTT's texture half is the identity mapping in all 50 packs the two games ship, which
        // RetailMtpMaterialTests pins so the equivalence stays a measurement rather than an assumption.
        material = Materials[lookup];
        return !material.Texture.IsNone && material.Texture.Index < TextureNames.Count;
    }

    /// <summary>Resolves the diffuse texture a draw record samples, all the way to its name.</summary>
    /// <param name="instance">The instance being drawn.</param>
    /// <param name="materialSlot">The slot from the model's draw record.</param>
    /// <param name="textureName">Receives the texture's bare name, without a path or an extension.</param>
    /// <returns><see langword="false"/> when the record draws untextured.</returns>
    /// <remarks>
    /// The whole chain in one call — slot to <c>GTT</c> to <c>TEXF</c> — because both games walk it
    /// identically and a title that resolved it itself would be reimplementing
    /// <see cref="TryGetMaterial"/>'s other half.
    /// <para>
    /// A texture id addressing the common pack is refused rather than followed. The pack list a common id
    /// would be resolved against is the resource system's, not this type's, and no retail pack in either
    /// game ships one — <c>GTT</c>'s texture half is the identity mapping throughout, so every id is
    /// below <see cref="PackId.CommonBase"/> by construction. A caller that one day needs the common
    /// case has to supply the other pack, which is a signature this cannot have.
    /// </para>
    /// </remarks>
    public bool TryGetTextureName(MtpInstance instance, int materialSlot, [NotNullWhen(true)] out string? textureName)
    {
        textureName = null;
        if (!TryGetMaterial(instance, materialSlot, out MtpMaterial material) || material.Texture.IsCommon)
        {
            return false;
        }

        textureName = TextureNames[material.Texture.Index];
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"MTP pack: {ModelNames.Count} models, {TextureNames.Count} textures, {Instances.Count} instances";

    private static ReadOnlyCollection<string> ReadNames(MtpFile file, FourCc tag, bool required)
    {
        if (!file.TryFindChunk(tag, out MtpChunk chunk))
        {
            return required
                ? throw new PackFormatException($"The pack has no '{tag}' chunk.")
                : ReadOnlyCollection<string>.Empty;
        }

        ReadOnlySpan<byte> span = chunk.Payload.Span;
        int count = ReadCount(span, tag);
        return ReadPool(span[4..], count, tag, 0);
    }

    private static ReadOnlyCollection<string> ReadInstanceNames(MtpFile file)
    {
        MtpChunk chunk = file.GetChunk(MtpTags.InstanceNames);
        ReadOnlySpan<byte> span = chunk.Payload.Span;
        int count = ReadCount(span, MtpTags.InstanceNames);

        // VNAM is `count`, then `count` u32s, then the pool. The u32 table is derived data — it is the
        // running sum of `4 + 4*slotCount` over the INST records — and the original ignores it, as do we.
        int poolStart = 4 + (4 * count);
        if (poolStart > span.Length)
        {
            throw new PackFormatException(
                $"The 'VNAM' chunk declares {count} names but is only {span.Length} bytes, too small for its offset table.");
        }

        return ReadPool(span[poolStart..], count, MtpTags.InstanceNames, poolStart);
    }

    private static ReadOnlyCollection<MtpMaterial> ReadMaterials(MtpFile file)
    {
        MtpChunk chunk = file.GetChunk(MtpTags.MaterialTable);
        ReadOnlySpan<byte> span = chunk.Payload.Span;
        int count = ReadCount(span, MtpTags.MaterialTable);

        int needed = 4 + (count * MtpMaterial.Size);
        if (needed > span.Length)
        {
            throw new PackFormatException(
                $"The 'GTT ' chunk declares {count} materials, which needs {needed} bytes, but it is {span.Length}.");
        }

        MtpMaterial[] materials = new MtpMaterial[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = span.Slice(4 + (i * MtpMaterial.Size), MtpMaterial.Size);
            materials[i] = new MtpMaterial(
                new PackId(BinaryPrimitives.ReadUInt32LittleEndian(entry)),
                new PackId(BinaryPrimitives.ReadUInt32LittleEndian(entry[4..])));
        }

        return new ReadOnlyCollection<MtpMaterial>(materials);
    }

    private static ReadOnlyCollection<MtpInstance> ReadInstances(MtpFile file, ReadOnlyCollection<string> names, int materialCount)
    {
        MtpChunk chunk = file.GetChunk(MtpTags.Instances);
        ReadOnlyMemory<byte> payload = chunk.Payload;
        ReadOnlySpan<byte> span = payload.Span;

        MtpInstance[] instances = new MtpInstance[names.Count];
        int offset = 0;
        for (int i = 0; i < names.Count; i++)
        {
            if (offset + 8 > span.Length)
            {
                throw new PackFormatException(
                    $"The 'INST' chunk ran out after {i} of {names.Count} records ({span.Length} bytes).");
            }

            uint model = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            uint slots = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            offset += 8;

            if (slots > int.MaxValue / 4 || (long)offset + (4L * slots) > span.Length)
            {
                throw new PackFormatException(
                    $"Instance '{names[i]}' declares {slots} material slots, which do not fit the 'INST' chunk.");
            }

            int slotCount = (int)slots;
            int[] indices = new int[slotCount];
            for (int j = 0; j < slotCount; j++)
            {
                uint raw = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + (4 * j))..]);
                // A slot is an index into GTT, which is what MTP_ParseMTP indexes with it; TryGetMaterial
                // then reads the texture and the palette out of that entry. Every retail pack gives GTT
                // exactly as many entries as TEXF, so bounding against either accepts the same files --
                // this bounds against the table actually indexed.
                if (raw >= (uint)materialCount)
                {
                    throw new PackFormatException(
                        $"Instance '{names[i]}' slot {j} references material {raw}, but the pack has {materialCount}.");
                }

                indices[j] = (int)raw;
            }

            offset += 4 * slotCount;
            instances[i] = new MtpInstance(names[i], new PackId(model), indices);
        }

        // The walk consumes the chunk byte-exactly for every retail pack; a leftover means a misread.
        if (offset != span.Length)
        {
            throw new PackFormatException(
                $"The 'INST' walk consumed {offset} of {span.Length} bytes after {names.Count} records.");
        }

        return new ReadOnlyCollection<MtpInstance>(instances);
    }

    private static int ReadCount(ReadOnlySpan<byte> payload, FourCc tag)
    {
        if (payload.Length < 4)
        {
            throw new PackFormatException($"The '{tag}' chunk is {payload.Length} bytes, too small for its count.");
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (count > int.MaxValue)
        {
            throw new PackFormatException($"The '{tag}' chunk declares {count} entries, too many to address.");
        }

        return (int)count;
    }

    private static ReadOnlyCollection<string> ReadPool(ReadOnlySpan<byte> pool, int count, FourCc tag, int poolStart)
    {
        if (count == 0)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        string[] names = new string[count];
        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            int terminator = pool[offset..].IndexOf((byte)0);
            if (terminator < 0)
            {
                throw new PackFormatException(
                    $"The '{tag}' name pool ended without a terminator at name {i} of {count} (pool offset {poolStart + offset}).");
            }

            names[i] = Encoding.Latin1.GetString(pool.Slice(offset, terminator));
            offset += terminator + 1;
        }

        return new ReadOnlyCollection<string>(names);
    }
}
