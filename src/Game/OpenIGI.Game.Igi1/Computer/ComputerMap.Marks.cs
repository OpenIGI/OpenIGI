using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi1.Computer;

/// <content>
/// The marks over the map (<c>0x467970</c>): the layers the boot box has brought up, each object drawn by its type's map
/// handler, in the order the original draws them.
/// </content>
public sealed partial class ComputerMap
{
    private readonly List<ComputerMark> _marks = [];
    private readonly List<ComputerObject> _found = [];

    /// <summary>Gets the marks as the last tick left them, in the order they are drawn.</summary>
    public IReadOnlyList<ComputerMark> Marks => _marks;

    /// <summary>Gets the selected camera's view, as the last tick left it, or <see langword="null"/>.</summary>
    public ComputerViewCone? ViewCone { get; private set; }

    // 0x467970. The query is one record handed from object to object: what one leaves in it -- a highlight's sprite, a soldier's
    // marker -- the next draws with, unless it sets its own.
    private void BuildMarks()
    {
        _marks.Clear();
        ViewCone = null;
        Query q = new();
        IIgi1ComputerWorld world = _computer.World;
        if ((Shown & Layers.Humans) != 0)
        {
            foreach (ComputerObject human in world.Humans())
            {
                Draw(human, q);
            }
        }

        if ((Shown & Layers.Vehicles) != 0)
        {
            foreach (ComputerObject vehicle in world.Vehicles())
            {
                Draw(vehicle, q);
            }
        }

        if ((Shown & Layers.Hilights) != 0)
        {
            foreach (ComputerHilight hilight in world.Hilights())
            {
                // 0x4687A0.
                if (!hilight.IsOn)
                {
                    continue;
                }

                q.Sprite = hilight.Sprite;
                q.Marker = hilight.Marker;
                q.Color = hilight.Color;
                q.Text = hilight.Text;
                q.Text2 = hilight.Text2;
                foreach (int id in hilight.Ids)
                {
                    _found.Clear();
                    world.FindObjects(id, _found);
                    foreach (ComputerObject named in _found)
                    {
                        Draw(named, q);
                    }
                }
            }
        }

        if ((Shown & Layers.Cameras) != 0 && _computer.Zoom >= 1)
        {
            foreach (ComputerObject lens in world.Lenses())
            {
                Draw(lens, q);
            }

            if (SelectedLens is { } selected)
            {
                ViewCone = world.ViewCone(selected);
            }
        }

        if ((Shown & Layers.Objects) != 0)
        {
            foreach (ComputerObject mine in world.Mines())
            {
                Draw(mine, q);
            }

            foreach (ComputerObject holder in world.GunHolders())
            {
                Draw(holder, q);
            }
        }
    }

    private void Draw(ComputerObject o, Query q)
    {
        int zoom = _computer.Zoom;
        switch (o.Kind)
        {
            case ComputerObjectKind.Terminal:
                // 0x443DC0.
                Place(q, o);
                Sprite(q, nudged: true);
                Marker(q, culled: false);
                Label(q);
                break;
            case ComputerObjectKind.Player:
                // 0x413730.
                Place(q, o);
                q.Marker = 2;
                q.Color = 3;
                Marker(q, culled: false);

                // A co-op teammate under its name, which is no text resource to be looked up.
                if (o.Name.Length > 0)
                {
                    _marks.Add(new ComputerMark(ComputerMarkKind.Label, q.Position) { Text = o.Name });
                }

                break;
            case ComputerObjectKind.Human:
                // 0x460280: from the first close zoom to the last but one, alive, in view and in sight of the map's eye.
                if (zoom == 0 || zoom == 3)
                {
                    return;
                }

                Place(q, o);
                q.Marker = 1;
                q.Color = o.Team is 0 or 2 ? 3 : 0;
                if (o.IsLive && _computer.World.CanSee(CameraPosition, o.Position, o.Task))
                {
                    Marker(q, culled: true);
                }

                break;
            case ComputerObjectKind.Vehicle:
                // 0x467C60: a yellow box while it runs.
                if (zoom == 0 || zoom == 3 || !o.IsLive)
                {
                    return;
                }

                Place(q, o);
                q.Marker = 0;
                q.Color = 1;
                Marker(q, culled: false);
                break;
            case ComputerObjectKind.Mine:
                // 0x475F30: only a mine set off from afar, and the sprite it leaves behind is none.
                Place(q, o);
                q.Sprite = string.Empty;
                if (!o.IsExploded && o.IsRemote)
                {
                    q.Sprite = "h_mine.spr";
                    Sprite(q, nudged: true);
                    q.Sprite = string.Empty;
                }

                break;
            case ComputerObjectKind.GunHolder:
                // 0x46E9F0.
                Place(q, o);
                q.Sprite = "h_sentrygun.spr";
                Sprite(q, nudged: true);
                q.Sprite = string.Empty;
                break;
            case ComputerObjectKind.Lens:
                // 0x442C60: its own sprite, the query left alone; nothing once the camera has gone off.
                if (!o.IsExploded)
                {
                    _marks.Add(new ComputerMark(ComputerMarkKind.Sprite, o.Position)
                    {
                        Text = o.IsLive ? "h_scamera2.spr" : "h_scamera1.spr",
                        Frame = o.IsLive ? o.Frame : 0,
                    });
                }

                break;
        }
    }

    private static void Place(Query q, in ComputerObject o)
    {
        q.Position = o.Position;
        q.Yaw = o.Yaw;
        q.Radius = o.Radius;
    }

    // 0x467DF0.
    private void Sprite(Query q, bool nudged)
    {
        if (q.Sprite.Length > 0)
        {
            _marks.Add(new ComputerMark(ComputerMarkKind.Sprite, q.Position) { Text = q.Sprite, Nudged = nudged });
        }
    }

    // 0x467F30.
    private void Marker(Query q, bool culled)
    {
        if (q.Marker == -1)
        {
            return;
        }

        float size = q.Radius >= ComputerMarkShapes.MinimumSize ? q.Radius : ComputerMarkShapes.MinimumSize;
        _marks.Add(new ComputerMark(ComputerMarkKind.Marker, q.Position)
        {
            Shape = q.Marker,
            Color = q.Color,
            Yaw = q.Yaw,
            Size = size,
            Culled = culled,
            Text = string.Empty,
        });
    }

    // 0x4680B0: the title at the first close zoom, the detail closer; a resource that is not there is shown by its name.
    private void Label(Query q)
    {
        int zoom = _computer.Zoom;
        string name = zoom == 1 ? q.Text : zoom > 1 ? q.Text2 : string.Empty;
        if (name.Length == 0)
        {
            return;
        }

        string text = _computer.World.FindText(name) ?? $"RM: {name}";
        _marks.Add(new ComputerMark(ComputerMarkKind.Label, q.Position) { Text = text });
    }

    // MapQuery: what the handlers pass on to each other.
    private sealed class Query
    {
        public Vector3d Position { get; set; }

        public float Yaw { get; set; }

        public float Radius { get; set; }

        public string Sprite { get; set; } = string.Empty;

        public int Marker { get; set; } = -1;

        public int Color { get; set; } = -1;

        public string Text { get; set; } = string.Empty;

        public string Text2 { get; set; } = string.Empty;
    }
}
