using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>
/// One node of the object tree a placement expands into: a LOD chain, where it sits, and the children its
/// model attaches.
/// </summary>
/// <remarks>
/// <para>
/// The original calls these sector objects. A placement supplies the root; every further node comes from
/// an <see cref="MefAttachment"/> on the node above it, resolved through the
/// <see cref="MagicObjectRegistry"/> (<c>0x4DFA50</c>). Attachments are read from the most detailed model
/// only — the original passes <c>*model</c>, which is level 0.
/// </para>
/// <para>
/// <see cref="LightmapIndex"/> is why the tree is built at all: the baked lightmaps of a whole placement
/// are numbered by walking this tree depth first and counting one per level of detail, so a node's index
/// cannot be known without every node before it.
/// </para>
/// </remarks>
public sealed class WorldObject
{
    /// <summary>The model the original substitutes for one it cannot find (<c>0x4CEC10</c>).</summary>
    public const string MissingModelName = "missing";

    private readonly List<WorldObject> _children = [];

    private WorldObject(string name, LodModelChain models, Vector3f position, Matrix3f orientation, int lightmapIndex)
    {
        Name = name;
        Models = models;
        Position = position;
        Orientation = orientation;
        LightmapIndex = lightmapIndex;
    }

    /// <summary>Gets the model name this node was resolved from.</summary>
    public string Name { get; }

    /// <summary>Gets the node's detail levels.</summary>
    public LodModelChain Models { get; }

    /// <summary>Gets the node's position in its parent's space; the root sits at the origin.</summary>
    public Vector3f Position { get; }

    /// <summary>Gets the node's orientation in its parent's space.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>
    /// Gets the lightmap index of this node's most detailed model; level <c>n</c> is at
    /// <c>LightmapIndex + n</c>.
    /// </summary>
    public int LightmapIndex { get; }

    /// <summary>Gets the children this node's model attaches, in attachment order.</summary>
    public IReadOnlyList<WorldObject> Children => _children;

    /// <summary>Gets the number of lightmaps this node and everything under it accounts for.</summary>
    public int LightmapCount
    {
        get
        {
            int total = Models.Count;
            foreach (WorldObject child in _children)
            {
                total += child.LightmapCount;
            }

            return total;
        }
    }

    /// <summary>Expands a placement's model into its tree of attached objects.</summary>
    /// <param name="modelName">The placement's model name.</param>
    /// <param name="resolve">Looks one model name up.</param>
    /// <param name="magic">The magic-object table an attachment name is resolved through.</param>
    /// <param name="root">Receives the tree when the model resolves.</param>
    /// <returns><see langword="false"/> when the placement's own model does not resolve.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static bool TryBuild(string modelName, ModelResolver resolve, MagicObjectRegistry magic, out WorldObject? root)
    {
        ArgumentNullException.ThrowIfNull(modelName);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(magic);

        int next = 0;
        root = Build(modelName, Vector3f.Zero, Matrix3f.Identity, resolve, magic, ref next, depth: 0);
        return root is not null;
    }

    /// <summary>
    /// Expands every model a lightmap owner places into one tree apiece, numbering them all from a single
    /// running count.
    /// </summary>
    /// <param name="modelNames">The owner's model names, in the order its task subtree places them.</param>
    /// <param name="resolve">Looks one model name up.</param>
    /// <param name="magic">The magic-object table an attachment name is resolved through.</param>
    /// <returns>
    /// One entry per input name, in the same order and sharing one lightmap numbering, with
    /// <see langword="null"/> where a name did not resolve. The alignment is deliberate: the caller knows
    /// which task each name came from and needs to keep that link.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The count runs across the whole owner, not per placement: an object's baked lightmaps are one set,
    /// so the second placement's first model carries on where the first placement's tree left off.
    /// </remarks>
    public static IReadOnlyList<WorldObject?> BuildAll(
        IEnumerable<string> modelNames,
        ModelResolver resolve,
        MagicObjectRegistry magic)
    {
        ArgumentNullException.ThrowIfNull(modelNames);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(magic);

        List<WorldObject?> roots = [];
        int next = 0;
        foreach (string name in modelNames)
        {
            roots.Add(Build(name, Vector3f.Zero, Matrix3f.Identity, resolve, magic, ref next, depth: 0));
        }

        return roots;
    }

    private static WorldObject? Build(
        string modelName,
        Vector3f position,
        Matrix3f orientation,
        ModelResolver resolve,
        MagicObjectRegistry magic,
        ref int next,
        int depth)
    {
        // Attachments can name an object whose model attaches it straight back. The original has no guard,
        // because its data has no such cycle; recursing forever on a modified file is not worth inheriting.
        const int maxDepth = 16;
        if (depth > maxDepth || !LodModelChain.TryResolve(modelName, resolve, out LodModelChain? models) || models is null)
        {
            return null;
        }

        // The index is claimed before the children are walked, which is what makes the numbering
        // depth-first: a node owns one lightmap per level of detail, then its subtree follows.
        WorldObject node = new(modelName, models, position, orientation, next);
        next += models.Count;

        // Attachments whose model encloses space become sectors, which the original puts on a separate
        // child list that it walks only after the ordinary one. Both keep attachment order within
        // themselves. Getting this wrong shifts every lightmap index from the first sector onwards.
        List<(MefAttachment Attachment, string Model)> ordinary = [];
        List<(MefAttachment Attachment, string Model)> sectors = [];
        foreach (MefAttachment attachment in models.Highest.Attachments)
        {
            if (!magic.TryGet(attachment.Name, out MagicObjectDefinition definition)
                || !TryResolveChild(definition.Model, resolve, out string model, out MefModel? head)
                || head is null)
            {
                continue;
            }

            (head.IsSector ? sectors : ordinary).Add((attachment, model));
        }

        foreach ((MefAttachment attachment, string model) in ordinary.Concat(sectors))
        {
            WorldObject? child = Build(
                model,
                attachment.Position,
                attachment.Orientation,
                resolve,
                magic,
                ref next,
                depth + 1);

            if (child is not null)
            {
                node._children.Add(child);
            }
        }

        return node;
    }

    /// <summary>
    /// Resolves a magic object's model, standing in <see cref="MissingModelName"/> when the named one is
    /// absent, the way <c>0x4CEC10</c> does.
    /// </summary>
    /// <remarks>
    /// This is not error handling on our part — it is the original's, and it changes the answer. A
    /// substituted object is still an object: it occupies its place in the tree and takes its lightmap
    /// indices with it, so dropping it instead would shift every index after it.
    /// </remarks>
    private static bool TryResolveChild(string name, ModelResolver resolve, out string model, out MefModel? head)
    {
        model = name;
        if (resolve(name, out head) && head is not null)
        {
            return true;
        }

        if (name.Length == 0)
        {
            return false;
        }

        model = MissingModelName;
        return resolve(MissingModelName, out head) && head is not null;
    }

    /// <summary>Walks this node and everything under it, depth first, in lightmap-index order.</summary>
    /// <returns>The nodes.</returns>
    public IEnumerable<WorldObject> Walk()
    {
        yield return this;
        foreach (WorldObject child in _children)
        {
            foreach (WorldObject descendant in child.Walk())
            {
                yield return descendant;
            }
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} @{LightmapIndex} ({_children.Count} attached)";
}
