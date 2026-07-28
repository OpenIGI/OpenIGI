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

    /// <summary>The archive under a level's directory that holds its baked object lightmaps.</summary>
    public const string LightmapArchive = "lightmaps/lightmaps.res";

    /// <summary>Resolves a model id against the pack its <see cref="PackId.IsCommon"/> flag selects.</summary>
    /// <param name="model">The model id from an instance.</param>
    /// <returns>The pack that owns the id.</returns>
    public LoadedPack PackFor(PackId model) => model.IsCommon ? CommonPack : LevelPack;

    /// <summary>Resolves a model by name across both packs.</summary>
    /// <param name="name">The model name, for example <c>410_01_1</c>.</param>
    /// <param name="model">Receives the model when either pack holds it.</param>
    /// <returns><see langword="true"/> when the name resolves.</returns>
    /// <remarks>
    /// The level pack is asked first: it is opened second and its names shadow the common pack's, which is
    /// the same precedence the drawable scene uses.
    /// </remarks>
    public bool TryGetModel(string name, out MefModel? model) =>
        LevelPack.TryGetModel(name, out model) || CommonPack.TryGetModel(name, out model);

    /// <summary>Reads the level's baked object lightmaps.</summary>
    /// <param name="resources">Where the archive is read from.</param>
    /// <param name="archive">Receives the archive when the level has one.</param>
    /// <returns><see langword="true"/> when the archive is present and parses.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resources"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A level with no baked lightmaps is not an error — the original rebuilds them from scratch when the
    /// file is absent or stale, which is a tool path, not a game one.
    /// </remarks>
    public bool TryReadLightmapArchive(
        Engine.Resources.ResourceRegistry resources,
        out Formats.Ilff.IresArchive? archive)
    {
        ArgumentNullException.ThrowIfNull(resources);

        archive = null;
        if (!resources.TryLoad(LevelPack.Directory + LightmapArchive, out ReadOnlyMemory<byte> bytes))
        {
            return false;
        }

        archive = Formats.Ilff.IresArchive.Read(bytes);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        IsComplete ? $"{Descriptor} (complete)" : $"{Descriptor} ({PendingSteps.Count} step(s) pending)";
}
