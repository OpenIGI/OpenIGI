using OpenIGI.Formats.Animation;
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
/// <param name="Packs">
/// Every model pack the mission has open, in the order the game opens them, or empty for the two the
/// first game opens. <b>Order is behaviour</b>: a name that appears in more than one pack resolves to
/// the first that holds it, so the list is the precedence.
/// </param>
/// <param name="Terrain">
/// The level's terrain, or <see langword="null"/> when the loader was given no
/// <c>TerrainLoader</c> or the level ships none. Check
/// <see cref="TerrainData.IsUsable"/> before drawing it: level 14's files exist but are not live data.
/// </param>
public sealed record LoadedMission(
    MissionDescriptor Descriptor,
    LoadedPack CommonPack,
    LoadedPack LevelPack,
    IReadOnlyList<string> PendingSteps,
    TerrainData? Terrain = null,
    IReadOnlyList<LoadedPack>? Packs = null)
{
    /// <summary>Gets every pack the mission has open, in the order the game opens them.</summary>
    /// <remarks>
    /// <para>
    /// Project I.G.I. opens two — a per-location common pack and the level's — and a mission that names
    /// no list falls back to exactly those, in that order, which is what it has always used.
    /// </para>
    /// <para>
    /// I.G.I. 2 opens <b>four</b>, and that is read rather than assumed: <c>Game_CreateHandler</c>
    /// (<c>igi2ed 0x4469A0</c>) calls <c>MTP_Load</c> five times, for <c>common</c>, <c>new</c>, the
    /// location's, the level's and finally <c>editor</c> — the last under a "Loading editor..." caption
    /// and from <c>LOCAL:editor/</c>, so it is the editor's own and no part of the game. The first four
    /// are the game's, in that order, and <c>MTP_VirModel_GetPtr</c> returns the first match walking from
    /// the head, so the common pack shadows the level's rather than the other way about.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LoadedPack> PacksInLoadOrder =>
        Packs is { Count: > 0 } named ? named : [CommonPack, LevelPack];

    /// <summary>Gets a value indicating whether every step of the original's sequence ran.</summary>
    public bool IsComplete => PendingSteps.Count == 0;

    /// <summary>The archive under a level's directory that holds its baked object lightmaps.</summary>
    public const string LightmapArchive = "lightmaps/lightmaps.res";

    /// <summary>Resolves a model id against the pack its <see cref="PackId.IsCommon"/> flag selects.</summary>
    /// <param name="model">The model id from an instance.</param>
    /// <returns>The pack that owns the id.</returns>
    public LoadedPack PackFor(PackId model) => model.IsCommon ? CommonPack : LevelPack;

    /// <summary>Resolves a model by name across the packs.</summary>
    /// <param name="name">The model name, for example <c>410_01_1</c>.</param>
    /// <param name="model">Receives the model when a pack holds it.</param>
    /// <returns><see langword="true"/> when the name resolves.</returns>
    /// <remarks>
    /// The level pack is asked first: it is opened second and its names shadow the common pack's, which is
    /// the same precedence the drawable scene uses. Any other pack the mission opened is asked after both, the latest opened first.
    /// </remarks>
    public bool TryGetModel(string name, out MefModel? model)
    {
        if (LevelPack.TryGetModel(name, out model) || CommonPack.TryGetModel(name, out model))
        {
            return true;
        }

        foreach (LoadedPack pack in OtherPacks)
        {
            if (pack.TryGetModel(name, out model))
            {
                return true;
            }
        }

        model = null;
        return false;
    }

    /// <summary>Finds the instance a model is placed as, across the packs, with the pack that holds it.</summary>
    /// <param name="name">The instance's name, for example <c>003_01_1</c>.</param>
    /// <param name="pack">Receives the pack.</param>
    /// <param name="instance">Receives the instance.</param>
    /// <param name="modelled">Whether only an instance whose pack holds its model will do, as a weapon's part needs.</param>
    /// <param name="commonFirst">Whether the common pack is asked before the level's, as the first-person arms are read.</param>
    /// <returns><see langword="false"/> when no pack places it.</returns>
    /// <remarks>
    /// The same precedence <see cref="TryGetModel(string, out MefModel?)"/> uses -- the level's pack, the common one, then any other
    /// the mission opened -- so a body found in another pack is drawn with that pack's textures.
    /// </remarks>
    public bool TryFindInstance(
        string name,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LoadedPack? pack,
        out MtpInstance instance,
        bool modelled = false,
        bool commonFirst = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        LoadedPack first = commonFirst ? CommonPack : LevelPack;
        LoadedPack second = commonFirst ? LevelPack : CommonPack;
        foreach (LoadedPack candidate in OtherPacks.Prepend(second).Prepend(first))
        {
            foreach (MtpInstance placed in candidate.Instances)
            {
                if (string.Equals(placed.Name, name, StringComparison.OrdinalIgnoreCase) && (!modelled || candidate.TryGetModel(placed, out MefModel? _)))
                {
                    pack = candidate;
                    instance = placed;
                    return true;
                }
            }
        }

        pack = null;
        instance = default;
        return false;
    }

    /// <summary>Resolves the diffuse texture a model's material slot samples, across the packs.</summary>
    /// <param name="model">The model's name, as level data spells it.</param>
    /// <param name="materialSlot">The draw record's material slot.</param>
    /// <param name="textureName">Receives the texture's bare name.</param>
    /// <returns><see langword="false"/> when no pack answers.</returns>
    /// <remarks>The same precedence <see cref="TryGetModel(string, out MefModel?)"/> uses.</remarks>
    public bool TryGetTextureName(string model, int materialSlot, out string textureName)
    {
        if (LevelPack.TryGetTextureName(model, materialSlot, out textureName)
            || CommonPack.TryGetTextureName(model, materialSlot, out textureName))
        {
            return true;
        }

        foreach (LoadedPack pack in OtherPacks)
        {
            if (pack.TryGetTextureName(model, materialSlot, out textureName))
            {
                return true;
            }
        }

        textureName = string.Empty;
        return false;
    }

    /// <summary>Resolves an animation set by the skeleton id its own <c>BOSH</c> chunk declares.</summary>
    /// <param name="skeletonId">The skeleton id — a soldier's <c>Bone Heirachy</c> parameter.</param>
    /// <param name="set">Receives the set when a pack loaded it.</param>
    /// <returns><see langword="true"/> when a set with that skeleton id was loaded.</returns>
    /// <remarks>
    /// The original registers sets into global tables indexed by the id out of <c>BOSH</c>
    /// (<c>0x4D67A0</c>), so a set loaded later overwrites an earlier one with the same id. Packs open
    /// common-then-level, so asking the level pack first gives the same precedence.
    /// </remarks>
    public bool TryGetAnimationSet(int skeletonId, out BoneAnimationSet? set)
    {
        foreach (LoadedPack pack in OtherPacks.Prepend(CommonPack).Prepend(LevelPack))
        {
            foreach (BoneAnimationSet candidate in pack.AnimationSets)
            {
                if (candidate.Skeleton.Id == skeletonId)
                {
                    set = candidate;
                    return true;
                }
            }
        }

        set = null;
        return false;
    }

    // The packs a name is asked of after the level's and the common: any other the mission opened, the latest first. The first
    // game opens only the two, so its own missions never ask these. They are for a mission that opens more -- a guest of one game
    // in another's level, whose bodies come from several of its levels' packs -- and asking them only after both keeps every name
    // the two packs answer resolving as it always has.
    // Asked only once both have missed, so the first game's lookups cost what they always did.
    private IEnumerable<LoadedPack> OtherPacks =>
        PacksInLoadOrder.Reverse().Where(pack => !ReferenceEquals(pack, LevelPack) && !ReferenceEquals(pack, CommonPack));

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
