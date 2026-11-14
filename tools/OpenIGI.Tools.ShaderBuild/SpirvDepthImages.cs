using System.Runtime.InteropServices;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Takes the depth flag off every image type of a SPIR-V module, for the GLSL translation.</summary>
/// <remarks>
/// <para>
/// A module that declares <c>DepthTexture2D</c> gets an image type marked depth, and reads it with ordinary
/// samples and loads. SPIR-V allows that. GLSL does not: SPIRV-Cross spells a depth image as a
/// <c>sampler2DShadow</c>, and a <c>texture</c> or <c>textureLod</c> of one with no reference value has no
/// overload, so the module fails to compile in the driver.
/// </para>
/// <para>
/// So the flag is cleared before translation. SPIRV-Cross works out which images really are compared from the
/// instructions that use them (a <c>Dref</c> sample marks its image and sampler), so the shadow lookups still
/// come out as <c>sampler2DShadow</c> and every other depth read becomes a plain <c>sampler2D</c>, which reads
/// depth in its red channel exactly as SPIR-V's own does.
/// </para>
/// <para>
/// Only the bytes handed to SPIRV-Cross change; the committed SPIR-V that Vulkan runs is untouched.
/// </para>
/// </remarks>
public static class SpirvDepthImages
{
    private const uint Magic = 0x07230203;
    private const int OpTypeImage = 25;

    // OpTypeImage: result id, sampled type, dimension, depth, arrayed, multisampled, sampled, format.
    private const int DepthOperand = 4;
    private const uint DepthYes = 1;
    private const uint DepthNo = 0;

    /// <summary>Copies a module with no image type marked depth.</summary>
    /// <param name="spirv">The module.</param>
    /// <returns>The module's words, depth flags cleared.</returns>
    /// <exception cref="ShaderBuildException">The bytes are not a SPIR-V module.</exception>
    public static uint[] Clear(ReadOnlySpan<byte> spirv)
    {
        if (spirv.Length % sizeof(uint) != 0)
        {
            throw new ShaderBuildException("A module the build compiled to SPIR-V is not a whole number of words.");
        }

        uint[] words = MemoryMarshal.Cast<byte, uint>(spirv).ToArray();
        if (words.Length < 5 || words[0] != Magic)
        {
            throw new ShaderBuildException("A module the build compiled to SPIR-V is not SPIR-V.");
        }

        for (int at = 5; at < words.Length;)
        {
            int count = (int)(words[at] >> 16);
            int opcode = (int)(words[at] & 0xFFFF);
            if (count == 0 || at + count > words.Length)
            {
                throw new ShaderBuildException("A SPIR-V module the build compiled is truncated.");
            }

            if (opcode == OpTypeImage && count > DepthOperand && words[at + DepthOperand] == DepthYes)
            {
                words[at + DepthOperand] = DepthNo;
            }

            at += count;
        }

        return words;
    }
}
