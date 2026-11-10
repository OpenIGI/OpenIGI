using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>The live pose and vehicle state of one task a cutscene moves, attaches to or targets.</summary>
internal sealed class CutsceneTransformState(Vector3d position, Matrix3f orientation) : Saves.IExchangedState
{
    // Not retail: Project I.G.I. kept no save games. Where it is and how it is turned, how it is moving and what drives it; a
    // vehicle's physics body is set down again from these on its next tick.
    void Saves.IExchangedState.Exchange(Saves.StateExchange exchange)
    {
        Position = exchange.Position(Position);
        PreviousPosition = exchange.Position(PreviousPosition);
        Orientation = exchange.Frame(Orientation);
        PreviousOrientation = exchange.Frame(PreviousOrientation);
        Euler = exchange.Vector(Euler);
        Speed = exchange.Real(Speed);
        IsDynamic = exchange.Bool(IsDynamic);
        TrackPosition = exchange.Real(TrackPosition);
        PreviousTrackPosition = exchange.Real(PreviousTrackPosition);
        Thrust = exchange.Real(Thrust);
        TargetThrust = exchange.Real(TargetThrust);
        OriginalThrust = exchange.Real(OriginalThrust);
        ThrustStep = exchange.Real(ThrustStep);
        MaxSpeed = exchange.Real(MaxSpeed);
        Control = exchange.Vector(Control);
        Steering = exchange.Real(Steering);
        TargetSteering = exchange.Real(TargetSteering);
        ParkingBrake = exchange.Bool(ParkingBrake);
        VehicleGateReleased = exchange.Bool(VehicleGateReleased);
        Heading = exchange.Real(Heading);
        Velocity = exchange.Position(Velocity);
        GroundContact = exchange.Bool(GroundContact);
        CollisionEnabled = exchange.Bool(CollisionEnabled);
        TerminalLandingOnTerrain = exchange.Bool(TerminalLandingOnTerrain);
        TerminalLandingLatched = exchange.Bool(TerminalLandingLatched);
        TerminalLandingTarget = exchange.Position(TerminalLandingTarget);
        TerminalLandingPosition = exchange.Position(TerminalLandingPosition);
        RotorPhase = exchange.Real(RotorPhase);
        RotorPhaseTick = exchange.Int(RotorPhaseTick);
        AngularMomentum = exchange.Vector(AngularMomentum);
        PhysicsTicks = exchange.Int(PhysicsTicks);
        if (exchange.IsReading)
        {
            DynamicRevision++;
            RetailResetPending = RetailBody is not null;
        }
    }

    public Vector3d Position { get; set; } = position;
    public Vector3d PreviousPosition { get; set; } = position;
    public Vector3d OriginPosition { get; } = position;
    public Matrix3f Orientation { get; set; } = orientation;
    public Matrix3f PreviousOrientation { get; set; } = orientation;
    public Matrix3f AuthoredOrientation { get; } = orientation;
    public Vector3f Euler { get; set; }
    public float Speed { get; set; }
    public bool IsDynamic { get; set; }
    public RailroadConsist? Consist { get; set; }
    public float TrackPosition { get; set; }
    public float PreviousTrackPosition { get; set; }
    public int DynamicRevision { get; set; }
    public int LastSimulationOrder { get; set; } = int.MinValue;
    public float Thrust { get; set; }
    public float TargetThrust { get; set; }
    public float OriginalThrust { get; set; }
    public float ThrustStep { get; set; }
    public float MaxSpeed { get; set; }
    public CutsceneTransformState? TrainRoot { get; set; }
    public int ConsistIndex { get; set; }
    public Vector3f Control { get; set; }
    public float Steering { get; set; }
    public float TargetSteering { get; set; }
    public bool ParkingBrake { get; set; }
    public CarPhysicsDefinition? CarPhysics { get; set; }

    /// <summary>The car's retail rigid body, once its controller's set-up packet has created it.</summary>
    public RetailCarBody? RetailBody { get; set; }

    /// <summary>Whether a set-up packet arrived before <see cref="CutsceneRuntime.ConfigureRetailCars"/> supplied the world.</summary>
    public bool RetailResetPending { get; set; }

    /// <summary>The helicopter's retail rigid body, once its AnimTask has first spoken.</summary>
    public RetailHeliBody? RetailHeli { get; set; }

    /// <summary>Gets or sets the plane's retail body, once its AnimTask has spoken.</summary>
    public RetailPlaneBody? RetailPlane { get; set; }

    public CarMotionState CarMotion { get; } = new();
    public PlanePhysicsDefinition? PlanePhysics { get; set; }
    public GameTask? VehicleLatchedObstacle { get; set; }
    public bool VehicleGateReleased { get; set; }
    public float Heading { get; set; }
    public Vector3d Velocity { get; set; }
    public HelicopterPhysicsDefinition? HelicopterPhysics { get; set; }
    public double GroundRestOffset { get; set; } = double.NaN;
    public bool GroundContact { get; set; }
    public bool GroundContactEventLogged { get; set; }
    public bool GroundContactLogged { get; set; }
    public bool CollisionEnabled { get; set; }
    public bool TerminalLandingOnTerrain { get; set; }
    public bool TerminalLandingLatched { get; set; }
    public bool TerminalLandingEventLogged { get; set; }
    public Vector3d? TerminalLandingTarget { get; set; }
    public Vector3d TerminalLandingPosition { get; set; }
    public float RotorPhase { get; set; }
    public int RotorPhaseTick { get; set; }
    public Vector3f AngularMomentum { get; set; }
    public int PhysicsTicks { get; set; }
    public float RotorAxialCoefficient { get; set; }
    public Vector3f QueuedRotorForce { get; set; }
    public Vector3f QueuedRotorTorque { get; set; }
}
