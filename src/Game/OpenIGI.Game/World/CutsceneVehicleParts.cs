using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>A helicopter's moving parts: its rocket pods and its sliding door (<c>0x4F4660</c> runs them).</summary>
/// <remarks>
/// <para>
/// A Heli's update (<c>0x4316D0</c>) tells its door to open or shut from its "Open door" expression, then runs its
/// children; its AnimTask's packet (<c>0x431B70</c>) runs the parts before the flight step, on every packet but the
/// set-up, and the set-up makes them again (<c>0x4F4820</c>, <c>0x4F4800</c>).
/// </para>
/// <para>
/// Only the Hind (<c>700_01_1</c>) carries either: four pods and one door.
/// </para>
/// </remarks>
internal sealed class CutsceneVehicleParts(CutsceneContext context, CutsceneVehicleArmament armament)
{
    /// <summary>The rocket a pod fires, <c>0x427A70</c>'s model.</summary>
    public const string RocketModel = "700_10_1";

    private const string RocketPodTaskTypeName = "TASKTYPE_ROCKETPOD";
    private const string HeliDoorTaskTypeName = "TASKTYPE_HELIDOOR";

    private readonly CutsceneContext _context = context;
    private readonly CutsceneVehicleArmament _armament = armament;
    private readonly Dictionary<GameTask, HeliParts> _byVehicle = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WorldPlacement, RocketPod> _pods = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WorldPlacement, SlidingDoor> _doors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WorldPlacement, (PlanePartKind Kind, int Index)> _planeParts = new(ReferenceEqualityComparer.Instance);

    private enum PlanePartKind
    {
        Gear,
        Hatch,
        Canopy,
        Rudder,
    }

    /// <summary>Gets or sets the game's stream, which a pod draws its gaps from.</summary>
    public IgiRandom? Random { get; set; }

    /// <summary>Finds every helicopter's pods and door among the level's placements.</summary>
    /// <param name="scene">The built world, whose placements carry the helicopters' attachments.</param>
    /// <param name="magic">Resolves an attachment's definition, or <see langword="null"/>.</param>
    /// <param name="taskTypeName">Names a definition's task type.</param>
    public void Configure(WorldScene scene, Func<string, MagicObjectDefinition?> magic, Func<MagicObjectDefinition, string> taskTypeName)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(taskTypeName);
        _byVehicle.Clear();
        _pods.Clear();
        _doors.Clear();
        _planeParts.Clear();
        Dictionary<GameTask, int> legs = new(ReferenceEqualityComparer.Instance);
        Dictionary<GameTask, int> surfaces = new(ReferenceEqualityComparer.Instance);
        foreach (WorldPlacement placement in scene.Placements)
        {
            // A plane's legs, hatch and canopy, which its body poses; its legs in attachment order, as its body holds them.
            if (string.Equals(placement.Owner.Type.Name, "Plane", StringComparison.Ordinal))
            {
                if (placement.MagicTaskTypeName == "TASKTYPE_GEAR")
                {
                    int leg = legs.GetValueOrDefault(placement.Owner);
                    legs[placement.Owner] = leg + 1;
                    _planeParts[placement] = (PlanePartKind.Gear, leg);
                }
                else if (placement.MagicTaskTypeName == "TASKTYPE_HATCH")
                {
                    _planeParts[placement] = (PlanePartKind.Hatch, 0);
                }
                else if (placement.MagicTaskTypeName == "TASKTYPE_RUDDER")
                {
                    int surface = surfaces.GetValueOrDefault(placement.Owner);
                    surfaces[placement.Owner] = surface + 1;
                    _planeParts[placement] = (PlanePartKind.Rudder, surface);
                }
                else if (placement.MagicTaskTypeName == "TASKTYPE_COCKPITSHIELD")
                {
                    _planeParts[placement] = (PlanePartKind.Canopy, 0);
                }

                continue;
            }

            if (placement.MagicTaskTypeName is not (RocketPodTaskTypeName or HeliDoorTaskTypeName)
                || !string.Equals(placement.Owner.Type.Name, "Heli", StringComparison.Ordinal)
                || !_context.TaskTransforms.TryGetValue(placement.Owner, out CutsceneTransformState? transform)
                || magic(placement.Instance.Name) is not { } definition)
            {
                continue;
            }

            if (!_byVehicle.TryGetValue(placement.Owner, out HeliParts? parts))
            {
                _byVehicle[placement.Owner] = parts = new HeliParts();
            }

            if (taskTypeName(definition) == RocketPodTaskTypeName && definition.RocketPodDelayTicks is > 0 and int delay)
            {
                // The attachment in the helicopter's frame, as the level placed it on the helicopter's authored pose.
                Vector3d offset = transform.AuthoredOrientation.Transform(
                    (placement.Position * WorldScene.UnitsPerMetre) - transform.OriginPosition);
                RocketPod pod = new(offset, transform.AuthoredOrientation.Transposed() * placement.Orientation, delay);
                parts.Pods.Add(pod);
                _pods[placement] = pod;
            }
            else if (taskTypeName(definition) == HeliDoorTaskTypeName && definition.HeliDoor is { } record)
            {
                SlidingDoor door = new(record);
                parts.Doors.Add(door);
                _doors[placement] = door;
            }
        }
    }

    /// <summary>Makes a part as the level is made, drawing what it draws (<c>0x427940</c>).</summary>
    /// <param name="placement">A placement of the level, in the order its objects are made.</param>
    public void Made(WorldPlacement placement)
    {
        if (_pods.TryGetValue(placement, out RocketPod? pod) && Random is { } random)
        {
            pod.Create(random);
        }
    }

    /// <summary>Tells a helicopter's door to open or shut from its "Open door" expression (<c>0x4316D0</c>).</summary>
    /// <param name="heli">The Heli task.</param>
    public void TickDoor(GameTask heli)
    {
        if (!_byVehicle.TryGetValue(heli, out HeliParts? parts) || parts.Doors.Count == 0 || heli is not DeclaredTask declared)
        {
            return;
        }

        string expression = TextOrEmpty(declared, "Open door");
        if (expression.Length == 0)
        {
            return;
        }

        // 0x431AC0 finds the first door among the parts.
        parts.Doors[0].IsOpen = _context.Expressions.EvaluateCondition(expression, emptyValue: false, heli);
    }

    /// <summary>Makes a helicopter's parts again, as its flight's set-up packet does.</summary>
    /// <param name="heli">The Heli task.</param>
    public void Remake(GameTask heli)
    {
        if (!_byVehicle.TryGetValue(heli, out HeliParts? parts))
        {
            return;
        }

        foreach (SlidingDoor door in parts.Doors)
        {
            door.Create();
        }

        if (Random is { } random)
        {
            foreach (RocketPod pod in parts.Pods)
            {
                pod.Create(random);
            }
        }
    }

    /// <summary>Runs a helicopter's parts, as a flight packet does before its step.</summary>
    /// <param name="heli">The Heli task.</param>
    /// <param name="transform">Its pose as its last step left it.</param>
    public void Run(GameTask heli, CutsceneTransformState transform)
    {
        if (!_byVehicle.TryGetValue(heli, out HeliParts? parts))
        {
            return;
        }

        foreach (SlidingDoor door in parts.Doors)
        {
            door.Run();
        }

        if (Random is not { } random || parts.Pods.Count == 0)
        {
            return;
        }

        Vector3d[] enemies = [.. _armament.EnemiesSeenBy(heli)];
        foreach (RocketPod pod in parts.Pods)
        {
            (Vector3d position, Matrix3f orientation) = pod.Pose(transform.Position, transform.Orientation);
            pod.Run(
                random,
                position,
                orientation,
                transform.Speed,
                enemies,
                (from, to) => _armament.PartSees(heli, from, to),
                launch => _armament.FireRocket(heli, new VehicleCombatShot(
                    launch.Origin,
                    launch.Target,
                    launch.Orientation.Transposed().Transform(Vector3f.UnitY),
                    MissileType.HindHelicopter,
                    launch.SpeedMetresPerSecond,
                    RocketModel)
                {
                    Owner = heli,
                }));
        }
    }

    /// <summary>Gets how far a drawn door placement has slid, in its helicopter's frame, world units.</summary>
    /// <param name="placement">The candidate placement.</param>
    /// <param name="slide">Receives the slide.</param>
    /// <returns><see langword="true"/> when the placement is a door.</returns>
    public bool TryGetDoorSlide(WorldPlacement placement, out Vector3d slide)
    {
        if (_doors.TryGetValue(placement, out SlidingDoor? door))
        {
            slide = door.Displacement;
            return true;
        }

        slide = default;
        return false;
    }

    /// <summary>Poses a drawn leg, hatch, canopy or control-surface placement of a plane where its body last placed the part.</summary>
    /// <param name="placement">The candidate placement.</param>
    /// <param name="position">Receives its position, metres.</param>
    /// <param name="orientation">Receives its orientation.</param>
    /// <returns><see langword="true"/> when the placement is such a part of a plane flying on its retail body.</returns>
    public bool TryGetPlanePartDraw(WorldPlacement placement, out Vector3d position, out Matrix3f orientation)
    {
        RetailPlanePart? part = null;
        if (_planeParts.TryGetValue(placement, out (PlanePartKind Kind, int Index) entry)
            && _context.TaskTransforms.TryGetValue(placement.Owner, out CutsceneTransformState? state)
            && state.RetailPlane is { } plane)
        {
            part = entry.Kind switch
            {
                PlanePartKind.Gear => entry.Index < plane.Gears.Count ? plane.Gears[entry.Index] : null,
                PlanePartKind.Hatch => plane.Hatch,
                PlanePartKind.Rudder => entry.Index < plane.Rudders.Count ? plane.Rudders[entry.Index] : null,
                _ => plane.Shield,
            };
        }

        if (part is null)
        {
            position = default;
            orientation = Matrix3f.Identity;
            return false;
        }

        position = new Vector3d(part.World[0], part.World[1], part.World[2]) / WorldScene.UnitsPerMetre;
        orientation = part.DrawOrientation;
        return true;
    }

    private sealed class HeliParts
    {
        public List<RocketPod> Pods { get; } = [];

        public List<SlidingDoor> Doors { get; } = [];
    }
}
