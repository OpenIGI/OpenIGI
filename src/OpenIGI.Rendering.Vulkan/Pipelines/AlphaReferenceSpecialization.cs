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

    /// <summary>The <c>constant_id</c>s the forward mesh vertex stage declares the fixed-function sun under.</summary>
    /// <remarks>
    /// The two colours of the light I.G.I. 2 shines on an object with no baked lightmap — the
    /// <c>GlobalLight</c>'s object ambient and the <c>Dirlight</c>'s front colour — which are constants
    /// of the level for the same reason the fog is, and are baked in for the same reason: there is no
    /// push room. The direction is not a constant of the level in any one draw's space, and it travels
    /// in the push block instead; see <c>IGraphicsDevice.SetVertexSun</c>. A title without the light
    /// leaves all six at zero, and a black light over a black ambient changes nothing.
    /// </remarks>
    public const uint SunAmbientRedConstantId = 6;

    /// <summary>The green channel of the sun's ambient.</summary>
    public const uint SunAmbientGreenConstantId = 7;

    /// <summary>The blue channel of the sun's ambient.</summary>
    public const uint SunAmbientBlueConstantId = 8;

    /// <summary>The red channel of the sun's colour.</summary>
    public const uint SunColourRedConstantId = 9;

    /// <summary>The green channel of the sun's colour.</summary>
    public const uint SunColourGreenConstantId = 10;

    /// <summary>The blue channel of the sun's colour.</summary>
    public const uint SunColourBlueConstantId = 11;

    /// <summary>How many constants <see cref="DescribeMesh"/> writes.</summary>
    public const int MeshConstantCount = 12;

    /// <summary>Fills a description of the forward mesh shader's constants.</summary>
    /// <param name="data">Room for <see cref="MeshConstantCount"/> four-byte values, which must outlive the pipeline creation call.</param>
    /// <param name="entries">Room for <see cref="MeshConstantCount"/> map entries, likewise.</param>
    /// <param name="reference">The cut-out threshold to bake in.</param>
    /// <param name="reflective">Whether the variant samples an environment cube.</param>
    /// <param name="fogColour">The scene's fog colour.</param>
    /// <param name="fogDensity">The scene's fog extinction per metre; zero is no fog.</param>
    /// <param name="sunAmbient">The flat light an unlightmapped vertex receives from the scene sun.</param>
    /// <param name="sunColour">The scene sun's diffuse colour.</param>
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
        float fogDensity,
        Vector3f sunAmbient,
        Vector3f sunColour)
    {
        WriteFloat(data, 0, ToUnit(reference));
        data[1] = reflective ? 1u : 0u;
        WriteFloat(data, 2, fogColour.X);
        WriteFloat(data, 3, fogColour.Y);
        WriteFloat(data, 4, fogColour.Z);
        WriteFloat(data, 5, fogDensity);
        WriteFloat(data, 6, sunAmbient.X);
        WriteFloat(data, 7, sunAmbient.Y);
        WriteFloat(data, 8, sunAmbient.Z);
        WriteFloat(data, 9, sunColour.X);
        WriteFloat(data, 10, sunColour.Y);
        WriteFloat(data, 11, sunColour.Z);

        // The constant ids happen to be the slot numbers, and the map says so entry by entry rather than
        // relying on it: the ids are the shader's contract, the offsets are this function's.
        entries[0] = Entry(ConstantId, 0);
        entries[1] = Entry(ReflectiveConstantId, 1);
        entries[2] = Entry(FogColourRedConstantId, 2);
        entries[3] = Entry(FogColourGreenConstantId, 3);
        entries[4] = Entry(FogColourBlueConstantId, 4);
        entries[5] = Entry(FogDensityConstantId, 5);
        entries[6] = Entry(SunAmbientRedConstantId, 6);
        entries[7] = Entry(SunAmbientGreenConstantId, 7);
        entries[8] = Entry(SunAmbientBlueConstantId, 8);
        entries[9] = Entry(SunColourRedConstantId, 9);
        entries[10] = Entry(SunColourGreenConstantId, 10);
        entries[11] = Entry(SunColourBlueConstantId, 11);

        return new SpecializationInfo
        {
            MapEntryCount = MeshConstantCount,
            PMapEntries = entries,
            DataSize = sizeof(uint) * MeshConstantCount,
            PData = data,
        };
    }

    private static void WriteFloat(uint* data, int slot, float value) => data[slot] = *(uint*)&value;

    private static SpecializationMapEntry Entry(uint constantId, int slot) => new()
    {
        ConstantID = constantId,
        Offset = (uint)(sizeof(uint) * slot),
        Size = sizeof(uint),
    };

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
