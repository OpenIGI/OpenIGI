using OpenIGI.Formats.Ilff;

namespace OpenIGI.Editor.Panels;

/// <summary>One thing the content browser lists: a folder, a file, or an entry inside a <c>.res</c> archive.</summary>
internal sealed class ContentNode
{
    private List<ContentNode>? _children;

    public ContentNode(string name, string path, ContentNode? parent, bool isFolder, IresEntry? entry = null)
    {
        Name = name;
        Path = path;
        Parent = parent;
        IsFolder = isFolder;
        Entry = entry;
    }

    /// <summary>Gets the name shown.</summary>
    public string Name { get; }

    /// <summary>Gets the game path (<c>LOCAL:common/textures</c>); for an archive entry, its archive's path.</summary>
    public string Path { get; }

    /// <summary>Gets the containing node.</summary>
    public ContentNode? Parent { get; }

    /// <summary>Gets whether the node is a folder.</summary>
    public bool IsFolder { get; }

    /// <summary>Gets the archive entry the node is, or <see langword="null"/>.</summary>
    public IresEntry? Entry { get; }

    /// <summary>Gets whether the node is an archive that can be opened like a folder.</summary>
    public bool IsArchive => !IsFolder && Entry is null && Name.EndsWith(".res", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets whether the node can be opened like a folder: a folder or an archive.</summary>
    public bool IsContainer => IsFolder || IsArchive;

    /// <summary>Gets the lower-cased extension of the file or entry.</summary>
    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();

    /// <summary>Forgets the children listed, so they are listed afresh: for a list that changes, such as the favourites.</summary>
    public void Forget() => _children = null;

    /// <summary>Gets the children, listed the first time they are asked for.</summary>
    /// <param name="list">Lists a node's children.</param>
    /// <returns>The children.</returns>
    public IReadOnlyList<ContentNode> Children(Func<ContentNode, List<ContentNode>> list) => _children ??= list(this);
}
