using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>
/// How the cut-out threshold reaches a fragment shader: baked into the pipeline, not pushed per draw.
/// </summary>
/// <remarks>
/// <para>
/// A specialization constant because there is nowhere else for it to go. Both mesh push blocks are
/// exactly the 128 bytes Vulkan guarantees and every float in them is spoken for — the model-view fills
/// sixteen, the normal matrix nine, and the rest carry the marker, the exposure, the pose blend, the key
/// colour, the material and the lightmap scale. Widening the range past 128 would break on the hardware
/// that reports exactly that much, which is not rare.
/// </para>
/// <para>
/// It costs a pipeline per distinct threshold, and that is affordable precisely because
/// <see cref="IGraphicsDevice.SetAlphaReference"/> takes a <b>byte</b>: the space is the 256 values
/// <c>D3DRS_ALPHAREF</c> itself has, a level uses two or three of them, and the pipelines are built on
/// demand. A seam taking a float would have made this unbounded.
/// </para>
/// <para>
/// Every shader that cuts out has to take the same constant. The G-buffer, the forward mesh and the
/// shadow pass all test the same texel, and the shadow pass says so in its own comment: a pass that cuts
/// on less than the G-buffer keeps casters the frame never draws, and one that cuts on more drops
/// shadows from things you can see.
/// </para>
/// </remarks>
internal static unsafe class AlphaReferenceSpecialization
{
    /// <summary>The <c>constant_id</c> the shaders declare the threshold under.</summary>
    public const uint ConstantId = 0;

    /// <summary>The threshold a shader falls back to when no specialization is supplied.</summary>
    /// <remarks>
    /// <c>128 / 255</c>, which is the <c>0.5</c> the shaders used to test against to within a texel
    /// value. The default is written into the GLSL as well, so a pipeline built without a
    /// specialization draws exactly what this port always drew.
    /// </remarks>
    public const float DefaultUnit = IGraphicsDevice.DefaultAlphaReference / 255f;

    /// <summary>Turns a reference out of 255 into the fraction a shader compares against.</summary>
    /// <param name="reference">The reference.</param>
    /// <returns>The fraction.</returns>
    public static float ToUnit(byte reference) => reference / 255f;

    /// <summary>The <c>constant_id</c> the forward mesh shader declares its reflection switch under.</summary>
    public const uint ReflectiveConstantId = 1;

    /// <summary>Fills a description of both the forward mesh shader's constants.</summary>
    /// <param name="data">Room for two four-byte values, which must outlive the pipeline creation call.</param>
    /// <param name="entries">Room for two map entries, likewise.</param>
    /// <param name="reference">The cut-out threshold to bake in.</param>
    /// <param name="reflective">Whether the variant samples an environment cube.</param>
    /// <returns>The description.</returns>
    /// <remarks>
    /// The reflection switch is a constant for the same reason the threshold is — no push room — and it
    /// buys more than the threshold does. A cube lookup that every draw in the world computed and threw
    /// away would be a texture fetch per fragment for the one surface in the level that reflects; as a
    /// constant, the branch is gone from the shader the other pipelines were built from.
    /// </remarks>
    public static SpecializationInfo DescribeMesh(
        uint* data,
        SpecializationMapEntry* entries,
        byte reference,
        bool reflective)
    {
        float unit = ToUnit(reference);
        data[0] = *(uint*)&unit;
        data[1] = reflective ? 1u : 0u;

        entries[0] = new SpecializationMapEntry { ConstantID = ConstantId, Offset = 0, Size = sizeof(float) };
        entries[1] = new SpecializationMapEntry
        {
            ConstantID = ReflectiveConstantId,
            Offset = sizeof(float),
            Size = sizeof(int),
        };

        return new SpecializationInfo
        {
            MapEntryCount = 2,
            PMapEntries = entries,
            DataSize = sizeof(uint) * 2,
            PData = data,
        };
    }

    /// <summary>Fills a specialization description from storage the caller owns.</summary>
    /// <param name="value">Room for the one float, which must outlive the pipeline creation call.</param>
    /// <param name="entry">Room for the one map entry, likewise.</param>
    /// <param name="reference">The reference to bake in.</param>
    /// <returns>The description, pointing at <paramref name="value"/> and <paramref name="entry"/>.</returns>
    /// <remarks>
    /// Takes pointers rather than returning its own storage because a <see cref="SpecializationInfo"/>
    /// is only a pair of pointers: one that owned its buffer would have to stay alive across the
    /// caller's <c>vkCreateGraphicsPipelines</c>, and the caller's stack frame is exactly the scope that
    /// does.
    /// </remarks>
    public static SpecializationInfo Describe(float* value, SpecializationMapEntry* entry, byte reference)
    {
        *value = ToUnit(reference);
        *entry = new SpecializationMapEntry
        {
            ConstantID = ConstantId,
            Offset = 0,
            Size = sizeof(float),
        };

        return new SpecializationInfo
        {
            MapEntryCount = 1,
            PMapEntries = entry,
            DataSize = sizeof(float),
            PData = value,
        };
    }
}
