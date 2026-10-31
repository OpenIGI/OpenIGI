using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>The level's lifts as the walk reaches each, and whoever stands in them.</summary>
public sealed partial class Igi1MissionTick
{
    // One lift's tick: its floor expressions and its move, then what the move carries -- its sound, its collision and the
    // player standing in it. Per lift, because a lift the walk has not reached yet this tick still holds last tick's move.
    private void StepElevator(ElevatorRegistry elevators, PlacedElevator elevator)
    {
        elevators.TickElevator(elevator);
        audio.UpdateElevatorSoundPositions();
        audio.UpdateScriptSoundPositions();

        // A staged soldier standing in a car rides it through the cutscene runtime's attachments, so the runtime is
        // told where the car now is.
        mission.Cutscenes?.SetPlatformPose(elevator.Task, elevator.State.Position, elevator.Orientation);
        if (mission.Solids is { } elevatorSolids)
        {
            elevators.ApplyCollision(elevatorSolids, elevator);
        }

        // The soldiers grounded on a graph authored in the cab ride it with their graph (0x4F9770, 0x460CF0).
        mission.Soldiers?.CarryOnPlatform(elevator.Task, elevator.State.Position - elevator.State.PreviousPosition);

        if (host.PlayerMode && mission.Player is { } passenger
            && ElevatorRegistry.TryGetPassengerDisplacement(elevator, passenger.Body.Position, mission.Solids, out Vector3d displacement))
        {
            passenger.Body.Position += displacement;
        }

        // A co-op partner standing in it rides it on the server as its client's own copy rides it there: left behind, the
        // server put it back on the floor below tick after tick, and its client snapped between the two.
        foreach (Multiplayer.Igi1NetworkPlayer partner in mission.NetworkPlayers)
        {
            if (ElevatorRegistry.TryGetPassengerDisplacement(elevator, partner.Player.Body.Position, mission.Solids, out Vector3d carried))
            {
                partner.Player.Body.Position += carried;
            }
        }
    }
}
