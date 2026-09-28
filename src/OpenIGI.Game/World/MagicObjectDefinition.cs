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

    /// <inheritdoc/>
    public override string ToString() => $"{Name} -> {Model} (type {TaskType})";
}
