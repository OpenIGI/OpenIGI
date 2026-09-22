using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>
/// The constants the forward mesh shader is specialised with, and the identifiers it declares them
/// under.
/// </summary>
/// <remarks>
/// <para>
/// These identifiers are the shader's contract, not this file's convenience: the same numbers appear in
/// the Slang source, and a mismatch is a pipeline that compiles and shades with whatever the default
/// was. Vulkan takes them as specialisation data and WebGPU as pipeline-overridable constants, which is
/// the same idea under the other name — the RHI carries them as a map of identifier to raw bits.
/// </para>
/// <para>
/// The fog and the sun are here rather than in the push block for the same reason the cut-out threshold
/// is: there is no push room left. They are also genuinely constant — a level's fog does not vary
/// within a mission — so a change throws the variants away, which is why it only happens on a load.
/// </para>
/// </remarks>
public static class Mesh3DConstants
{
    /// <summary>The threshold a cut-out fragment must exceed, as a unit value.</summary>
    public const int AlphaReference = 0;

    /// <summary>Whether the variant samples the bound environment cube.</summary>
    public const int Reflective = 1;

    /// <summary>The red channel of the colour a surface tends to with distance.</summary>
    public const int FogColourRed = 2;

    /// <summary>Its green channel.</summary>
    public const int FogColourGreen = 3;

    /// <summary>Its blue channel.</summary>
    public const int FogColourBlue = 4;

    /// <summary>The fog's extinction per metre; zero is no fog.</summary>
    public const int FogDensity = 5;

    /// <summary>The red channel of the flat light an unlightmapped vertex receives.</summary>
    public const int SunAmbientRed = 6;

    /// <summary>Its green channel.</summary>
    public const int SunAmbientGreen = 7;

    /// <summary>Its blue channel.</summary>
    public const int SunAmbientBlue = 8;

    /// <summary>The red channel of the sun's diffuse colour.</summary>
    public const int SunColourRed = 9;

    /// <summary>Its green channel.</summary>
    public const int SunColourGreen = 10;

    /// <summary>Its blue channel.</summary>
    public const int SunColourBlue = 11;

    /// <summary>Whether the target's clip space runs with Y downwards, as Vulkan's does.</summary>
    /// <remarks>
    /// Declared by every vertex stage that turns one of the engine's OpenGL projections into clip
    /// space, which is all of them; see <c>Common/ClipSpace.slang</c>. It defaults to flipping, which
    /// is what the renderer has always done, so only a pipeline drawing through the RHI supplies it.
    /// </remarks>
    public const int ClipYDown = 64;

    /// <summary>The constants every pipeline built for the RHI carries, whatever else it carries.</summary>
    /// <remarks>
    /// One entry, and it is the difference between a frame and the same frame upside down. The RHI
    /// states clip space the way WebGPU does — Y upwards, which is also the way the engine's own
    /// projections run — and its Vulkan implementation reaches that with a flipped viewport. A stage
    /// that flipped as well would undo it.
    /// </remarks>
    public static IReadOnlyDictionary<int, RhiConstant> TargetConstants { get; } =
        new Dictionary<int, RhiConstant> { [ClipYDown] = RhiConstant.Of(0) };

    /// <summary>The threshold a shader falls back to when nothing is supplied.</summary>
    /// <remarks>
    /// <c>128 / 255</c>, which is the <c>0.5</c> the shaders used to test against to within a texel
    /// value. The default is written into the shader as well, so a pipeline built without any constants
    /// draws exactly what this port always drew.
    /// </remarks>
    public const byte DefaultAlphaReference = 128;

    /// <summary>Builds the constants one variant of the forward mesh shader is compiled with.</summary>
    /// <param name="alphaReference">The cut-out threshold out of 255.</param>
    /// <param name="reflective">Whether the variant samples an environment cube.</param>
    /// <param name="fogColour">The scene's fog colour.</param>
    /// <param name="fogDensity">The scene's fog extinction per metre.</param>
    /// <param name="sunAmbient">The flat light an unlightmapped vertex receives.</param>
    /// <param name="sunColour">The scene sun's diffuse colour.</param>
    /// <returns>The constants, by identifier.</returns>
    public static IReadOnlyDictionary<int, RhiConstant> Describe(
        byte alphaReference,
        bool reflective,
        Vector3f fogColour,
        float fogDensity,
        Vector3f sunAmbient,
        Vector3f sunColour) => new Dictionary<int, RhiConstant>
        {
            [ClipYDown] = RhiConstant.Of(0),
            [AlphaReference] = RhiConstant.Of(alphaReference / 255f),
            [Reflective] = RhiConstant.Of(reflective ? 1 : 0),
            [FogColourRed] = RhiConstant.Of(fogColour.X),
            [FogColourGreen] = RhiConstant.Of(fogColour.Y),
            [FogColourBlue] = RhiConstant.Of(fogColour.Z),
            [FogDensity] = RhiConstant.Of(fogDensity),
            [SunAmbientRed] = RhiConstant.Of(sunAmbient.X),
            [SunAmbientGreen] = RhiConstant.Of(sunAmbient.Y),
            [SunAmbientBlue] = RhiConstant.Of(sunAmbient.Z),
            [SunColourRed] = RhiConstant.Of(sunColour.X),
            [SunColourGreen] = RhiConstant.Of(sunColour.Y),
            [SunColourBlue] = RhiConstant.Of(sunColour.Z),
        };

    /// <summary>A float's bits, which is how a constant of any type travels.</summary>
    /// <param name="value">The value.</param>
    /// <returns>Its bits.</returns>
    public static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
}
