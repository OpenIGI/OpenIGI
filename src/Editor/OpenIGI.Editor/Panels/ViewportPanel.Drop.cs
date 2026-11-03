using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Host.Editor;
using OpenIGI.UI;
using OpenIGI.UI.Editor.Input;
using OpenIGI.UI.Layout;

namespace OpenIGI.Editor.Panels;

/// <summary>
/// Models dragged in from the content browser: drawn where they would stand while the drag is over the view, and placed
/// there when let go, standing on the ground across their whole footprint.
/// </summary>
internal sealed partial class ViewportPanel : IDropTarget
{
    // How far a dropped surface may be from the terrain and still count as standing on it, rather than on an object.
    private const double OnGroundMetres = 0.25;

    // Samples across a footprint, a side.
    private const int FootprintSamples = 5;

    // Where a dragged model would stand, while one is over the view, and the pointer it was found for. Finding it casts into
    // the scene, so it is done once a frame when drawn rather than for every move the pointer makes.
    private Vector3d? _dropAt;
    private UiVec2? _dropPointer;
    private UiVec2? _dropFoundFor;
    private DragGhost? _ghost;

    // A drag a script holds over the view (editor drag), standing in for the pointer's.
    private (DragDropData Drag, UiVec2 Point)? _scriptedDrag;

    // The ghosts drawn so far, one a model, kept hidden between drags and reused rather than drawn again each time; they are
    // only pictures, and go when the level is reloaded.
    private readonly Dictionary<string, (PlacementKey Key, Vector3d Spawned)> _ghosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when a model file is dropped on the world, with the file and where its origin goes, in metres.</summary>
    public event Action<ContentNode, Vector3d>? ModelDropped;

    /// <inheritdoc/>
    public bool CanDrop(DragDropData drag) =>
        drag.Payload is ContentNode { Extension: ".mef", IsFolder: false } && _workspace.Document is not null && WorldRect.Contains(Root?.PointerPosition ?? default);

    /// <inheritdoc/>
    public void DragOver(DragDropData drag, UiVec2 point)
    {
        _dropPointer = point;
        if (drag.Payload is ContentNode node && _ghost?.Node != node)
        {
            EndGhost();
            _ghost = new DragGhost(node, Path.GetFileNameWithoutExtension(node.Name), Footprint.Read(_host, node));
        }
    }

    /// <inheritdoc/>
    public void Drop(DragDropData drag, UiVec2 point)
    {
        if (_dropFoundFor != point && drag.Payload is ContentNode dropped && (_ghost?.Node != dropped))
        {
            _ghost = new DragGhost(dropped, Path.GetFileNameWithoutExtension(dropped.Name), Footprint.Read(_host, dropped));
        }

        // Where the ghost was last drawn, so the object lands where it was seen; worked out afresh only for a drop no frame drew.
        Vector3d? found = _dropAt ?? Standing(point);
        EndGhost();
        _dropAt = null;
        _dropPointer = null;
        _dropFoundFor = null;
        if (drag.Payload is ContentNode node && found is { } at)
        {
            ModelDropped?.Invoke(node, at);
        }
    }

    /// <summary>Holds a file over a point of the view as a drag would, for scripts; null lets it go without dropping.</summary>
    /// <param name="file">The file, or null to end the drag.</param>
    /// <param name="fraction">Where over the view, as fractions of its width and height.</param>
    /// <param name="drop">Whether to drop it there rather than keep holding it.</param>
    internal void ScriptDrag(ContentNode? file, UiVec2 fraction, bool drop = false)
    {
        if (file is null)
        {
            _scriptedDrag = null;
            return;
        }

        UiRect view = WorldRect;
        UiVec2 point = new(view.X + (view.Width * fraction.X), view.Y + (view.Height * fraction.Y));
        DragDropData drag = new(file, file.Name);
        if (drop)
        {
            _scriptedDrag = null;
            Drop(drag, point);
            return;
        }

        _scriptedDrag = (drag, point);
        DragOver(drag, point);
    }

    // What is under the pointer, or a point a little in front of the camera along the pointer's ray; never below the ground.
    private Vector3d? DropPoint(UiVec2 point)
    {
        Vector3d? at = SurfaceUnder(point, null)
            ?? (Projector.TryRay(point, out Vector3d ray) ? Projector.Eye + (ray * Math.Min(_camera.PivotDistance, 30.0)) : null);
        return at is { } p && GroundAt(p.X, p.Y, p.Z) is { } ground && p.Z < ground
            ? new Vector3d(p.X, p.Y, ground)
            : at;
    }

    // Where the dragged model's origin goes for a pointer: its base on the surface under the pointer. On open ground the base
    // goes down to the lowest ground under its footprint, so no edge floats over a dip and the rest settles into the slope, as
    // the original missions sit their buildings; on an object (a roof, a floor) it stands on that.
    private Vector3d? Standing(UiVec2 point)
    {
        if (DropPoint(point) is not { } surface)
        {
            return null;
        }

        if (_ghost?.Footprint is not { } footprint)
        {
            return surface;
        }

        double baseZ = surface.Z;
        if (GroundAt(surface.X, surface.Y, surface.Z) is { } ground && Math.Abs(surface.Z - ground) <= OnGroundMetres)
        {
            for (int j = 0; j < FootprintSamples; j++)
            {
                for (int i = 0; i < FootprintSamples; i++)
                {
                    double x = surface.X + footprint.MinX + ((footprint.MaxX - footprint.MinX) * i / (FootprintSamples - 1));
                    double y = surface.Y + footprint.MinY + ((footprint.MaxY - footprint.MinY) * j / (FootprintSamples - 1));
                    if (GroundAt(x, y, surface.Z) is { } under)
                    {
                        baseZ = Math.Min(baseZ, under);
                    }
                }
            }
        }

        return new Vector3d(surface.X, surface.Y, baseZ - footprint.MinZ);
    }

    // The terrain's height under a point, found by a short cast down through it from well above.
    private double? GroundAt(double x, double y, double nearZ)
    {
        const double Above = 200.0;
        return _host.Terrain is { } terrain && terrain.TryRaycast(new Vector3d(x, y, nearZ + Above), new Vector3d(0, 0, -1), Above * 2.0, out Vector3d hit)
            ? hit.Z
            : null;
    }

    // The dragged model drawn where it would stand, and a ring at its base.
    private void DrawDropMarker(UiPainter painter)
    {
        // The pointer is the dragging panel's while the drag lasts, so the view is never "hovered": where the pointer is says
        // whether the drag is over it.
        DragDropData? held = Root?.Dragging ?? _scriptedDrag?.Drag;
        UiVec2 pointer = _scriptedDrag?.Point ?? Root?.PointerPosition ?? default;
        if (held is not { } drag || !WorldRect.Contains(pointer) || drag.Payload is not ContentNode { Extension: ".mef", IsFolder: false } || _workspace.Document is null)
        {
            EndGhost();
            _dropAt = null;
            _dropPointer = null;
            _dropFoundFor = null;
            return;
        }

        if (_dropPointer is { } over && over != _dropFoundFor)
        {
            _dropAt = Standing(over);
            _dropFoundFor = over;
            MoveGhost();
        }

        if (_dropAt is { } at && Projector.TryProject(at + new Vector3d(0, 0, _ghost?.Footprint?.MinZ ?? 0.0), out UiVec2 screen))
        {
            painter.FillArc(screen, Theme.Px(9f), Theme.Px(11f), 0f, MathF.Tau, Theme.Accent);
            painter.FillDot(screen, Theme.Px(3f), Bgra32.White);
        }
    }

    // The ghost drawn at the drop point: made the first time there is one, moved after.
    private void MoveGhost()
    {
        if (_ghost is not { } ghost || _dropAt is not { } at)
        {
            return;
        }

        if (ghost.Key is null && !ghost.Failed)
        {
            if (_ghosts.TryGetValue(ghost.Model, out (PlacementKey Key, Vector3d Spawned) kept) && _host.Placements.Any(p => PlacementKey.Of(p) == kept.Key))
            {
                (ghost.Key, ghost.Spawned) = kept;
            }
            else
            {
                ghost.Key = _host.SpawnPlacement(ghost.Model, at, Matrix3f.Identity);
                ghost.Spawned = at;
                ghost.Failed = ghost.Key is null;
                if (ghost.Key is { } made)
                {
                    _ghosts[ghost.Model] = (made, at);
                }

                return;
            }
        }

        if (ghost.Key is { } key)
        {
            _host.SetPlacementHidden(key, hidden: false);
            _host.SetPlacementPose(key, new EditorPose(ghost.Spawned, at - ghost.Spawned, Matrix3f.Identity));
        }
    }

    // The ghost put away when the drag leaves, ends or is dropped; the placed object is drawn by the live link instead.
    private void EndGhost()
    {
        if (_ghost?.Key is { } key)
        {
            _host.SetPlacementHidden(key, hidden: true);
        }

        _ghost = null;
    }

    // A model being dragged: its file, its name, its extent and, once drawn, the placement that shows it.
    private sealed class DragGhost(ContentNode node, string model, Footprint? footprint)
    {
        public ContentNode Node { get; } = node;

        public string Model { get; } = model;

        public Footprint? Footprint { get; } = footprint;

        public PlacementKey? Key { get; set; }

        public Vector3d Spawned { get; set; }

        public bool Failed { get; set; }
    }
}

/// <summary>A model's extent about its origin, in metres, as it stands unturned.</summary>
/// <param name="MinX">Its least X.</param>
/// <param name="MaxX">Its greatest X.</param>
/// <param name="MinY">Its least Y.</param>
/// <param name="MaxY">Its greatest Y.</param>
/// <param name="MinZ">How low it reaches: its base, below the origin when negative.</param>
internal sealed record Footprint(double MinX, double MaxX, double MinY, double MaxY, double MinZ)
{
    /// <summary>Reads a model file's extent.</summary>
    /// <param name="host">The running game, to read a loose file.</param>
    /// <param name="file">The model file.</param>
    /// <returns>The extent, or null for a file that cannot be read as a model or has no vertices.</returns>
    public static Footprint? Read(IEditorHost host, ContentNode file)
    {
        ReadOnlyMemory<byte> bytes;
        if (file.Entry is { } entry)
        {
            bytes = entry.Data;
        }
        else if (!host.TryReadFile(file.Path, out bytes))
        {
            return null;
        }

        try
        {
            MefModel model = MefModel.Read(bytes, Formats.AssetFormatProfile.Permissive);
            if (model.Vertices.Length == 0)
            {
                return null;
            }

            Vector3f min = new(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3f max = new(float.MinValue, float.MinValue, float.MinValue);
            foreach (MefVertex vertex in model.Vertices)
            {
                Vector3f p = vertex.Position;
                min = new Vector3f(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z));
                max = new Vector3f(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z));
            }

            double scale = 1.0 / WorldUnits.PerMetre;
            return new Footprint(min.X * scale, max.X * scale, min.Y * scale, max.Y * scale, min.Z * scale);
        }
        catch (Exception error) when (error is Formats.Ilff.IlffFormatException or PackFormatException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }
}
