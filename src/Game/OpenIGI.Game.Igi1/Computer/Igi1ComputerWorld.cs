using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Computer;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Computer;

/// <summary>The computer's view of a Project I.G.I. mission: its collision, its humans, cameras and vehicles, its highlights.</summary>
public sealed class Igi1ComputerWorld : IIgi1ComputerWorld
{
    /// <summary>How far through glass a camera's view ray steps to carry on, world units (<c>0x533880</c>).</summary>
    public const double GlassStep = 20.48;

    private readonly MissionWorld _mission;
    private readonly Igi1MissionAudio _audio;
    private readonly List<ComputerObject> _humans = [];
    private readonly List<ComputerObject> _vehicles = [];
    private readonly List<ComputerObject> _lenses = [];
    private readonly List<ComputerObject> _holders = [];
    private readonly List<ComputerHilight> _hilights = [];
    private readonly List<(DeclaredTask Task, ComputerHilight Hilight)> _hilightTasks = [];
    private GameTask? _hilightsOf;
    private LevelExpressionRuntime? _expressions;

    /// <summary>Initializes a new instance of the <see cref="Igi1ComputerWorld"/> class.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="audio">Its sounds.</param>
    public Igi1ComputerWorld(MissionWorld mission, Igi1MissionAudio audio)
    {
        _mission = mission ?? throw new ArgumentNullException(nameof(mission));
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    }

    /// <inheritdoc/>
    public IgiRandom Random => _mission.SoundRandom;

    /// <inheritdoc/>
    public IComputerFonts? Fonts { get; set; }

    /// <summary>Gets or sets how a sprite's size is read, which the presentation that loaded the sprites answers.</summary>
    public Func<string, (int Width, int Height)>? SpriteSizes { get; set; }

    /// <summary>Gets or sets how a text resource is found, or <see langword="null"/> before the mission's text is up.</summary>
    public MissionStringResolver? Strings { get; set; }

    /// <summary>Gets or sets the script variables the highlights' conditions read.</summary>
    public ScriptVarRegistry? Variables { get; set; }

    /// <inheritdoc/>
    public (int Width, int Height) SpriteSize(string sprite) => SpriteSizes?.Invoke(sprite) ?? (0, 0);

    /// <inheritdoc/>
    public void PlaySound(string name) => _audio.PlayComputerSound(name);

    /// <inheritdoc/>
    public string? FindText(string resource) => Strings is { } strings && strings.TryGet(resource, out string text) ? text : null;

    /// <inheritdoc/>
    public bool TryDrop(Vector3d from, double depth, out Vector3d ground) =>
        TryLine(from, from - new Vector3d(0d, 0d, depth), out ground, out _);

    /// <inheritdoc/>
    public bool TryLine(Vector3d start, Vector3d finish, out Vector3d hit, out GameTask? struck)
    {
        if (_mission.Collision.Trace is { } trace && trace(new WorldTraceRequest(start, finish), out WorldTraceHit found))
        {
            hit = found.Position;
            struck = found.Owner;
            return true;
        }

        hit = default;
        struck = null;
        return false;
    }

    /// <inheritdoc/>
    public bool CanSee(Vector3d from, Vector3d point, GameTask target) =>
        _mission.Collision.Trace is not { } trace || SoldierSimulation.HasLineOfSight(trace, from, point, 0d, target);

    /// <inheritdoc/>
    public IReadOnlyList<ComputerObject> Humans()
    {
        _humans.Clear();
        if (_mission.Player is { } player && _mission.PlayerTask is { } playerTask)
        {
            _humans.Add(new ComputerObject(playerTask, ComputerObjectKind.Player, player.Body.Position, player.Yaw, 0f)
            {
                Team = playerTask.Team,
                IsLive = !_mission.IsPlayerDead,
            });
        }

        foreach ((GameTask task, int team, Vector3d position, float facing, bool live) in _mission.Soldiers?.MapHumans() ?? [])
        {
            _humans.Add(new ComputerObject(task, ComputerObjectKind.Human, position, facing, 0f) { Team = team, IsLive = live });
        }

        return _humans;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ComputerObject> Vehicles()
    {
        _vehicles.Clear();
        if (_mission.Cutscenes is not { } runtime || _mission.LevelRoot is not { } root)
        {
            return _vehicles;
        }

        foreach (VehicleOccupant occupant in runtime.VehicleOccupants())
        {
            if (root.Tree.FromId(occupant.TaskId) is not { } task)
            {
                continue;
            }

            float yaw = runtime.TryGetDrawTransform(task, out _, out Matrix3f orientation) ? SoldierBodyQueries.FacingOf(orientation) : 0f;
            float radius = 0f;
            foreach (RetailRayModel model in runtime.RetailVehicleModels())
            {
                if (ReferenceEquals(model.Owner, task))
                {
                    radius = model.ModelRadius * Math.Max(model.Scale[0], Math.Max(model.Scale[1], model.Scale[2]));
                    break;
                }
            }

            _vehicles.Add(new ComputerObject(task, ComputerObjectKind.Vehicle, occupant.Position, yaw, radius) { IsLive = true });
        }

        return _vehicles;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ComputerObject> Mines() => [];

    /// <inheritdoc/>
    public IReadOnlyList<ComputerObject> Lenses()
    {
        _lenses.Clear();
        int ticks = _mission.Objectives?.LevelTick ?? 0;
        foreach (SecurityCamera camera in _mission.SecurityCameras?.Cameras ?? [])
        {
            // 0x442BE0: the lens's animation steps every eighth tick from the level's start.
            _lenses.Add(new ComputerObject(camera.Task, ComputerObjectKind.Lens, camera.ViewPositionUnits, SoldierBodyQueries.FacingOf(camera.Orientation), 0f)
            {
                IsLive = camera.IsActive,
                IsExploded = camera.IsExploded,
                Frame = (ticks + 7) / 8,
            });
        }

        return _lenses;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ComputerObject> GunHolders()
    {
        _holders.Clear();
        if (_mission.LevelRoot is not { } root)
        {
            return _holders;
        }

        foreach (GameTask task in root.Descendants())
        {
            if (task is DeclaredTask { Type.Name: "AIStationaryGunHolder" } holder
                && TaskGeometry.TryFindPosition(root, holder.Id, out Vector3d at))
            {
                _holders.Add(new ComputerObject(holder, ComputerObjectKind.GunHolder, at, 0f, 0f));
            }
        }

        return _holders;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ComputerHilight> Hilights()
    {
        _hilights.Clear();
        if (_mission.LevelRoot is not { } root || Variables is not { } variables)
        {
            return _hilights;
        }

        if (!ReferenceEquals(_hilightsOf, root))
        {
            _hilightsOf = root;
            _expressions = new LevelExpressionRuntime(variables);
            _hilightTasks.Clear();
            foreach (GameTask task in root.Descendants())
            {
                if (task is DeclaredTask { Type.Name: "ComputerHilight" } declared)
                {
                    _hilightTasks.Add((declared, Read(declared)));
                }
            }
        }

        foreach ((DeclaredTask task, ComputerHilight hilight) in _hilightTasks)
        {
            // An empty condition evaluates to nought, which shows nothing; the value is made whole as the chopping FPU does.
            bool on = _expressions!.TryEvaluateReal(Text(task, "Hilight"), task, out double value) && (long)value != 0;
            _hilights.Add(hilight with { IsOn = on });
        }

        return _hilights;
    }

    /// <inheritdoc/>
    public void FindObjects(int id, ICollection<ComputerObject> found)
    {
        if (TaskById(id) is { } task)
        {
            Find(task, found);
        }
    }

    /// <inheritdoc/>
    public bool TryLocate(GameTask task, out Vector3d at)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (ReferenceEquals(task, _mission.PlayerTask) && _mission.Player is { } player)
        {
            at = player.Body.Position;
            return true;
        }

        foreach ((GameTask soldier, _, Vector3d position, _, _) in _mission.Soldiers?.MapHumans() ?? [])
        {
            if (ReferenceEquals(soldier, task))
            {
                at = position;
                return true;
            }
        }

        if (_mission.Cutscenes is { } runtime && runtime.TryGetDrawTransform(task, out Vector3d metres, out _))
        {
            at = metres * WorldScene.UnitsPerMetre;
            return true;
        }

        at = default;
        return _mission.LevelRoot is { } root && TaskGeometry.TryFindPosition(root, task.Id, out at);
    }

    /// <inheritdoc/>
    public bool CanFollow(GameTask task) => task switch
    {
        HumanPlayerTask => false,
        HumanTask human => human.Team == 1,
        DeclaredTask { Type.Name: "Heli" or "Car" or "Plane" } => true,
        _ => false,
    };

    /// <inheritdoc/>
    public void SetOffMine(GameTask mine)
    {
    }

    /// <inheritdoc/>
    public ComputerViewCone? ViewCone(GameTask lens)
    {
        if (_mission.SecurityCameras is not { } cameras || !cameras.TryGetCamera(lens, out SecurityCamera? camera) || camera is null || camera.IsExploded)
        {
            return null;
        }

        Vector3d origin = camera.ViewPositionUnits;
        float fov = camera.ViewconeAngle;
        float az = (fov * 0.5f) + SoldierBodyQueries.FacingOf(camera.Orientation);
        float step = fov * 0.083333336f;
        Vector3d[] ends = new Vector3d[ComputerViewCone.Rays];
        for (int i = 0; i < ends.Length; i++)
        {
            // Matrix_SetEuler(0, 0, az) times (0, range, 0): the second column, (-sin, cos, 0).
            float s = (float)Math.Sin(az), c = (float)Math.Cos(az);
            Vector3d start = origin;
            Vector3d end = origin + new Vector3d(-s * camera.ViewconeLength, c * camera.ViewconeLength, 0d);
            while (_mission.Collision.Trace is { } trace && trace(new WorldTraceRequest(start, end, IgnoredOwner: camera.Task), out WorldTraceHit hit))
            {
                if (!GameMaterial.IsSightTransparent(hit.Material))
                {
                    end = hit.Position;
                    break;
                }

                Vector3d along = hit.Position - start;
                double length = Math.Sqrt((along.X * along.X) + (along.Y * along.Y) + (along.Z * along.Z));
                start = length > 0d ? hit.Position + (along * (GlassStep / length)) : hit.Position;
            }

            ends[i] = end;
            az -= step;
        }

        return new ComputerViewCone(origin, ends);
    }

    /// <inheritdoc/>
    public GameTask? TaskById(int id) => _mission.LevelRoot?.Tree.FromId(id);

    private static ComputerHilight Read(DeclaredTask task)
    {
        string sprite = Text(task, "Click to select sprite");
        int colon = sprite.LastIndexOf(':');
        return new ComputerHilight(
            ComputerHilightIds.Parse(Text(task, "TaskID")),
            colon >= 0 ? sprite[(colon + 1)..] : sprite,
            MarkerValue(Text(task, "Marker mesh")),
            ColorValue(Text(task, "Marker color")),
            Text(task, "Title text resource"),
            Text(task, "Info text resource"));
    }

    // 0x467700's constants, as the highlight's create evaluates its two fields.
    private static int MarkerValue(string name) => name switch
    {
        "MARKER_BOX" => 0,
        _ => int.TryParse(name, out int value) ? value : -1,
    };

    private static int ColorValue(string name) => name switch
    {
        "MARKER_COLOR_RED" => 0,
        "MARKER_COLOR_YELLOW" => 1,
        "MARKER_COLOR_GREEN" => 2,
        "MARKER_COLOR_BLUE" => 4,
        _ => int.TryParse(name, out int value) ? value : -1,
    };

    private static string Text(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is string text ? text.Trim() : string.Empty;

    // A task's map handler: what it draws as, or the children it hands the query on to.
    private void Find(GameTask task, ICollection<ComputerObject> found)
    {
        if (task is ConditionalContainerTask container)
        {
            if (container.IsActive)
            {
                foreach (GameTask child in container.Children)
                {
                    Find(child, found);
                }
            }

            return;
        }

        if (task is GuardGeneratorTask generator)
        {
            if (generator.IsSpawned)
            {
                foreach (GameTask child in generator.Children)
                {
                    Find(child, found);
                }
            }

            return;
        }

        foreach (ComputerObject human in _humans)
        {
            if (ReferenceEquals(human.Task, task))
            {
                found.Add(human);
                return;
            }
        }

        if (task is not DeclaredTask declared || _mission.LevelRoot is not { } root)
        {
            return;
        }

        switch (declared.Type.Name)
        {
            case "Building" or "Terminal" or "Cabinet" or "Door" or "Switch" or "Radio" or "GenericTBA" or "PlaceExplosiveTBA":
                Terminal(root, declared, found);
                break;
            case "GenericPickup":
                if (_mission.GenericPickups?.IsCollected(declared) != true)
                {
                    Terminal(root, declared, found);
                }

                break;
            case "Heli" or "Car":
                foreach (ComputerObject vehicle in Vehicles())
                {
                    if (ReferenceEquals(vehicle.Task, task))
                    {
                        found.Add(vehicle);
                    }
                }

                break;
            case "AIStationaryGunHolder":
                if (TaskGeometry.TryFindPosition(root, declared.Id, out Vector3d at))
                {
                    found.Add(new ComputerObject(declared, ComputerObjectKind.GunHolder, at, 0f, 0f));
                }

                break;
        }
    }

    private static void Terminal(GameTask root, DeclaredTask task, ICollection<ComputerObject> found)
    {
        if (!TaskGeometry.TryFindPosition(root, task.Id, out Vector3d at))
        {
            return;
        }

        float yaw = task.TryGetValue("Orientation", out object? value) && value is Vector3f angles ? angles.Z : 0f;
        found.Add(new ComputerObject(task, ComputerObjectKind.Terminal, at, yaw, 0f));
    }
}
