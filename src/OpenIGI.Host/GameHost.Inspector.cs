using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Host.DevTools;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Rendering;
using System.Globalization;

namespace OpenIGI.Host;

/// <summary>
/// The game's answer to the developer inspector: everything the world is made of, and how to go and
/// look at it.
/// </summary>
/// <remarks>
/// <para>
/// A partial of the game rather than a type of its own, following the same split as the mission and
/// menu partials, because everything here needs the fields the loop already holds — the scene, the
/// soldiers, the registries, the player, the camera and the terrain.
/// </para>
/// <para>
/// <b>Handles encode their source.</b> An index into the last list produced would come apart the
/// moment the filter changed between listing and asking for detail, so a handle is
/// <c>kind × <see cref="KindStride"/> + index</c> and resolves on its own. That is what lets the panel
/// keep a selection while the reader types in the filter box.
/// </para>
/// <para>
/// Everything crosses as a snapshot or a string. The panels are on the far side of an interface that
/// must not see gameplay types, and a panel holding a live placement across a mission change would be
/// holding one the loader had already discarded.
/// </para>
/// </remarks>
internal sealed partial class GameHost : IWorldInspector
{
    /// <summary>How many handles each kind reserves.</summary>
    /// <remarks>Larger than any mission's count of anything, so two kinds can never collide.</remarks>
    private const int KindStride = 1_000_000;

    private const int KindPlayer = 0;
    private const int KindSoldier = 1;
    private const int KindObject = 2;
    private const int KindDoor = 3;
    private const int KindElevator = 4;
    private const int KindInteraction = 5;
    private const int KindWeaponPickup = 6;
    private const int KindPickup = 7;
    private const int KindCamera = 8;
    private const int KindSpeaker = 9;
    private const int KindLadder = 10;
    private const int KindTerrain = 11;

    private readonly List<SoldierSnapshot> _inspectorSoldiers = [];

    /// <inheritdoc/>
    public void Describe(string filter, List<InspectableEntity> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        string needle = filter ?? string.Empty;
        Vector3d eye = ViewpointPosition();

        AddPlayer(into, needle, eye);
        AddSoldiers(into, needle, eye);
        AddPlacements(into, needle, eye);
        AddDoors(into, needle, eye);
        AddElevators(into, needle, eye);
        AddInteractions(into, needle, eye);
        AddPickups(into, needle, eye);
        AddCameras(into, needle, eye);
        AddSpeakers(into, needle, eye);
        AddLadders(into, needle, eye);
        AddTerrain(into, needle, eye);
    }

    /// <inheritdoc/>
    public void Detail(int id, List<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();

        int kind = id / KindStride;
        int index = id % KindStride;

        switch (kind)
        {
            case KindPlayer:
                DetailPlayer(into);
                break;
            case KindSoldier:
                DetailSoldier(into, index);
                break;
            case KindObject:
                DetailPlacement(into, index);
                break;
            case KindDoor:
                DetailDoor(into, index);
                break;
            case KindElevator:
                DetailElevator(into, index);
                break;
            case KindInteraction:
                DetailInteraction(into, index);
                break;
            case KindWeaponPickup:
            case KindPickup:
                DetailPickup(into, kind, index);
                break;
            case KindCamera:
                DetailCamera(into, index);
                break;
            case KindSpeaker:
                DetailSpeaker(into, index);
                break;
            case KindLadder:
                DetailLadder(into, index);
                break;
            case KindTerrain:
                DetailTerrain(into);
                break;
            default:
                break;
        }
    }

    /// <inheritdoc/>
    public bool TryTeleportPlayerTo(int id) =>
        _player is not null && TryResolvePosition(id, out Vector3d position) &&
        _player.Place(_playerTerrain, position, _player.Body.Yaw);

    /// <inheritdoc/>
    public bool TryMoveCameraTo(int id)
    {
        if (_camera is null || !TryResolvePosition(id, out Vector3d position))
        {
            return false;
        }

        // Stand back and a little above rather than inside the thing being looked at, which is what
        // "go and look at it" means and what putting the camera at its exact origin does not.
        _camera.MoveTo(position + new Vector3d(0.0, -6.0, 3.0), _camera.Yaw, _camera.Pitch);
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The world-to-view rotation is the camera orientation's <em>transpose</em>, because its rows are
    /// the view axes — the same conversion <c>Camera3D.CreateModelView</c> makes, and the one
    /// place this is easy to get backwards. In view space the components are right, up and forward in
    /// that order.
    /// </para>
    /// <para>
    /// The lens is the frame's own horizontal half-angle tangent rather than a constant, so the marker
    /// still lands on its subject while the player is aiming down a scope and the field of view has
    /// narrowed underneath it.
    /// </para>
    /// </remarks>
    public bool TryProject(int id, out ScreenMarker marker)
    {
        marker = default;

        if (!TryResolvePosition(id, out Vector3d position))
        {
            return false;
        }

        Vector3d eye = _inspectorEye.Position;
        var offset = new Vector3d(position.X - eye.X, position.Y - eye.Y, position.Z - eye.Z);
        double distance = Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y) + (offset.Z * offset.Z));

        Vector3d view = _inspectorEye.Orientation.Transposed().Transform(offset);
        bool inFront = view.Z > 0.05;

        float width = MathF.Max(1f, _window.Width);
        float height = MathF.Max(1f, _window.Height);

        // Behind the camera there is no projection to make, so the direction is flattened onto the
        // screen plane and pushed well outside the viewport; the caller pins it to an edge from there.
        double forward = inFront ? view.Z : 0.05;
        double tangentH = Math.Max(0.0001f, _inspectorTangent);
        double tangentV = tangentH / Math.Max(0.0001f, _inspectorAspect);

        double ndcX = view.X / forward / tangentH;
        double ndcY = view.Y / forward / tangentV;

        if (!inFront)
        {
            // Mirror it: something behind the left shoulder belongs off the left edge, not the right.
            ndcX = ndcX == 0.0 ? -2.0 : -Math.Sign(ndcX) * Math.Max(2.0, Math.Abs(ndcX));
            ndcY = -ndcY;
        }

        marker = new ScreenMarker(
            (float)(((ndcX * 0.5) + 0.5) * width),
            (float)((1.0 - ((ndcY * 0.5) + 0.5)) * height),
            distance,
            inFront);

        return true;
    }

    /// <summary>The selection glow's colour — the console's own amber, so the two read as one tool.</summary>
    private static readonly Bgra32 SelectionGlow = Bgra32.FromRgb(255, 196, 64);

    /// <summary>How far the selection glow reaches past its subject, in pixels.</summary>
    /// <remarks>
    /// Pixels rather than metres, and the same number at every distance: the mark exists to be found,
    /// and one measured in world units is a bright halo up close and invisible across a valley.
    /// </remarks>
    private const float SelectionGlowRadius = 7f;

    /// <summary>
    /// Draws the selection glow around whatever the World panel has chosen.
    /// </summary>
    /// <param name="console">The console holding the selection.</param>
    /// <param name="camera">The eye the frame is being drawn from.</param>
    /// <remarks>
    /// <para>
    /// Both kinds of thing that have a model of their own are covered: a placement, drawn through the
    /// world renderer, and a soldier, drawn through the soldier renderer in the pose it is animating
    /// in. The player is skipped — a glow around the inside of one's own head is nothing.
    /// </para>
    /// <para>
    /// A ladder, an interaction volume or a speaker is an authored point with no shape to trace, so
    /// those keep the console's screen-space bracket, which is the honest answer for something that
    /// has no silhouette.
    /// </para>
    /// </remarks>
    private void DrawInspectorOutline(HostConsole console, Camera3D camera)
    {
        if (!console.WantsDraw || console.View.SelectedEntityId is not { } id)
        {
            return;
        }

        int kind = id / KindStride;
        int index = id % KindStride;

        if (kind == KindObject && _world is not null && _scene is not null)
        {
            _world.DrawOutline(_scene, camera, index, SelectionGlow, SelectionGlowRadius, throughWalls: true);
            return;
        }

        if (kind == KindSoldier)
        {
            _soldiers?.DrawOutline(index, camera, SelectionGlow, SelectionGlowRadius, throughWalls: true);
        }
    }

    /// <summary>Gets where the reader is looking from, which distances are measured against.</summary>
    /// <remarks>
    /// <c>_player.Position</c>, not <c>_player.Body.Position</c>: the body keeps its position in the
    /// level's own units and the camera converts, so reading the body here put every distance out by
    /// a factor of four thousand — reported as soldiers a hundred million metres away.
    /// </remarks>
    private Vector3d ViewpointPosition() =>
        _playerMode && _player is not null ? _player.Position : _camera?.Position ?? default;

    // ---- Listing ----

    private void AddPlayer(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        if (_player is null || !Matches(needle, "player", "player"))
        {
            return;
        }

        Vector3d at = _player.Position;
        into.Add(new InspectableEntity(
            KindPlayer * KindStride,
            "player",
            "player",
            at,
            _playerDead ? "dead" : "alive",
            Distance(eye, at)));
    }

    private void AddSoldiers(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        _inspectorSoldiers.Clear();
        _soldiers?.Snapshot(_inspectorSoldiers);

        for (int i = 0; i < _inspectorSoldiers.Count; i++)
        {
            SoldierSnapshot soldier = _inspectorSoldiers[i];
            string kind = soldier.IsCutsceneActor ? "actor" : "soldier";

            if (!Matches(needle, soldier.Name, kind))
            {
                continue;
            }

            into.Add(new InspectableEntity(
                (KindSoldier * KindStride) + i,
                kind,
                soldier.Name.Length > 0 ? soldier.Name : $"#{i}",
                soldier.Position,
                DescribeSoldier(soldier),
                Distance(eye, soldier.Position)));
        }
    }

    /// <summary>
    /// Lists every model placed in the level — the buildings, the props, the scenery.
    /// </summary>
    /// <remarks>
    /// This is the bulk of a mission by a wide margin, which is why the panel that draws it is
    /// row-virtualised: a level runs to thousands of placements and all of them belong here.
    /// </remarks>
    private void AddPlacements(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        if (_scene is null)
        {
            return;
        }

        IReadOnlyList<WorldPlacement> placements = _scene.Placements;
        for (int i = 0; i < placements.Count; i++)
        {
            WorldPlacement placement = placements[i];
            string model = placement.Instance.Name;
            string task = placement.TaskName;

            if (!Matches(needle, model, "object") && !Matches(needle, task, "model"))
            {
                continue;
            }

            // The model is the identity worth showing; the authored task name is boilerplate that
            // repeats -- nearly a thousand placements in this level are all called EditRigidObj_-1,
            // which tells the reader nothing. Both stay searchable.
            into.Add(new InspectableEntity(
                (KindObject * KindStride) + i,
                placement.IsAttached ? "attached" : "object",
                model,
                placement.Position,
                task,
                Distance(eye, placement.Position)));
        }
    }

    private void AddDoors(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        IReadOnlyList<PlacedDoor>? doors = _doors?.Doors;
        for (int i = 0; doors is not null && i < doors.Count; i++)
        {
            PlacedDoor door = doors[i];
            string name = door.Task.Name;

            if (!Matches(needle, name, "door"))
            {
                continue;
            }

            into.Add(new InspectableEntity(
                (KindDoor * KindStride) + i,
                "door",
                name,
                door.DrawPosition,
                DescribeDoor(door),
                Distance(eye, door.DrawPosition)));
        }
    }

    private void AddElevators(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        IReadOnlyList<PlacedElevator>? elevators = _elevators?.Elevators;
        for (int i = 0; elevators is not null && i < elevators.Count; i++)
        {
            PlacedElevator elevator = elevators[i];
            string name = elevator.Task.Name;

            if (!Matches(needle, name, "elevator"))
            {
                continue;
            }

            into.Add(new InspectableEntity(
                (KindElevator * KindStride) + i,
                "elevator",
                name,
                elevator.DrawPosition,
                elevator.State.IsMoving ? "moving" : $"floor {elevator.State.CurrentFloorIndex}",
                Distance(eye, elevator.DrawPosition)));
        }
    }

    private void AddInteractions(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        IReadOnlyList<WorldInteraction>? items = _interactions?.Items;
        for (int i = 0; items is not null && i < items.Count; i++)
        {
            WorldInteraction interaction = items[i];
            string name = interaction.Task.Name;

            if (!Matches(needle, name, "interaction"))
            {
                continue;
            }

            Vector3d at = Metres(interaction.Anchor);
            into.Add(new InspectableEntity(
                (KindInteraction * KindStride) + i,
                "action",
                name,
                at,
                interaction.Type.ToString().ToLowerInvariant(),
                Distance(eye, at)));
        }
    }

    private void AddPickups(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        IReadOnlyList<GunPickup>? guns = _gunPickups?.Items;
        for (int i = 0; guns is not null && i < guns.Count; i++)
        {
            GunPickup pickup = guns[i];
            string name = pickup.Weapon.ToString();

            if (!Matches(needle, name, "weapon"))
            {
                continue;
            }

            Vector3d gunAt = Metres(pickup.Position);
            into.Add(new InspectableEntity(
                (KindWeaponPickup * KindStride) + i,
                "weapon",
                name,
                gunAt,
                pickup.IsCollected ? "taken" : "available",
                Distance(eye, gunAt)));
        }

        IReadOnlyList<GenericPickup>? items = _genericPickups?.Items;
        for (int i = 0; items is not null && i < items.Count; i++)
        {
            GenericPickup pickup = items[i];
            string name = pickup.Task.Name;

            if (!Matches(needle, name, "pickup"))
            {
                continue;
            }

            Vector3d itemAt = Metres(pickup.Position);
            into.Add(new InspectableEntity(
                (KindPickup * KindStride) + i,
                "pickup",
                name,
                itemAt,
                pickup.IsCollected ? "taken" : "available",
                Distance(eye, itemAt)));
        }
    }

    private void AddCameras(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        IReadOnlyList<SecurityCamera>? cameras = _securityCameras?.Cameras;
        for (int i = 0; cameras is not null && i < cameras.Count; i++)
        {
            SecurityCamera camera = cameras[i];
            string name = camera.Task.Name;

            if (!Matches(needle, name, "camera"))
            {
                continue;
            }

            Vector3d at = camera.HeadPositionUnits / WorldScene.UnitsPerMetre;
            into.Add(new InspectableEntity(
                (KindCamera * KindStride) + i,
                "camera",
                name,
                at,
                camera.IsExploded ? "destroyed" : camera.IsDetection ? "alerted" : "watching",
                Distance(eye, at)));
        }
    }

    private void AddSpeakers(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        IReadOnlyList<AlarmSpeaker>? speakers = _alarmSpeakers?.Speakers;
        for (int i = 0; speakers is not null && i < speakers.Count; i++)
        {
            AlarmSpeaker speaker = speakers[i];
            string name = speaker.Task.Name;

            if (!Matches(needle, name, "speaker"))
            {
                continue;
            }

            Vector3d at = speaker.PositionUnits / WorldScene.UnitsPerMetre;
            into.Add(new InspectableEntity(
                (KindSpeaker * KindStride) + i,
                "speaker",
                name,
                at,
                speaker.IsDestroyed ? "destroyed" : "alarm",
                Distance(eye, at)));
        }
    }

    private void AddLadders(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        IReadOnlyList<LadderPlacement>? ladders = _scene?.Ladders;
        for (int i = 0; ladders is not null && i < ladders.Count; i++)
        {
            LadderPlacement ladder = ladders[i];

            if (!Matches(needle, "ladder", "ladder"))
            {
                continue;
            }

            Vector3d ladderAt = Metres(ladder.Origin);
            into.Add(new InspectableEntity(
                (KindLadder * KindStride) + i,
                "ladder",
                $"ladder {i}",
                ladderAt,
                "climbable",
                Distance(eye, ladderAt)));
        }
    }

    /// <summary>
    /// Adds the terrain as a single entry.
    /// </summary>
    /// <remarks>
    /// One row rather than one per tile: the terrain is a single thing from the reader's point of
    /// view, and its per-frame chunk and triangle counts are what anyone opening this actually wants
    /// to know about it.
    /// </remarks>
    private void AddTerrain(List<InspectableEntity> into, string needle, Vector3d eye)
    {
        if (_terrain is null || !Matches(needle, "terrain", "terrain"))
        {
            return;
        }

        into.Add(new InspectableEntity(
            KindTerrain * KindStride,
            "terrain",
            "terrain",
            default,
            _terrain.Summary,
            Distance(eye, default)));
    }

    // ---- Detail ----

    private void DetailPlayer(List<string> into)
    {
        if (_player is null)
        {
            return;
        }

        into.Add(Position(_player.Position));
        into.Add(Line($"yaw {_player.Body.Yaw:0.00}   pitch {_player.Body.Pitch:0.00}"));
        into.Add(Line($"eye {_player.Camera.Position.Z:0.0} m   mode {(_playerMode ? "walking" : "free camera")}"));
        into.Add(Line($"dead {(_playerDead ? "yes" : "no")}   ladder {(_player.IsOnLadder ? "yes" : "no")}"));
    }

    private void DetailSoldier(List<string> into, int index)
    {
        if (index < 0 || index >= _inspectorSoldiers.Count)
        {
            return;
        }

        SoldierSnapshot soldier = _inspectorSoldiers[index];
        into.Add(Position(soldier.Position));
        into.Add(Line($"animation {soldier.Animation}   health {FormatHealth(soldier.HealthFraction)}"));
        into.Add(Line($"dead {(soldier.IsDead ? "yes" : "no")}   cutscene actor {(soldier.IsCutsceneActor ? "yes" : "no")}"));
    }

    private void DetailPlacement(List<string> into, int index)
    {
        IReadOnlyList<WorldPlacement>? placements = _scene?.Placements;
        if (placements is null || index < 0 || index >= placements.Count)
        {
            return;
        }

        WorldPlacement placement = placements[index];
        into.Add(Position(placement.Position));
        into.Add(Line($"model {placement.Instance.Name}   pack {placement.Instance.Model}"));
        into.Add(Line($"task {placement.TaskName}   type {placement.Owner.Type}"));
        into.Add(Line($"lightmap {(placement.HasLightmap ? placement.LightmapEntryName() : "none")}"));
        into.Add(Line($"attached {(placement.IsAttached ? "yes" : "no")}   rotor {(placement.IsRotor ? "yes" : "no")}"));
    }

    private void DetailDoor(List<string> into, int index)
    {
        IReadOnlyList<PlacedDoor>? doors = _doors?.Doors;
        if (doors is null || index < 0 || index >= doors.Count)
        {
            return;
        }

        PlacedDoor door = doors[index];
        into.Add(Position(door.DrawPosition));
        into.Add(Line($"angle {door.State.AngleDegrees:0.0}°   slide {door.State.SlideFraction:0.00}"));
        into.Add(Line($"open {(door.State.IsFullyOpen ? "yes" : "no")}   closed {(door.State.IsFullyClosed ? "yes" : "no")}"));
        into.Add(Line($"sounding {(door.IsSounding ? "yes" : "no")}"));
    }

    private void DetailElevator(List<string> into, int index)
    {
        IReadOnlyList<PlacedElevator>? elevators = _elevators?.Elevators;
        if (elevators is null || index < 0 || index >= elevators.Count)
        {
            return;
        }

        PlacedElevator elevator = elevators[index];
        into.Add(Position(elevator.DrawPosition));
        into.Add(Line($"floor {elevator.State.CurrentFloorIndex} of {elevator.State.FloorCount}   moving {(elevator.State.IsMoving ? "yes" : "no")}"));
        into.Add(Line($"model {elevator.Model}   sounding {(elevator.IsSounding ? "yes" : "no")}"));
    }

    private void DetailInteraction(List<string> into, int index)
    {
        IReadOnlyList<WorldInteraction>? items = _interactions?.Items;
        if (items is null || index < 0 || index >= items.Count)
        {
            return;
        }

        WorldInteraction interaction = items[index];
        into.Add(Position(Metres(interaction.Anchor)));
        into.Add(Line($"type {interaction.Type}   duration {interaction.DurationTicks} tick(s)"));
        into.Add(Line($"sprite {interaction.ActionSprite}   pickable {(interaction.IsPickable ? "yes" : "no")}"));
    }

    private void DetailPickup(List<string> into, int kind, int index)
    {
        if (kind == KindWeaponPickup)
        {
            IReadOnlyList<GunPickup>? guns = _gunPickups?.Items;
            if (guns is null || index < 0 || index >= guns.Count)
            {
                return;
            }

            GunPickup gun = guns[index];
            into.Add(Position(Metres(gun.Position)));
            into.Add(Line($"weapon {gun.Weapon}   taken {(gun.IsCollected ? "yes" : "no")}"));
            return;
        }

        IReadOnlyList<GenericPickup>? items = _genericPickups?.Items;
        if (items is null || index < 0 || index >= items.Count)
        {
            return;
        }

        GenericPickup pickup = items[index];
        into.Add(Position(Metres(pickup.Position)));
        into.Add(Line($"task {pickup.Task.Name}   taken {(pickup.IsCollected ? "yes" : "no")}"));
    }

    private void DetailCamera(List<string> into, int index)
    {
        IReadOnlyList<SecurityCamera>? cameras = _securityCameras?.Cameras;
        if (cameras is null || index < 0 || index >= cameras.Count)
        {
            return;
        }

        SecurityCamera camera = cameras[index];
        into.Add(Position(camera.HeadPositionUnits / WorldScene.UnitsPerMetre));
        into.Add(Line($"active {(camera.IsActive ? "yes" : "no")}   moving {(camera.IsMoving ? "yes" : "no")}"));
        into.Add(Line($"detecting {(camera.IsDetection ? "yes" : "no")}   destroyed {(camera.IsExploded ? "yes" : "no")}"));
        into.Add(Line($"model {camera.HeadModelName}   task {camera.Task.Name}"));
    }

    private void DetailSpeaker(List<string> into, int index)
    {
        IReadOnlyList<AlarmSpeaker>? speakers = _alarmSpeakers?.Speakers;
        if (speakers is null || index < 0 || index >= speakers.Count)
        {
            return;
        }

        AlarmSpeaker speaker = speakers[index];
        into.Add(Position(speaker.PositionUnits / WorldScene.UnitsPerMetre));
        into.Add(Line($"siren {speaker.SirenSound}   destroyed {(speaker.IsDestroyed ? "yes" : "no")}"));
        into.Add(Line($"task {speaker.Task.Name}"));
    }

    private void DetailLadder(List<string> into, int index)
    {
        IReadOnlyList<LadderPlacement>? ladders = _scene?.Ladders;
        if (ladders is null || index < 0 || index >= ladders.Count)
        {
            return;
        }

        LadderPlacement ladder = ladders[index];
        into.Add(Position(Metres(ladder.Origin)));
        into.Add(Line($"bottom {Metres(ladder.BottomMount).Z:0.0} m   top {Metres(ladder.TopMount).Z:0.0} m"));
        into.Add(Line($"facing {ladder.Facing:0.00}   player yaw {ladder.PlayerYaw:0.00}"));
    }

    private void DetailTerrain(List<string> into)
    {
        if (_terrain is null)
        {
            return;
        }

        // The presenter names its own counters. A terrain that streams no chunks has none of these to
        // report, and an overlay that demanded them would be one only I.G.I. 1 could fill.
        _terrain.Describe(into);
    }

    // ---- Shared ----

    private bool TryResolvePosition(int id, out Vector3d position)
    {
        int kind = id / KindStride;
        int index = id % KindStride;

        switch (kind)
        {
            case KindPlayer when _player is not null:
                position = _player.Position;
                return true;
            case KindSoldier when index >= 0 && index < _inspectorSoldiers.Count:
                position = _inspectorSoldiers[index].Position;
                return true;
            case KindObject when _scene is { } scene && index >= 0 && index < scene.Placements.Count:
                position = scene.Placements[index].Position;
                return true;
            case KindDoor when _doors is { } doors && index >= 0 && index < doors.Doors.Count:
                position = doors.Doors[index].DrawPosition;
                return true;
            case KindElevator when _elevators is { } lifts && index >= 0 && index < lifts.Elevators.Count:
                position = lifts.Elevators[index].DrawPosition;
                return true;
            case KindInteraction when _interactions is { } actions && index >= 0 && index < actions.Items.Count:
                position = Metres(actions.Items[index].Anchor);
                return true;
            case KindLadder when _scene is { } world && index >= 0 && index < world.Ladders.Count:
                position = Metres(world.Ladders[index].Origin);
                return true;
            default:
                position = default;
                return false;
        }
    }

    /// <summary>Decides whether an entry passes the filter box.</summary>
    /// <param name="needle">What the reader typed.</param>
    /// <param name="name">The entry's name.</param>
    /// <param name="kind">The entry's kind.</param>
    /// <returns><see langword="true"/> when the entry should be listed.</returns>
    /// <remarks>
    /// Matched against the kind as well as the name, so typing <c>door</c> narrows to the doors
    /// without anyone having to know what any of them are called — which is the whole reason a reader
    /// opens this panel rather than the variable browser.
    /// </remarks>
    private static bool Matches(string needle, string name, string kind) =>
        needle.Length == 0 ||
        name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
        kind.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Converts an authored world position into metres.</summary>
    /// <param name="units">The position in the level's own units.</param>
    /// <returns>The position in metres.</returns>
    /// <remarks>
    /// The level's data is authored in units and only some of the runtime types convert before
    /// exposing a position — doors and elevators do, soldiers and ladders and pickups do not. Mixing
    /// the two is not a rounding error but a factor of four thousand, which shows up as a soldier
    /// reported five million metres away, so everything is normalised here at the seam.
    /// </remarks>
    private static Vector3d Metres(Vector3d units) => units / WorldScene.UnitsPerMetre;

    private static double Distance(Vector3d from, Vector3d to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double dz = to.Z - from.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static string DescribeSoldier(in SoldierSnapshot soldier)
    {
        if (soldier.IsDead)
        {
            return "dead";
        }

        return soldier.HealthFraction is >= 0f and < 1f
            ? Line($"hurt {soldier.HealthFraction:0.00}")
            : "alive";
    }

    private static string DescribeDoor(PlacedDoor door)
    {
        if (door.State.IsFullyOpen)
        {
            return "open";
        }

        return door.State.IsFullyClosed ? "closed" : Line($"moving {door.State.AngleDegrees:0.0}°");
    }

    private static string FormatHealth(float fraction) =>
        fraction < 0f ? "n/a" : fraction.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Position(Vector3d at) =>
        Line($"position {at.X:0.0}, {at.Y:0.0}, {at.Z:0.0}");

    private static string Line(FormattableString text) =>
        FormattableString.Invariant(text);
}
