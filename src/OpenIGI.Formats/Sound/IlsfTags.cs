using OpenIGI.Core;

namespace OpenIGI.Formats.Sound;

/// <summary>The four-character tags used by ILSF sounds.</summary>
/// <remarks>Values are in file order. See the original for the loader anchors.</remarks>
public static class IlsfTags
{
    /// <summary>The sound magic, <c>ILSF</c>, tested by the original loader at <c>0x495F70</c>.</summary>
    public static FourCc Ilsf { get; } = FourCc.FromString("ILSF");
}
