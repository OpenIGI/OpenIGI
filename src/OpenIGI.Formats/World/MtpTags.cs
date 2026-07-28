using OpenIGI.Core;

namespace OpenIGI.Formats.World;

/// <summary>The chunk tags used by <c>.mtp</c> resource packs.</summary>
/// <remarks>
/// Unlike MEF, an <c>.mtp</c> chunk tag reads the same way on disk as in the original's source, because
/// the original's walker byte-swaps the tag it reads (<c>0x4CEAA0</c>). See
/// <c>docs/formats/mtp-dat.md</c>.
/// </remarks>
public static class MtpTags
{
    /// <summary>The EA IFF-85 container magic, <c>FORM</c>.</summary>
    public static FourCc Form { get; } = FourCc.FromString("FORM");

    /// <summary>The form type of a resource pack, <c>MTP </c> — note the trailing space.</summary>
    public static FourCc Mtp { get; } = FourCc.FromString("MTP ");

    /// <summary>Chunk <c>BANM</c>: sound bank names.</summary>
    public static FourCc BankNames { get; } = FourCc.FromString("BANM");

    /// <summary>Chunk <c>SNDS</c>: sound names. Empty in every retail pack.</summary>
    public static FourCc SoundNames { get; } = FourCc.FromString("SNDS");

    /// <summary>Chunk <c>SVOL</c>: names of the dev-only volume model set. Empty in every retail pack.</summary>
    public static FourCc VolumeNames { get; } = FourCc.FromString("SVOL");

    /// <summary>Chunk <c>MODS</c>: model names. This order is the pack's model id space.</summary>
    public static FourCc ModelNames { get; } = FourCc.FromString("MODS");

    /// <summary>Chunk <c>TEXF</c>: texture names.</summary>
    public static FourCc TextureNames { get; } = FourCc.FromString("TEXF");

    /// <summary>Chunk <c>PALF</c>: palette names. Empty in every retail pack.</summary>
    public static FourCc PaletteNames { get; } = FourCc.FromString("PALF");

    /// <summary>Chunk <c>VNAM</c>: instance names, one per <see cref="Instances"/> record.</summary>
    public static FourCc InstanceNames { get; } = FourCc.FromString("VNAM");

    /// <summary>Chunk <c>INST</c>: instance records.</summary>
    public static FourCc Instances { get; } = FourCc.FromString("INST");

    /// <summary>Chunk <c>GTT </c>: the material table — note the trailing space.</summary>
    public static FourCc MaterialTable { get; } = FourCc.FromString("GTT ");
}
