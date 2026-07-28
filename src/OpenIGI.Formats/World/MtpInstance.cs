namespace OpenIGI.Formats.World;

/// <summary>
/// One placement record from a pack's <c>INST</c> chunk: a model plus the material slots its sub-meshes
/// draw with.
/// </summary>
/// <remarks>
/// A model's <c>REND</c> draw record carries a <c>materialSlot</c> that indexes
/// <see cref="MaterialIndices"/>; several records may share a slot, so the slot count and the model's
/// draw-record count are unrelated.2 and §4.4.
/// </remarks>
/// <param name="Name">The instance name from the <c>VNAM</c> pool, for example <c>1009_01_1</c>.</param>
/// <param name="Model">The model this instance draws, in the pack's <c>MODS</c> id space.</param>
/// <param name="MaterialIndices">One index into the pack's material table per material slot, in slot order.</param>
public readonly record struct MtpInstance(string Name, PackId Model, ReadOnlyMemory<int> MaterialIndices)
{
    /// <summary>Gets the number of material slots, the original's <c>nSubs</c>.</summary>
    public int SlotCount => MaterialIndices.Length;

    /// <inheritdoc/>
    public override string ToString() => $"{Name} model={Model} slots={SlotCount}";
}
