using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Host.Editor;

/// <summary>
/// The editor's layer over a title's view culling: what the editor has hidden, or left out of an isolated set, is not drawn;
/// everything else is as the title decides.
/// </summary>
/// <remarks>
/// Hiding is how a deleted object leaves the picture at once and comes back on undo, and how Hide and Isolate work, without
/// the level being rebuilt. Like <see cref="EditorPlacementMotion"/>, it is laid over whatever the title installed and taken
/// off when the editor closes.
/// </remarks>
internal sealed class EditorPlacementVisibility : IPlacementVisibility
{
    private readonly HashSet<PlacementKey> _hidden = [];
    private HashSet<PlacementKey>? _isolated;

    /// <summary>Gets or sets the title's own culling, which decides everything the editor does not.</summary>
    public IPlacementVisibility? Inner { get; set; }

    /// <summary>Gets whether the layer changes anything.</summary>
    public bool IsEmpty => _hidden.Count == 0 && _isolated is null;

    /// <inheritdoc/>
    public bool IsOutsideVisible => Inner?.IsOutsideVisible ?? true;

    /// <inheritdoc/>
    public ViewClip? OutsideClip => Inner?.OutsideClip;

    /// <summary>Hides or shows what a task placed.</summary>
    /// <param name="key">The placements' key.</param>
    /// <param name="hidden">Whether to hide them.</param>
    public void SetHidden(PlacementKey key, bool hidden)
    {
        if (hidden)
        {
            _hidden.Add(key);
        }
        else
        {
            _hidden.Remove(key);
        }
    }

    /// <summary>Draws only what some tasks placed, or everything again.</summary>
    /// <param name="keys">The placements to keep, or <see langword="null"/> to end the isolation.</param>
    public void SetIsolated(IReadOnlyCollection<PlacementKey>? keys) => _isolated = keys is null ? null : [.. keys];

    /// <summary>Forgets everything hidden and isolated.</summary>
    public void Clear()
    {
        _hidden.Clear();
        _isolated = null;
    }

    /// <inheritdoc/>
    public void BeginView(in Camera3D eye, float tangentHalfFovX, float tangentHalfFovY) =>
        Inner?.BeginView(eye, tangentHalfFovX, tangentHalfFovY);

    /// <inheritdoc/>
    public bool IsVisible(WorldPlacement placement) => Shown(placement) && (Inner?.IsVisible(placement) ?? true);

    /// <inheritdoc/>
    public bool IsVisible(WorldPlacement placement, Vector3d drawnAt) => Shown(placement) && (Inner?.IsVisible(placement, drawnAt) ?? true);

    /// <inheritdoc/>
    public ViewClip? ClipOf(WorldPlacement placement) => Inner?.ClipOf(placement);

    /// <inheritdoc/>
    public ViewClip? ClipOf(WorldPlacement placement, Vector3d drawnAt) => Inner?.ClipOf(placement, drawnAt);

    /// <inheritdoc/>
    public bool EnclosesEye(WorldPlacement placement) => Inner?.EnclosesEye(placement) ?? true;

    /// <inheritdoc/>
    public string DescribeView() => Inner?.DescribeView() ?? "everything";

    private bool Shown(WorldPlacement placement)
    {
        if (IsEmpty)
        {
            return true;
        }

        PlacementKey key = PlacementKey.Of(placement);
        return !_hidden.Contains(key) && (_isolated is null || _isolated.Contains(key));
    }
}
