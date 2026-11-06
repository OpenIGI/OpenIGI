using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Input;

/// <summary>What is being dragged between widgets: the thing itself and the words shown beside the pointer.</summary>
/// <param name="Payload">The thing dragged; a drop target decides by its type whether to take it.</param>
/// <param name="Label">Shown beside the pointer while dragging.</param>
public sealed record DragDropData(object Payload, string Label);

/// <summary>A widget that things dragged from elsewhere can be dropped on.</summary>
public interface IDropTarget
{
    /// <summary>Gets whether a drag would be taken here.</summary>
    /// <param name="drag">The drag.</param>
    /// <returns><see langword="true"/> when it would.</returns>
    bool CanDrop(DragDropData drag);

    /// <summary>Follows a drag that could be dropped here, so the target can show where it would land.</summary>
    /// <param name="drag">The drag.</param>
    /// <param name="point">The pointer.</param>
    void DragOver(DragDropData drag, UiVec2 point);

    /// <summary>Takes a drop.</summary>
    /// <param name="drag">The drag.</param>
    /// <param name="point">Where it was dropped.</param>
    void Drop(DragDropData drag, UiVec2 point);
}
