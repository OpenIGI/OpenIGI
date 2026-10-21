using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using static OpenIGI.Game.World.DeclaredTaskValues;

namespace OpenIGI.Game.World;

/// <summary>Captures each task's authored transform at load, the way each type's own set-up reads it.</summary>
/// <remarks>
/// A Human is remembered by reference and parented to the vehicle it sits in; a Train is laid on its railroad, a child
/// Train coupled behind its lead; a Car, Plane or Heli carries the physics record its model names and the authored
/// thrust its receiver restores on its first packet.
/// </remarks>
internal sealed class CutsceneTransformCapture(
    CutsceneContext context,
    CutsceneAttachments attachments,
    Func<string, float>? modelLength,
    Func<string, float>? modelPivot)
{
    private readonly CutsceneContext _context = context;
    private readonly CutsceneAttachments _attachments = attachments;
    private readonly Func<string, float>? _modelLength = modelLength;
    private readonly Func<string, float>? _modelPivot = modelPivot;

    /// <summary>Captures one task's authored transform, if it has one.</summary>
    public void Capture(GameTask task)
    {
        if (task is HumanSoldierTask soldier)
        {
            // Cabin copies ship with HumanSoldier id -1; the mind
            // id is on the child HumanAI. Skipping NoId left them unparented so they froze
            // while the airframe left. Remember by task reference and attach via QVM parent.
            Matrix3f orientation = Matrix3f.FromEngineEulerAngles(0f, 0f, soldier.Gamma);
            CutsceneTransformState humanState = new(soldier.Position, orientation);
            _context.Remember(task, humanState);
            _attachments.CaptureAttachment(task, humanState);
            return;
        }

        if (task is not DeclaredTask declared)
        {
            return;
        }

        if (string.Equals(task.Type.Name, "Train", StringComparison.Ordinal))
        {
            float length = Math.Max(0f, _modelLength?.Invoke(TextOrEmpty(declared, "Model")) ?? 0f);
            float pivot = Math.Max(0f, _modelPivot?.Invoke(TextOrEmpty(declared, "Model")) ?? 0f);

            // Retail's Train::UpdateConsist (0x42A2C0/0x42A7C0) places the whole consist from the lead's
            // scalar: one coupler per car walked along the rail's polyline (0x4FEC10), each car laid on the
            // chord between its pair at its own pivot fraction. A child Train couples behind the cars its
            // lead already has, and carries no rail position of its own.
            if (task.Parent is DeclaredTask parent
                && string.Equals(parent.Type.Name, "Train", StringComparison.Ordinal)
                && _context.TaskTransforms.TryGetValue(parent, out CutsceneTransformState? parentTrain)
                && parentTrain.TrainRoot is { Consist: { } consist } root)
            {
                int index = consist.Add(declared, length, pivot);
                consist.TryPlace(root.TrackPosition, index, out Vector3d childPosition, out Matrix3f childOrientation);
                CutsceneTransformState childState = new(childPosition, childOrientation)
                {
                    Consist = consist,
                    TrainRoot = root,
                    ConsistIndex = index,
                };
                _context.Remember(task, childState);
                return;
            }

            if (RailroadPath.FromTrain(declared) is { } railroad)
            {
                RailroadConsist cars = new(railroad);
                int lead = cars.Add(declared, length, pivot);
                cars.TryPlace(railroad.InitialPosition, lead, out Vector3d railPosition, out Matrix3f railOrientation);
                CutsceneTransformState trainState = new(railPosition, railOrientation)
                {
                    Consist = cars,
                    ConsistIndex = lead,
                    TrackPosition = railroad.InitialPosition,
                    PreviousTrackPosition = railroad.InitialPosition,
                    Thrust = RealStrictIgnoringSpaces(declared, "Original thrust"),
                    TargetThrust = RealStrictIgnoringSpaces(declared, "Original thrust"),
                    OriginalThrust = RealStrictIgnoringSpaces(declared, "Original thrust"),
                    ThrustStep = MathF.Abs(RealStrictIgnoringSpaces(declared, "ThrustStep")),
                    MaxSpeed = RealStrictIgnoringSpaces(declared, "MaxSpeed"),
                };
                trainState.TrainRoot = trainState;
                _context.Remember(task, trainState);
                return;
            }
        }

        Vector3d position = Vector(declared, "Position");
        Vector3f angles = Vector32(declared, "Orientation");
        CutsceneTransformState state = new(position, Matrix3f.FromEngineEulerAngles(angles.X, angles.Y, angles.Z))
        {
            // Vehicle AnimTask channels are deltas/controls. Preserve the authored starting attitude
            // when the first wait entry advances; beginning this accumulator at zero snapped linked
            // helicopters and planes to the identity matrix on cutscene tick one.
            Euler = angles,
            Heading = angles.Z,
        };
        if (string.Equals(task.Type.Name, "Car", StringComparison.Ordinal)
            && _context.VehiclePhysics?.TryGetCar(TextOrEmpty(declared, WorldScene.ModelParameter), out CarPhysicsDefinition car) == true)
        {
            state.CarPhysics = car;
            state.Steering = 0f;
            state.TargetSteering = 0f;

            // Car::ReadChannels restores both the live throttle and its target from this authored
            // value on animation tick zero, the way the Heli and Train receivers do. Mission 2's
            // truck opens its cutscene already carrying 0.6 and coasts down from it.
            state.OriginalThrust = RealStrictIgnoringSpaces(declared, "Original thrust");
            state.Thrust = state.OriginalThrust;
            state.TargetThrust = state.OriginalThrust;
        }
        if (string.Equals(task.Type.Name, "Plane", StringComparison.Ordinal)
            && _context.VehiclePhysics?.TryGetPlane(TextOrEmpty(declared, WorldScene.ModelParameter), out PlanePhysicsDefinition plane) == true)
        {
            state.PlanePhysics = plane;
        }
        if (string.Equals(task.Type.Name, "Heli", StringComparison.Ordinal))
        {
            // Heli::ReadChannels restores both live and target collective from this authored value on
            // animation tick zero. It then writes -1 into channel three so the value remains latched.
            state.Thrust = RealStrictIgnoringSpaces(declared, "Original Thrust");
            state.TargetThrust = state.Thrust;
            state.OriginalThrust = state.Thrust;
            string model = TextOrEmpty(declared, WorldScene.ModelParameter);
            if (_context.VehiclePhysics?.TryGetHelicopter(model, out HelicopterPhysicsDefinition physics) == true)
            {
                state.HelicopterPhysics = physics;
            }

            // The retail Heli live-update callback begins at 0x431B70 and invokes the validated
            // force helper 0x431E30. The authored collision flag is carried into the live body;
            // the contact solver below uses it only to prevent terrain penetration. The QVM
            // collective and cyclic controls remain free to lift and steer the airframe.
            state.CollisionEnabled = BoolStrict(declared, "Collision detection");
        }
        bool attached = _attachments.CaptureAttachment(task, state);
        if (task.Id != GameTask.NoId || attached)
        {
            _context.Remember(task, state);
        }
    }
}
