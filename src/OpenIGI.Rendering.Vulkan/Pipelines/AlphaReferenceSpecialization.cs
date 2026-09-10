using OpenIGI.Core.Numerics;
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

    /// <summary>The <c>constant_id</c>s the forward mesh shader declares the scene's fog under.</summary>
    /// <remarks>
    /// <para>
    /// Constants for the same reason the two above are — no push room — and, unlike those two, that is
    /// no compromise at all: a level's fog IS a constant. I.G.I. 2 reads one colour and one density off
    /// the level's <c>GlobalLight</c> when the mission loads and never changes them while it is played
    /// (<c>GlobalLightQTask_DrawCallback</c>, <c>igi2ed 0x6534E0</c>). Baking them means the fog costs a
    /// multiply-add in the shaders that have it and nothing whatever in the ones that do not — which is
    /// every pipeline Project I.G.I. builds, since it reports no atmosphere.
    /// </para>
    /// </remarks>
    public const uint FogColourRedConstantId = 2;

    /// <summary>The green channel of the scene's fog colour.</summary>
    public const uint FogColourGreenConstantId = 3;

    /// <summary>The blue channel of the scene's fog colour.</summary>
    public const uint FogColourBlueConstantId = 4;

    /// <summary>The scene fog's extinction per metre; zero is no fog, and no cost.</summary>
    public const uint FogDensityConstantId = 5;

    /// <summary>Fills a description of the forward mesh shader's constants.</summary>
    /// <param name="data">Room for six four-byte values, which must outlive the pipeline creation call.</param>
    /// <param name="entries">Room for six map entries, likewise.</param>
    /// <param name="reference">The cut-out threshold to bake in.</param>
    /// <param name="reflective">Whether the variant samples an environment cube.</param>
    /// <param name="fogColour">The scene's fog colour.</param>
    /// <param name="fogDensity">The scene's fog extinction per metre; zero is no fog.</param>
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
        bool reflective,
        Vector3f fogColour,
        float fogDensity)
    {
        float unit = ToUnit(reference);
        data[0] = *(uint*)&unit;
        data[1] = reflective ? 1u : 0u;
        float red = fogColour.X;
        float green = fogColour.Y;
        float blue = fogColour.Z;
        data[2] = *(uint*)&red;
        data[3] = *(uint*)&green;
        data[4] = *(uint*)&blue;
        data[5] = *(uint*)&fogDensity;

        entries[0] = new SpecializationMapEntry { ConstantID = ConstantId, Offset = 0, Size = sizeof(float) };
        entries[1] = new SpecializationMapEntry
        {
            ConstantID = ReflectiveConstantId,
            Offset = sizeof(float),
            Size = sizeof(int),
        };
        entries[2] = new SpecializationMapEntry
        {
            ConstantID = FogColourRedConstantId,
            Offset = sizeof(float) * 2,
            Size = sizeof(float),
        };
        entries[3] = new SpecializationMapEntry
        {
            ConstantID = FogColourGreenConstantId,
            Offset = sizeof(float) * 3,
            Size = sizeof(float),
        };
        entries[4] = new SpecializationMapEntry
        {
            ConstantID = FogColourBlueConstantId,
            Offset = sizeof(float) * 4,
            Size = sizeof(float),
        };
        entries[5] = new SpecializationMapEntry
        {
            ConstantID = FogDensityConstantId,
            Offset = sizeof(float) * 5,
            Size = sizeof(float),
        };

        return new SpecializationInfo
        {
            MapEntryCount = 6,
            PMapEntries = entries,
            DataSize = sizeof(uint) * 6,
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
