using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>A live vehicle's sound inputs this tick: its engine sound set and the parameters its sounds read.</summary>
/// <param name="Vehicle">The Car or Heli task.</param>
/// <param name="SoundSet">
/// The set it instantiates: a car's record <c>+610</c> (<see cref="CarPhysicsDefinition.EngineSoundSet"/>), a
/// helicopter's hardcoded <c>helicopter_1</c> (<c>0x4316D0</c>).
/// </param>
/// <param name="PositionUnits">Where it is, in world units; its sounds follow it.</param>
/// <param name="Throttle">Parameter 0: a car's smoothed throttle (<c>+1824</c>), a helicopter's collective.</param>
/// <param name="Running">Parameter 1: 1 while parameter 0 is above 0.01, else 0.</param>
/// <param name="Speed">Parameter 2: a car's speed times 0.001, at most 1 (<c>Car_Tick</c> at <c>0x42EDF5</c>).</param>
/// <param name="SoundOn">Its "Play sound" expression: the set is deleted while it reads 0 (<c>0x42E320</c>, <c>0x4316D0</c>).</param>
public readonly record struct VehicleSoundSource(
    GameTask Vehicle,
    string SoundSet,
    Vector3d PositionUnits,
    float Throttle,
    float Running,
    float Speed,
    bool SoundOn);

/// <summary>One vehicle gun's sound inputs at the end of a tick.</summary>
/// <param name="Vehicle">The vehicle carrying it.</param>
/// <param name="Index">Its index among the vehicle's guns.</param>
/// <param name="PositionUnits">The gun's origin in world units.</param>
/// <param name="Hold">
/// The hold of <c>Gun_Fire</c> (<c>0x478BA0</c>): the trigger is held, the pull's burst is not spent and the magazine
/// is not empty. The cooldown between rounds is not part of it.
/// </param>
/// <param name="RoundsFired">How many rounds it fired this tick.</param>
/// <param name="Turning">Whether the mount is still traversing towards its aim (<c>0x46F390</c>).</param>
/// <param name="Weapon">The gun's weapon row, whose two fire sounds it plays.</param>
/// <param name="TurnSound">The mount's traverse loop (magic-object argument 10).</param>
public readonly record struct VehicleGunSound(
    GameTask Vehicle,
    int Index,
    Vector3d PositionUnits,
    bool Hold,
    int RoundsFired,
    bool Turning,
    WeaponType Weapon,
    string TurnSound);
