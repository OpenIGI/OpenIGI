namespace OpenIGI.Game.World;

/// <summary>
/// One entry of the magic-object table: the model and task type a model attachment of this name spawns.
/// </summary>
/// <remarks>
/// Published by the <c>DefineMagicObj</c> native (<c>0x4C4520</c>), which stores the name and model as
/// 16-byte fields and the task type as a 16-bit id. Retail defines 387 of them in
/// <c>magicobj/magicobj.qvm</c>, and in most the name and the model are the same string — an attachment
/// named <c>400_03_1</c> spawns the model <c>400_03_1</c>.
/// </remarks>
/// <param name="Name">The attachment name this definition answers to.</param>
/// <param name="Model">The model the spawned object draws, resolved as a LOD chain.</param>
/// <param name="TaskType">
/// The task type id the object is spawned with. The original allocates these at startup and exports them
/// to scripts as <c>TASKTYPE_*</c> constants, so the number means nothing outside one run; see
/// <see cref="MagicObjectRegistry"/>.
/// </param>
/// <param name="DestroyedModel">The model displayed after an explosive magic object is destroyed.</param>
/// <param name="ExplosionSound">The authored sound used by an explosive magic object, when present.</param>
/// <param name="ExplosionFireballs">The optional authored FireBall count for an explosive magic object.</param>
/// <param name="ExplosionFragments">The optional authored Fragment count for an explosive magic object.</param>
/// <param name="ExplosionDelayTicks">The authored simulation-tick delay before the explosive magic object emits its blast.</param>
/// <param name="ExplosionDamageFactor">The authored explosion damage scale.</param>
/// <param name="ExplosionFalloffMetres">The authored outer explosion falloff radius, in metres.</param>
/// <param name="ExplosionRadiusMetres">The authored full-damage explosion radius, in metres.</param>
/// <param name="ExplosionDamageThreshold">The authored damage required before the object explodes.</param>
/// <param name="Sound">The authored loop/rotation sound for a rotating magic object (e.g. radar dish), when present.</param>
/// <param name="Speed">The authored rotation speed in degrees per second (e.g. for <c>TASKTYPE_RADARDISH</c>).</param>
/// <param name="Acceleration">The authored rotation acceleration in degrees per second squared.</param>
public readonly record struct MagicObjectDefinition(
    string Name,
    string Model,
    int TaskType,
    string DestroyedModel = "",
    string ExplosionSound = "",
    float ExplosionFireballs = 0f,
    int ExplosionFragments = 0,
    int ExplosionDelayTicks = 0,
    float ExplosionDamageFactor = 0f,
    float ExplosionFalloffMetres = 0f,
    float ExplosionRadiusMetres = 0f,
    float ExplosionDamageThreshold = 0f,
    string Sound = "",
    float Speed = 0f,
    float Acceleration = 0f)
{
    /// <summary>Gets the gun a <c>TASKTYPE_AISTATIONARYGUN</c> attachment carries, or <see langword="null"/>.</summary>
    public MagicGunMount? Gun { get; init; }

    /// <summary>Gets a helicopter skid's lengths, for a <c>TASKTYPE_HELIWHEEL</c> object.</summary>
    public MagicHeliWheel? HeliWheel { get; init; }

    /// <summary>Gets a landing gear leg's record, for a <c>TASKTYPE_GEAR</c> object.</summary>
    public MagicGear? Gear { get; init; }

    /// <summary>Gets the angle a plane's nose-gear hatch shuts through, degrees, for a <c>TASKTYPE_HATCH</c> object (<c>0x436090</c>).</summary>
    public float? HatchDegrees { get; init; }

    /// <summary>Gets a plane control surface's kind and throw, for a <c>TASKTYPE_RUDDER</c> object (<c>0x436740</c>).</summary>
    public MagicRudder? Rudder { get; init; }

    /// <summary>Gets the angle a plane's canopy swings open through, degrees, for a <c>TASKTYPE_COCKPITSHIELD</c> object (<c>0x436090</c>).</summary>
    public float? CanopyDegrees { get; init; }

    /// <summary>Gets a rocket pod's least gap between salvoes, in ticks, for a <c>TASKTYPE_ROCKETPOD</c> object (<c>0x427D40</c>).</summary>
    public int? RocketPodDelayTicks { get; init; }

    /// <summary>Gets a sliding door's record, for a <c>TASKTYPE_HELIDOOR</c> object.</summary>
    public MagicHeliDoor? HeliDoor { get; init; }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} -> {Model} (type {TaskType})";
}

/// <summary>A helicopter skid's 8-byte record (<c>0x427240</c>), in world units.</summary>
/// <param name="Travel">Record <c>+0</c>: how far below the skid the ground can still carry it (L0).</param>
/// <param name="BottomOut">Record <c>+4</c>: the depth under which the skid has bottomed out (L1).</param>
public readonly record struct MagicHeliWheel(float Travel, float BottomOut);

/// <summary>A plane control surface's 8-byte record (<c>0x436740</c>), read from argument 3 on.</summary>
/// <param name="Kind">0 an aileron, 1 the left taileron, 2 the right, 3 the rudder.</param>
/// <param name="Degrees">How far a full control throws it, degrees.</param>
public readonly record struct MagicRudder(int Kind, float Degrees);

/// <summary>A helicopter's sliding door's 12-byte record (<c>0x4277C0</c>), read from argument 3 on.</summary>
/// <param name="Axis">The axis of the vehicle's frame it slides along: 0 X, 1 Y, 2 Z.</param>
/// <param name="SpeedUnitsPerTick">How far it slides a tick: the authored metres a second times 4096 over 30.</param>
/// <param name="OpenUnits">Where it stops open, along the axis: the authored metres times 4096.</param>
public readonly record struct MagicHeliDoor(int Axis, float SpeedUnitsPerTick, float OpenUnits);

/// <summary>A landing gear leg's 20-byte record (<c>0x435BD0</c>), read from argument 3 on.</summary>
/// <param name="FoldDegrees">Record <c>+0</c>: the angle the leg folds through when the gear comes up.</param>
/// <param name="Steers">Record <c>+4</c>: the leg turns with the yaw control while the gear is down (<c>0x4355A0</c>).</param>
/// <param name="Value8">Record <c>+8</c>, which the plane's physics does not read.</param>
/// <param name="Value12">Record <c>+12</c>, which the plane's physics does not read.</param>
/// <param name="Value16">Record <c>+16</c>, which the plane's physics does not read.</param>
public readonly record struct MagicGear(float FoldDegrees, bool Steers, float Value8, float Value12, float Value16);
