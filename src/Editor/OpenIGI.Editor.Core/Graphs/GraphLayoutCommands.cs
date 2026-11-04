using OpenIGI.Editor.Core.Commands;

namespace OpenIGI.Editor.Core.Graphs;

/// <summary>Moves nodes on the canvas; a drag of the same nodes merges into one step.</summary>
/// <param name="layout">The layout.</param>
/// <param name="moves">Each node's position before and after.</param>
public sealed class MoveNodesCommand(GraphLayout layout, IReadOnlyDictionary<GraphNodeKey, (GraphPoint From, GraphPoint To)> moves) : IEditorCommand
{
    private Dictionary<GraphNodeKey, (GraphPoint From, GraphPoint To)> _moves = new(moves);

    /// <inheritdoc/>
    public string Name => _moves.Count == 1 ? "Move node" : "Move nodes";

    /// <summary>Gets each node's position before and after.</summary>
    public IReadOnlyDictionary<GraphNodeKey, (GraphPoint From, GraphPoint To)> Moves => _moves;

    /// <inheritdoc/>
    public void Apply() => Set(static m => m.To);

    /// <inheritdoc/>
    public void Undo() => Set(static m => m.From);

    /// <inheritdoc/>
    public bool CanMerge(IEditorCommand following) =>
        following is MoveNodesCommand next && next._moves.Count == _moves.Count && next._moves.Keys.All(_moves.ContainsKey);

    /// <inheritdoc/>
    public bool TryMerge(IEditorCommand following)
    {
        if (!CanMerge(following))
        {
            return false;
        }

        MoveNodesCommand next = (MoveNodesCommand)following;
        _moves = _moves.ToDictionary(static m => m.Key, m => (m.Value.From, next._moves[m.Key].To));
        return true;
    }

    private void Set(Func<(GraphPoint From, GraphPoint To), GraphPoint> pick)
    {
        foreach ((GraphNodeKey key, (GraphPoint From, GraphPoint To) move) in _moves)
        {
            layout.Positions[key] = pick(move);
        }

        layout.NotifyChanged();
    }
}

/// <summary>Adds, changes or removes a comment: whichever the before and after say.</summary>
/// <param name="layout">The layout.</param>
/// <param name="before">The comment before, or <see langword="null"/> to add one.</param>
/// <param name="after">The comment after, or <see langword="null"/> to remove it.</param>
public sealed class SetCommentCommand(GraphLayout layout, GraphComment? before, GraphComment? after) : IEditorCommand
{
    /// <inheritdoc/>
    public string Name => (before, after) switch
    {
        (null, _) => "Add comment",
        (_, null) => "Delete comment",
        _ => "Edit comment",
    };

    /// <inheritdoc/>
    public void Apply() => Replace(before, after);

    /// <inheritdoc/>
    public void Undo() => Replace(after, before);

    /// <inheritdoc/>
    /// <remarks>Successive edits of one comment (a drag, a typed title) merge into one step.</remarks>
    public bool CanMerge(IEditorCommand following) =>
        following is SetCommentCommand next && before is not null && after is not null
        && next.Before?.Id == after.Id && next.After?.Id == after.Id;

    /// <inheritdoc/>
    public bool TryMerge(IEditorCommand following)
    {
        if (!CanMerge(following))
        {
            return false;
        }

        after = ((SetCommentCommand)following).After;
        return true;
    }

    private GraphComment? Before => before;

    private GraphComment? After => after;

    private void Replace(GraphComment? remove, GraphComment? add)
    {
        int at = remove is null ? -1 : layout.Comments.FindIndex(c => c.Id == remove.Id);
        if (at >= 0)
        {
            layout.Comments.RemoveAt(at);
        }

        if (add is not null)
        {
            layout.Comments.Insert(at >= 0 ? at : layout.Comments.Count, add);
        }

        layout.NotifyChanged();
    }
}

/// <summary>Sizes a frame by hand, or gives it back to its contents (a <see langword="null"/> size).</summary>
/// <param name="layout">The layout.</param>
/// <param name="frame">The frame's container task.</param>
/// <param name="size">The new size, or <see langword="null"/> to fit the contents.</param>
public sealed class ResizeFrameCommand(GraphLayout layout, GraphNodeKey frame, GraphPoint? size) : IEditorCommand
{
    private GraphPoint? _before;

    /// <inheritdoc/>
    public string Name => "Resize frame";

    /// <inheritdoc/>
    public void Apply()
    {
        _before = layout.FrameSizes.TryGetValue(frame, out GraphPoint was) ? was : null;
        Set(size);
    }

    /// <inheritdoc/>
    public void Undo() => Set(_before);

    /// <inheritdoc/>
    public bool CanMerge(IEditorCommand following) => following is ResizeFrameCommand next && next.Frame == Frame;

    /// <inheritdoc/>
    public bool TryMerge(IEditorCommand following)
    {
        if (!CanMerge(following))
        {
            return false;
        }

        size = ((ResizeFrameCommand)following).NewSize;
        return true;
    }

    private GraphNodeKey Frame => frame;

    private GraphPoint? NewSize => size;

    private void Set(GraphPoint? value)
    {
        if (value is { } given)
        {
            layout.FrameSizes[frame] = given;
        }
        else
        {
            layout.FrameSizes.Remove(frame);
        }

        layout.NotifyChanged();
    }
}

/// <summary>Folds nodes to their title, or unfolds them.</summary>
/// <param name="layout">The layout.</param>
/// <param name="nodes">The nodes.</param>
/// <param name="collapsed">Whether they end folded.</param>
public sealed class CollapseNodesCommand(GraphLayout layout, IReadOnlyList<GraphNodeKey> nodes, bool collapsed) : IEditorCommand
{
    private readonly List<GraphNodeKey> _changed = [];

    /// <inheritdoc/>
    public string Name => collapsed ? "Collapse" : "Expand";

    /// <inheritdoc/>
    public void Apply()
    {
        _changed.Clear();
        foreach (GraphNodeKey key in nodes)
        {
            if (collapsed ? layout.Collapsed.Add(key) : layout.Collapsed.Remove(key))
            {
                _changed.Add(key);
            }
        }

        layout.NotifyChanged();
    }

    /// <inheritdoc/>
    public void Undo()
    {
        foreach (GraphNodeKey key in _changed)
        {
            _ = collapsed ? layout.Collapsed.Remove(key) : layout.Collapsed.Add(key);
        }

        layout.NotifyChanged();
    }
}

/// <summary>Sets the reroute points a wire runs through; an empty list straightens it again.</summary>
/// <param name="layout">The layout.</param>
/// <param name="wire">The wire's <see cref="GraphLayout.WireKey"/>.</param>
/// <param name="points">Its points, in canvas units and in order from the output.</param>
public sealed class SetRerouteCommand(GraphLayout layout, string wire, IReadOnlyList<GraphPoint> points) : IEditorCommand
{
    private List<GraphPoint>? _before;

    /// <inheritdoc/>
    public string Name => points.Count == 0 ? "Straighten wire" : "Reroute wire";

    /// <inheritdoc/>
    public void Apply()
    {
        _before = layout.Reroutes.TryGetValue(wire, out List<GraphPoint>? was) ? [.. was] : null;
        Set(points);
    }

    /// <inheritdoc/>
    public void Undo() => Set(_before);

    private void Set(IReadOnlyList<GraphPoint>? to)
    {
        if (to is null || to.Count == 0)
        {
            layout.Reroutes.Remove(wire);
        }
        else
        {
            layout.Reroutes[wire] = [.. to];
        }

        layout.NotifyChanged();
    }
}
