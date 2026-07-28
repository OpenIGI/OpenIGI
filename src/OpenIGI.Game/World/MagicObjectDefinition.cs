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
public readonly record struct MagicObjectDefinition(string Name, string Model, int TaskType)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Name} -> {Model} (type {TaskType})";
}
