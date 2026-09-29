using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One vehicle gun's weapon model as its last update posed it (<c>0x470050</c>).</summary>
/// <param name="Vehicle">The vehicle carrying it.</param>
/// <param name="Index">Its index among the vehicle's guns.</param>
/// <param name="WeaponModel">The weapon object's model (<c>614_03_1</c> for a T80's cannon, <c>123_01_1</c> for its M2HB).</param>
/// <param name="PositionUnits">Where the weapon object sits, in world units (<c>+376</c>).</param>
/// <param name="Orientation">The weapon object's orientation (<c>+400</c>).</param>
/// <param name="FiredThisTick">Whether the gun put a round out on the last tick, which shows its gun flame.</param>
/// <param name="ShotSequence">A serial that advances once per round fired, for the flame's roll.</param>
public readonly record struct VehicleGunVisual(
    GameTask Vehicle,
    int Index,
    string WeaponModel,
    Vector3d PositionUnits,
    Matrix3f Orientation,
    bool FiredThisTick,
    int ShotSequence);
