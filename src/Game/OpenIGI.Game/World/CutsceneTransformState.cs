using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>The live pose and vehicle state of one task a cutscene moves, attaches to or targets.</summary>
internal sealed class CutsceneTransformState(Vector3d position, Matrix3f orientation)
{
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
