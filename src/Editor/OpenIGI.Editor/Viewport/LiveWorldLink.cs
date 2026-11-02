using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// Keeps the running level's picture in step with the document: a task moved in the editor is drawn where it now is,
/// from the next frame, without reloading the level.
/// </summary>
/// <remarks>
/// <para>
/// The document and the running level are two trees built from the same script, so each document task with a place
/// is matched to the running task that owns placements: the same type and label, and the placement nearest the
/// task's position as loaded. From then on a position edit — a gizmo drag, a typed value, an undo — becomes an
/// offset the renderer applies to everything that task placed.
/// </para>
/// <para>
/// Only the picture follows. Collision, lighting and AI read the level as it was built, and catch up when the level
/// is reloaded from what was saved.
/// </para>
/// </remarks>
internal sealed class LiveWorldLink(IEditorHost host, MissionWorkspace workspace)
{
    private const double MatchToleranceMetres = 0.05;

    private readonly Dictionary<MissionTask, (Vector3d Loaded, GameTask Owner)> _links = new(ReferenceEqualityComparer.Instance);
    private MissionDocument? _bound;
    private IReadOnlyList<WorldPlacement>? _boundPlacements;

    /// <summary>Gets how many document tasks are linked to the running level.</summary>
    public int LinkedCount => _links.Count;

    /// <summary>Gets the radius, in metres, of everything a task placed, measured from the task's position.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The radius, or <see langword="null"/> when the task is not linked to anything placed.</returns>
    public double? RadiusMetres(MissionTask task)
    {
        if (!_links.TryGetValue(task, out (Vector3d Loaded, GameTask Owner) link) || _boundPlacements is not { } placements)
        {
            return null;
        }

        double radius = 0.0;
        foreach (WorldPlacement placement in placements)
        {
            if (ReferenceEquals(placement.Owner, link.Owner))
            {
                // The model's sphere is in world units; the placement's position is in metres.
                double reach = (placement.Position - link.Loaded).Length + WorldUnits.ToMetres(placement.Model.RenderSphereRadius);
                radius = Math.Max(radius, reach);
            }
        }

        return radius > 0.0 ? radius : null;
    }

    /// <summary>Binds to a newly opened document or rebuilt level. Called once a frame.</summary>
    public void Update()
    {
        MissionDocument? document = workspace.Document;
        IReadOnlyList<WorldPlacement> placements = host.Placements;
        if (ReferenceEquals(document, _bound) && ReferenceEquals(placements, _boundPlacements))
        {
            return;
        }

        if (_bound is not null)
        {
            _bound.Changed -= OnChanged;
        }

        _bound = document;
        _boundPlacements = placements;
        _links.Clear();
        if (document is null || placements.Count == 0)
        {
            return;
        }

        document.Changed += OnChanged;
        Bind(document, placements);
    }


    private void Bind(MissionDocument document, IReadOnlyList<WorldPlacement> placements)
    {
        // Every running owner, with the placement it put nearest its own spot, grouped by type and label.
        Dictionary<(string Type, string Label), List<(GameTask Owner, Vector3d At)>> owners = [];
        HashSet<GameTask> seen = new(ReferenceEqualityComparer.Instance);
        foreach (WorldPlacement placement in placements)
        {
            if (!seen.Add(placement.Owner))
            {
                continue;
            }

            (string, string) key = (placement.Owner.Type.Name, placement.Owner.Label);
            if (!owners.TryGetValue(key, out List<(GameTask Owner, Vector3d At)>? list))
            {
                owners[key] = list = [];
            }

            list.Add((placement.Owner, placement.Position));
        }

        foreach (MissionTask task in document.EnumerateTasks())
        {
            if (TaskPresentation.PositionMetres(task) is not { } loaded
                || !owners.TryGetValue((task.TypeName, task.Label), out List<(GameTask Owner, Vector3d At)>? candidates)
                || candidates.Count == 0)
            {
                continue;
            }

            (GameTask Owner, Vector3d At) best = candidates.MinBy(c => (c.At - loaded).LengthSquared);
            if ((best.At - loaded).Length <= MatchToleranceMetres)
            {
                _links[task] = (loaded, best.Owner);
                candidates.Remove(best);
            }
        }
    }

    private void OnChanged(MissionChange change)
    {
        if (change.Kind != MissionChangeKind.Parameter || !_links.TryGetValue(change.Task, out (Vector3d Loaded, GameTask Owner) link))
        {
            return;
        }

        if (TaskPresentation.PositionMetres(change.Task) is { } now)
        {
            host.SetPlacementOffset(link.Owner, now - link.Loaded);
        }
    }
}
