using OpenIGI.Core;

namespace OpenIGI.Formats.Ilff;

/// <summary>The four-character tags used by ILFF containers.</summary>
/// <remarks>
/// Values are in file order. See <c>docs/formats/ilff.md</c> for the container layout.
/// </remarks>
public static class IlffTags
{
    /// <summary>The container magic, <c>ILFF</c>, which also identifies a nested container in a chunk list.</summary>
    public static FourCc Ilff { get; } = FourCc.FromString("ILFF");

    /// <summary>Form type <c>IRES</c>: a resource archive of name/payload pairs.</summary>
    public static FourCc Ires { get; } = FourCc.FromString("IRES");

    /// <summary>Form type <c>OCEM</c>: an MEF model (the logical name is <c>MECO</c>).</summary>
    public static FourCc Ocem { get; } = FourCc.FromString("OCEM");

    /// <summary>Form type <c>MINA</c>: a bone animation (the logical name is <c>ANIM</c>).</summary>
    /// <remarks>
    /// The sequel keeps its animations inside a pack's model archive rather than beside it as loose
    /// <c>.iff</c> files, so a models archive holds both forms. An animation is not a malformed model,
    /// and reading it as one is how several hundred of them came to be reported as broken.
    /// </remarks>
    public static FourCc Mina { get; } = FourCc.FromString("MINA");

    /// <summary>Form type <c>FONT</c>: a bitmap font.</summary>
    public static FourCc Font { get; } = FourCc.FromString("FONT");

    /// <summary>Chunk <c>NAME</c>: sets the name used by the chunks that follow.</summary>
    public static FourCc Name { get; } = FourCc.FromString("NAME");

    /// <summary>Chunk <c>BODY</c>: the payload of the current name.</summary>
    public static FourCc Body { get; } = FourCc.FromString("BODY");

    /// <summary>Chunk <c>PATH</c>: a path node registered under the current name.</summary>
    public static FourCc Path { get; } = FourCc.FromString("PATH");

    /// <summary>Chunk <c>CSTR</c>: a context string attached to the current resource.</summary>
    public static FourCc Cstr { get; } = FourCc.FromString("CSTR");

    /// <summary>Chunk <c>DBUG</c>: a debug sub-form, which is itself an <c>ILFF</c>+<c>DBUG</c> container.</summary>
    public static FourCc Dbug { get; } = FourCc.FromString("DBUG");
}
