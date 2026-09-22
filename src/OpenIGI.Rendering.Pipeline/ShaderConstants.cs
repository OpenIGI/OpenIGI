using OpenIGI.Rendering.Rhi;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>
/// Keeps a set of specialisation constants down to the ones a module actually declares.
/// </summary>
/// <remarks>
/// <para>
/// A constant a shader no longer reads is easy to leave behind: the value is still computed, still
/// handed over, and on Vulkan still silently ignored — an unknown identifier in the specialisation data
/// costs nothing and says nothing. WebGPU refuses it outright, because an override is set by the
/// identifier the module declared and one it never declared cannot be set at all.
/// </para>
/// <para>
/// So the two backends disagree about a mistake neither of them causes, and the disagreement surfaces
/// only where a frame is drawn on both. Filtering here removes it at the source: what is supplied is
/// what the module says it takes, and a constant that quietly stopped existing stops being sent.
/// </para>
/// </remarks>
public static class ShaderConstants
{
    /// <summary>Keeps only the constants a module declares.</summary>
    /// <param name="reflection">What the module declares.</param>
    /// <param name="constants">The constants a caller wants to fix.</param>
    /// <returns>Those of them the module has an identifier for.</returns>
    public static IReadOnlyDictionary<int, RhiConstant> Declared(
        ShaderReflection reflection,
        IReadOnlyDictionary<int, RhiConstant> constants)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        ArgumentNullException.ThrowIfNull(constants);

        HashSet<int> declared = [];

        foreach (ShaderSpecializationConstant constant in reflection.SpecializationConstants)
        {
            declared.Add(constant.Id);
        }

        Dictionary<int, RhiConstant> kept = [];

        foreach ((int id, RhiConstant value) in constants)
        {
            if (declared.Contains(id))
            {
                kept[id] = value;
            }
        }

        return kept;
    }
}
