namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Reads each specialisation constant's default out of a SPIR-V module.</summary>
/// <remarks>
/// <para>
/// A pipeline supplies only the constants it means to change; every other one keeps the value the source
/// declared. Vulkan and WebGPU know those defaults from the module itself. HLSL has no specialisation
/// constants at all -- Slang gathers them into a constant buffer the Direct3D 12 backend fills -- so the
/// defaults have to be written down, or every constant a pipeline left alone would read nought there.
/// </para>
/// <para>
/// Slang's reflection does not report them, so they are read from the SPIR-V: the <c>SpecId</c> decorations
/// name each constant, and its <c>OpSpecConstant</c>, <c>OpSpecConstantTrue</c> or <c>OpSpecConstantFalse</c>
/// carries the value. Every constant the library declares is thirty-two bits or a boolean.
/// </para>
/// </remarks>
public static class SpirvSpecDefaults
{
    private const uint Magic = 0x07230203;
    private const int OpDecorate = 71;
    private const int OpSpecConstantTrue = 48;
    private const int OpSpecConstantFalse = 49;
    private const int OpSpecConstant = 50;
    private const int DecorationSpecId = 1;

    /// <summary>Reads the defaults.</summary>
    /// <param name="spirv">The module.</param>
    /// <returns>Each constant's default as thirty-two bits, keyed by its <c>SpecId</c>.</returns>
    /// <exception cref="ShaderBuildException">The bytes are not a SPIR-V module.</exception>
    public static IReadOnlyDictionary<int, uint> Read(ReadOnlySpan<byte> spirv)
    {
        ReadOnlySpan<uint> words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(spirv);
        if (words.Length < 5 || words[0] != Magic)
        {
            throw new ShaderBuildException("A module the build compiled to SPIR-V is not SPIR-V.");
        }

        Dictionary<uint, int> specIds = [];
        Dictionary<uint, uint> values = [];

        for (int at = 5; at < words.Length;)
        {
            int count = (int)(words[at] >> 16);
            int opcode = (int)(words[at] & 0xFFFF);
            if (count == 0 || at + count > words.Length)
            {
                throw new ShaderBuildException("A SPIR-V module the build compiled is truncated.");
            }

            switch (opcode)
            {
                case OpDecorate when count >= 4 && words[at + 2] == DecorationSpecId:
                    specIds[words[at + 1]] = (int)words[at + 3];
                    break;
                case OpSpecConstantTrue:
                    values[words[at + 2]] = 1;
                    break;
                case OpSpecConstantFalse:
                    values[words[at + 2]] = 0;
                    break;
                case OpSpecConstant when count >= 4:
                    values[words[at + 2]] = words[at + 3];
                    break;
                default:
                    break;
            }

            at += count;
        }

        Dictionary<int, uint> defaults = [];
        foreach ((uint result, int specId) in specIds)
        {
            if (values.TryGetValue(result, out uint value))
            {
                defaults[specId] = value;
            }
        }

        return defaults;
    }
}
