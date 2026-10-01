using OpenIGI.Core;

namespace OpenIGI.Formats.Iff;

/// <summary>
/// One chunk of an EA IFF-85 file: its id, its payload, and — for a group chunk — the form type.
/// </summary>
/// <remarks>
/// <para>
/// This is the container the animation sets use. It is not the
/// same thing as the engine's own <c>ILFF</c> (<c>docs/formats/ilff.md</c>) despite the similar name:
/// IFF sizes are <b>big-endian</b> and exclude the eight-byte header, groups carry a leading form type,
/// and odd-sized chunks are padded to an even boundary.
/// </para>
/// </remarks>
public sealed class IffChunk
{
    /// <summary>The <c>FORM</c> group id.</summary>
    public static readonly FourCc Form = FourCc.FromString("FORM");

    /// <summary>The <c>LIST</c> group id.</summary>
    public static readonly FourCc List = FourCc.FromString("LIST");

    /// <summary>The <c>CAT </c> group id.</summary>
    public static readonly FourCc Catalog = FourCc.FromString("CAT ");

    private readonly List<IffChunk> _children = [];

    internal IffChunk(FourCc id, FourCc formType, ReadOnlyMemory<byte> payload)
    {
        Id = id;
        FormType = formType;
        Payload = payload;
    }

    /// <summary>Gets the chunk id.</summary>
    public FourCc Id { get; }

    /// <summary>Gets the form type of a group chunk, or the default for a leaf.</summary>
    public FourCc FormType { get; }

    /// <summary>Gets whether this is a group chunk (<c>FORM</c>, <c>LIST</c> or <c>CAT </c>).</summary>
    public bool IsGroup => Id == Form || Id == List || Id == Catalog;

    /// <summary>
    /// Gets the chunk's payload — for a group, everything after the four-byte form type.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>Gets the chunks nested inside this group; empty for a leaf.</summary>
    public IReadOnlyList<IffChunk> Children => _children;

    internal void Add(IffChunk child) => _children.Add(child);

    /// <summary>Finds the first immediate child with an id.</summary>
    /// <param name="id">The chunk id to look for.</param>
    /// <returns>The child, or <see langword="null"/> when there is none.</returns>
    public IffChunk? FirstChild(FourCc id)
    {
        foreach (IffChunk child in _children)
        {
            if (child.Id == id)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Finds the first immediate group child with a form type.</summary>
    /// <param name="formType">The form type to look for.</param>
    /// <returns>The group, or <see langword="null"/> when there is none.</returns>
    public IffChunk? FirstGroup(FourCc formType)
    {
        foreach (IffChunk child in _children)
        {
            if (child.IsGroup && child.FormType == formType)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Enumerates the immediate children with an id, in file order.</summary>
    /// <param name="id">The chunk id to look for.</param>
    /// <returns>The matching children.</returns>
    public IEnumerable<IffChunk> ChildrenWithId(FourCc id)
    {
        foreach (IffChunk child in _children)
        {
            if (child.Id == id)
            {
                yield return child;
            }
        }
    }

    /// <summary>Enumerates the immediate group children with a form type, in file order.</summary>
    /// <param name="formType">The form type to look for.</param>
    /// <returns>The matching groups.</returns>
    public IEnumerable<IffChunk> GroupsWithType(FourCc formType)
    {
        foreach (IffChunk child in _children)
        {
            if (child.IsGroup && child.FormType == formType)
            {
                yield return child;
            }
        }
    }

    /// <summary>Gets a required immediate child.</summary>
    /// <param name="id">The chunk id.</param>
    /// <returns>The child.</returns>
    /// <exception cref="IffFormatException">The child is missing.</exception>
    public IffChunk RequireChild(FourCc id) =>
        FirstChild(id) ?? throw new IffFormatException($"Chunk '{Id}' has no '{id}' child.");

    /// <inheritdoc/>
    public override string ToString() =>
        IsGroup ? $"{Id} [{FormType}] ({_children.Count} children)" : $"{Id} ({Payload.Length} bytes)";
}
