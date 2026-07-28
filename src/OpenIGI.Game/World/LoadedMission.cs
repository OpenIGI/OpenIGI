using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>A mission that has been loaded: its descriptor, its two packs, and what is still outstanding.</summary>
/// <remarks>
/// The original keeps the equivalent state as fields on its game object — the terrain at <c>+32</c>, the
/// common pack handle at <c>+36</c>, and so on. Holding it in one value instead makes it obvious what a
/// mission consists of, and what a partially implemented load is still missing.
/// </remarks>
/// <param name="Descriptor">The mission's descriptor.</param>
/// <param name="CommonPack">The per-location common pack, for example <c>location0</c>.</param>
/// <param name="LevelPack">The level's own pack, for example <c>level1</c>.</param>
/// <param name="PendingSteps">
/// The steps of the original's sequence this load did not perform, as human-readable descriptions. Empty
/// once M6 and M7 close.
/// </param>
public sealed record LoadedMission(
    MissionDescriptor Descriptor,
    LoadedPack CommonPack,
    LoadedPack LevelPack,
    IReadOnlyList<string> PendingSteps)
{
    /// <summary>Gets a value indicating whether every step of the original's sequence ran.</summary>
    public bool IsComplete => PendingSteps.Count == 0;

    /// <summary>Resolves a model id against the pack its <see cref="PackId.IsCommon"/> flag selects.</summary>
    /// <param name="model">The model id from an instance.</param>
    /// <returns>The pack that owns the id.</returns>
    public LoadedPack PackFor(PackId model) => model.IsCommon ? CommonPack : LevelPack;

    /// <inheritdoc/>
    public override string ToString() =>
        IsComplete ? $"{Descriptor} (complete)" : $"{Descriptor} ({PendingSteps.Count} step(s) pending)";
}
