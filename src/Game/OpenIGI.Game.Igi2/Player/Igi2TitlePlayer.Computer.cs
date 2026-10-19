using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The open map computer: its screens run after the player as the game screen's task, on the raw keys and the mouse, and while
/// the map is shown the player looks through its camera.
/// </content>
public sealed partial class Igi2TitlePlayer
{
    private TitleRawKeys _rawKeys;
    private Igi2ComputerMapWorld? _mapWorld;

    /// <summary>Gets the open computer's screens, or <see langword="null"/> while the level has no computer.</summary>
    public Igi2ComputerDisplay? ComputerDisplay { get; private set; }

    /// <summary>Gets a value indicating whether the computer's screens cover the view.</summary>
    public bool IsComputerShown => ComputerDisplay is { IsOpen: true };

    /// <summary>Gets a value indicating whether the weather is hidden: the open computer turns the rain off (<c>Computer_isRain</c>).</summary>
    public bool IsWeatherHidden => IsComputerShown;

    /// <summary>Gets a value indicating whether the player looks through the map's camera.</summary>
    public bool IsMapShown => ComputerDisplay is { IsMapShown: true, Map: not null };

    /// <summary>Gets or sets the level's humans, which the map can follow.</summary>
    internal IReadOnlyList<Igi2Human> MapHumans { get; set; } = [];

    /// <summary>Gets or sets the level's tasks by id, which the objectives' map buttons look at.</summary>
    internal Func<int, GameTask?>? TaskById { get; set; }

    /// <summary>Gets or sets the mission's number, which the quirks pick the broken casing by.</summary>
    public int MissionId { get; set; }

    /// <summary>Gets or sets the title's special cases, which say which missions have the broken casing.</summary>
    public Igi2Quirks Quirks { get; set; } = new();

    // The game screen's ComputerMenu task, after the player: it opens as the player's computer state opens the computer, runs,
    // and closes with it.
    private void RunComputerDisplay()
    {
        if (Computer is not { } computer)
        {
            return;
        }

        Igi2ComputerDisplay display = ComputerDisplay ??= new Igi2ComputerDisplay(computer)
        {
            Strings = key => Strings?.Get(key) ?? key,
            PlaySound = name => Sounds.New(name, EyePosition, () => EyePosition),
            Log = StatusMessages.Log,
            Saves = new ComputerSaves(this),
        };
        display.IsBroken = Quirks.HasBrokenComputer(MissionId);
        if (computer.IsOpen && !display.IsOpen)
        {
            display.Open(() => Human.Position);
        }
        else if (!computer.IsOpen && display.IsOpen)
        {
            display.Close();
        }

        if (!display.IsOpen)
        {
            return;
        }

        TitleRawKeys raw = _rawKeys;
        _mapWorld ??= new Igi2ComputerMapWorld(this);
        display.Run(
            new Igi2ComputerInput(
                Human.Channels[Igi2HumanChannel.LookX],
                Human.Channels[Igi2HumanChannel.LookY],
                Human.InvertMouse,
                (raw & TitleRawKeys.MouseLeft) != 0,
                (raw & TitleRawKeys.MouseRight) != 0,
                (raw & TitleRawKeys.WheelUp) != 0,
                (raw & TitleRawKeys.WheelDown) != 0,
                (raw & TitleRawKeys.Up) != 0,
                (raw & TitleRawKeys.Down) != 0,
                (raw & TitleRawKeys.Left) != 0,
                (raw & TitleRawKeys.Right) != 0,
                (raw & TitleRawKeys.Enter) != 0,
                (raw & TitleRawKeys.Home) != 0,
                (raw & TitleRawKeys.Control) != 0),
            _mapWorld,
            Human.Random);
    }

    private readonly List<Igi2ComputerMapMark> _mapMarks = [];
    private readonly Igi2LineTrace _markLine = new(1);

    /// <summary>Gets or sets the level's vehicles, which the map marks.</summary>
    internal IReadOnlyList<Vehicles.Igi2CarTask> MapVehicles { get; set; } = [];

    /// <summary>Gets or sets the level's mounted guns' holders, which the map marks.</summary>
    internal IReadOnlyList<Weapons.Igi2AiStationaryGunHolderTask> MapGunHolders { get; set; } = [];

    /// <summary>
    /// The marks the map draws over the level, <c>ComputerObject_DrawHandler</c> with every flag (<c>31</c>): each human's, the
    /// vehicles', each highlight's on what it points at, and the mounted guns'. Asked for as the map is drawn.
    /// </summary>
    /// <returns>The marks, in the order they are drawn.</returns>
    public IReadOnlyList<Igi2ComputerMapMark> ComputerMapMarks()
    {
        _mapMarks.Clear();
        if (!IsMapShown || ComputerDisplay?.Map is not { } map || Computer is not { } computer)
        {
            return _mapMarks;
        }

        int zoom = map.ZoomLevel;
        foreach (Igi2Human human in MapHumans)
        {
            MarkHuman(human, map);
        }

        foreach (Vehicles.Igi2CarTask car in MapVehicles)
        {
            MarkVehicle(car, zoom);
        }

        foreach (Igi2ComputerHilightTask hilight in computer.Hilights)
        {
            if (!hilight.IsOn)
            {
                continue;
            }

            string? sprite = Igi2ComputerMapMark.HilightSprites.Contains(hilight.Sprite) ? hilight.Sprite : null;
            IReadOnlyList<int> ids = hilight.TaskIds;
            foreach (int id in ids)
            {
                if (TaskById?.Invoke(id) is { } task)
                {
                    MarkTask(task, hilight, sprite, map, zoom);
                }
            }

            // ComputerHilight_DrawComputerObjectHandler: pointing at nothing, its sprite stands where it is.
            if (ids.Count == 0 && sprite is not null)
            {
                _mapMarks.Add(new Igi2ComputerMapMark(Igi2ComputerMarkKind.Sprite, SnapToObject(hilight.Position), sprite));
            }
        }

        // The notified list's (Mine_DrawComputerObjectHandler): each remote mine still to explode.
        foreach (Weapons.Igi2Mine mine in Mines?.Laid ?? [])
        {
            if (mine.IsRemote && !mine.IsExploded)
            {
                _mapMarks.Add(new Igi2ComputerMapMark(Igi2ComputerMarkKind.Sprite, SnapToObject(mine.Position), "h_mine"));
            }
        }

        foreach (Weapons.Igi2AiStationaryGunHolderTask holder in MapGunHolders)
        {
            _mapMarks.Add(new Igi2ComputerMapMark(Igi2ComputerMarkKind.Sprite, SnapToObject(holder.Position), "h_sentrygun"));
        }

        return _mapMarks;
    }

    // HumanPlayer_DrawComputerObjectHandler and Human_DrawComputerObjectHandler: the player a green triangle its way; a soldier
    // alive and active, seen from the camera, a diamond in its team's colour.
    private void MarkHuman(Igi2Human human, Igi2ComputerMap map)
    {
        if (ReferenceEquals(human, Human))
        {
            _mapMarks.Add(new Igi2ComputerMapMark(
                Igi2ComputerMarkKind.Marker, human.Position, Marker: Igi2ComputerMarker.Triangle, Colour: Igi2ComputerMarkerColour.Green, Gamma: human.Gamma));
            return;
        }

        if (human.IsDead || !human.IsActive)
        {
            return;
        }

        Vector3d head = new(human.Position.X, human.Position.Y, human.Position.Z + 4096.0);
        if (CollisionWorld is { } world)
        {
            _markLine.Starts[0] = map.Position;
            _markLine.Ends[0] = head;
            world.TraceLines(_markLine, 0xFFFFFFFFu);
            Igi2Intersection hit = _markLine.Intersections[0];
            if (hit.IsHit && !ReferenceEquals(hit.HitObject?.Owner, human))
            {
                return;
            }
        }

        Igi2ComputerMarkerColour colour = human.Team is 0 or 2 ? Igi2ComputerMarkerColour.BrightGreen : Igi2ComputerMarkerColour.Blue;
        _mapMarks.Add(new Igi2ComputerMapMark(
            Igi2ComputerMarkKind.Marker, human.Position, Marker: Igi2ComputerMarker.Diamond, Colour: colour, Radius: Igi2ComputerMapMark.HumanRadius, Gamma: human.Gamma));
    }

    // ComputerObject_VehicleDrawComputerObjectHandler: a yellow box about it, at the middle two zooms.
    private void MarkVehicle(Vehicles.Igi2CarTask car, int zoom)
    {
        if (zoom is 0 or 3)
        {
            return;
        }

        IReadOnlyList<float> m = car.CurrentOrientation;
        _mapMarks.Add(new Igi2ComputerMapMark(
            Igi2ComputerMarkKind.Marker, car.Position, Marker: Igi2ComputerMarker.Box, Colour: Igi2ComputerMarkerColour.Yellow,
            Radius: car.Radius, Gamma: World.Igi2Pose.Gamma(m[0], m[3])));
    }

    // A highlight's task's own DrawComputerObject handler, with the highlight's sprite, marker, colour and texts.
    private void MarkTask(GameTask task, Igi2ComputerHilightTask hilight, string? sprite, Igi2ComputerMap map, int zoom)
    {
        switch (task)
        {
            case Igi2HumanPlayerTask:
                MarkHuman(Human, map);
                break;
            case Ai.Igi2HumanSoldierTask { Body: { } soldier }:
                MarkHuman(soldier, map);
                break;
            case Ai.Igi2AiSquadTask squad:
                foreach (GameTask member in squad.Children)
                {
                    MarkTask(member, hilight, sprite, map, zoom);
                }

                break;
            case Vehicles.Igi2CarTask car:
                MarkVehicle(car, zoom);
                break;
            case Weapons.Igi2AiStationaryGunHolderTask holder:
                _mapMarks.Add(new Igi2ComputerMapMark(Igi2ComputerMarkKind.Sprite, SnapToObject(holder.Position), "h_sentrygun"));
                break;
            case Interaction.Igi2EditRigidObjTask rigid:
                MarkRigid(rigid, hilight, sprite, zoom);
                break;
        }
    }

    // RigidDynCubeObj_DrawComputerObjectHandler: the sprite on top, the marker about it, the text beside.
    private void MarkRigid(Interaction.Igi2EditRigidObjTask rigid, Igi2ComputerHilightTask hilight, string? sprite, int zoom)
    {
        Vector3d at = rigid.CameraTargetPosition;
        IReadOnlyList<float> m = rigid.CameraTargetOrientation;
        float radius = rigid.Models?.Invoke(rigid.Model)?.NativeMarkerBound ?? 0f;
        Vector3d top = SnapToObject(at);
        if (sprite is not null)
        {
            _mapMarks.Add(new Igi2ComputerMapMark(Igi2ComputerMarkKind.Sprite, top, sprite));
        }

        if (hilight.MarkerMeshValue != -1)
        {
            _mapMarks.Add(new Igi2ComputerMapMark(
                Igi2ComputerMarkKind.Marker, at, Marker: (Igi2ComputerMarker)hilight.MarkerMeshValue,
                Colour: (Igi2ComputerMarkerColour)hilight.MarkerColourValue, Radius: radius, Gamma: World.Igi2Pose.Gamma(m[0], m[3])));
        }

        string text = zoom == 1 ? hilight.TitleText : zoom > 1 ? hilight.InfoText : string.Empty;
        if (text.Length != 0 && ComputerDisplay is { } display)
        {
            _mapMarks.Add(new Igi2ComputerMapMark(Igi2ComputerMarkKind.Text, top, display.Text(text)));
        }
    }

    // ComputerObject_SnapToObject: the top of whatever stands at a point, a line from high above it down past it.
    private Vector3d SnapToObject(Vector3d at)
    {
        if (CollisionWorld is not { } world)
        {
            return at;
        }

        _markLine.Starts[0] = new Vector3d(at.X, at.Y, at.Z + 2048000.0);
        _markLine.Ends[0] = new Vector3d(at.X, at.Y, at.Z - 409600.0);
        world.TraceLines(_markLine, 0xFFFFFFFFu);
        return _markLine.Intersections[0].Position;
    }

    /// <summary>
    /// The player's delayed run's saves (<c>HumanPlayer_RunDelayedHandler</c>): a load asked for is read, then a save finished is
    /// written, and both are forgotten.
    /// </summary>
    private void RunComputerSaves()
    {
        if (Human.LoadSlot is var load and not 0)
        {
            Human.LoadSlot = 0;
            Saves?.Load(load - 1);
        }

        if (Human.SaveSlot is var save and not 0)
        {
            Saves?.Write(save - 1);
            Human.TempSaveSlot = 0;
            Human.SaveSlot = 0;
        }
    }

    // The save screen's hold on the player and its saves.
    private sealed class ComputerSaves(Igi2TitlePlayer player) : IIgi2ComputerSaves
    {
        public float BatteryLevel => player.Saves?.BatteryLevel ?? 0f;

        public bool IsSaveAllowed => player.Human.IsSaveAllowed;

        public int QuickLoadSlot => player.Human.QuickLoadSlot;

        public (bool IsUsed, string Info, string When) Slot(int slot) =>
            player.Saves?.SlotLabel(slot, key => player.Strings?.Get(key) ?? key) ?? (false, string.Empty, string.Empty);

        public void BeginSave(int slot) => player.Human.BeginSaveToSlot(slot);

        public void FinishSave(int slot) => player.Human.FinishSaveToSlot(slot);

        public void Load(int slot) => player.Human.LoadFromSlot(slot);

        public void FreezeFire() => player.FreezeFire();

        public void QuitComputer() => player.Human.QuitComputer();
    }

    // What the map asks of the level, from the player's collision world, the level's humans and its tasks.
    private sealed class Igi2ComputerMapWorld(Igi2TitlePlayer player) : IIgi2ComputerMapWorld
    {
        private readonly Igi2LineTrace _line = new(1);

        public IEnumerable<(Vector3d Position, IIgi2CameraTarget? Followable)> Humans
        {
            get
            {
                foreach (Igi2Human human in player.MapHumans)
                {
                    // Human_ComputerMapFollowQTaskEventHandler: only a friend, team one, is followed.
                    yield return (human.Position, human.Team == 1 ? new Target(() => human.Position) : null);
                }
            }
        }

        public bool DetonateRemoteMine(Vector3d at, double reachSquared)
        {
            foreach (Weapons.Igi2Mine mine in player.Mines?.Laid ?? [])
            {
                double dx = mine.Position.X - at.X, dy = mine.Position.Y - at.Y, dz = mine.Position.Z - at.Z;
                if (mine.IsRemote && reachSquared > (dz * dz) + (dy * dy) + (dx * dx))
                {
                    mine.DetonateFromMap();
                    return true;
                }
            }

            return false;
        }

        public Vector3d? GroundBelow(Vector3d from, double distance)
        {
            if (player.CollisionWorld is not { } world)
            {
                return null;
            }

            _line.Starts[0] = from;
            world.NextZ(_line, [distance], Igi2CollisionWorld.TerrainGroup);
            return _line.Intersections[0].IsHit ? _line.Intersections[0].Position : null;
        }

        public (Vector3d Position, bool IsHit, IIgi2CameraTarget? Followable) Trace(Vector3d start, Vector3d finish)
        {
            if (player.CollisionWorld is not { } world)
            {
                return (finish, false, null);
            }

            _line.Starts[0] = start;
            _line.Ends[0] = finish;
            world.TraceLines(_line, 0xFFFFFFFFu);
            Igi2Intersection hit = _line.Intersections[0];
            IIgi2CameraTarget? followable = hit.HitObject?.Owner switch
            {
                Igi2Human { Team: 1 } human => new Target(() => human.Position),
                Vehicles.Igi2CarTask car => new Target(() => car.Position),
                _ => null,
            };
            return (hit.Position, hit.IsHit, followable);
        }

        public IIgi2CameraTarget? Task(int id) => player.TaskById?.Invoke(id) switch
        {
            IIgi2CameraTarget target => target,
            Igi2HumanPlayerTask => new Target(() => player.Human.Position),
            Ai.Igi2HumanSoldierTask { Body: { } soldier } => new Target(() => soldier.Position),
            GameTask task when task.TryGetScriptValue("Position", out object? value) && value is Vector3d position
                => new Target(() => position),
            _ => null,
        };
    }

    // Where something stands, as the map looks at it.
    private sealed class Target(Func<Vector3d> position) : IIgi2CameraTarget
    {
        private static readonly float[] Identity = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

        public Vector3d CameraTargetPosition => position();

        public IReadOnlyList<float> CameraTargetOrientation => Identity;
    }
}
