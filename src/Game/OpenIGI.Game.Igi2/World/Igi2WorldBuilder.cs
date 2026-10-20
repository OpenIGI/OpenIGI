using OpenIGI.Formats.Ilff;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>Walks an I.G.I. 2 level into placements.</summary>
/// <remarks>
/// <para>
/// Three things the first game's walk does not do, and all three are why this is a seam rather than a
/// parameter.
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Models resolve through a table over every open pack, in load order.</b>
/// <c>Game_CreateHandler</c> (<c>igi2ed 0x4469A0</c>) opens <c>common</c>, then <c>new</c>, then the
/// location's, then the level's — and <c>MTP_VirModel_GetPtr</c> (<c>0x41EED0</c>) returns the first
/// match walking from the head, so <b>common shadows the level</b>. That is the opposite of the first
/// game, where the level pack is opened second and wins.
/// </description></item>
/// <item><description>
/// <b>Lightmaps bind by task id</b>, out of the level's own archive, rather than by placement.
/// </description></item>
/// <item><description>
/// <b>Characters are skipped</b>, because their model is a character kind drawn by a character
/// presenter on a schedule the world pass does not own — the same division the first game draws under,
/// reached from a different set of type names.
/// </description></item>
/// </list>
/// <para>
/// The fifth <c>MTP_Load</c> in that handler is the <c>editor</c> pack, loaded from
/// <c>LOCAL:editor/</c> under a "Loading editor..." caption. It is the editor's own and no part of the
/// game, which is why four is the number here and not five.
/// </para>
/// </remarks>
public sealed class Igi2WorldBuilder : ITitleWorldBuilder
{
    /// <inheritdoc/>
    public WorldBuildResult Build(in WorldBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Mission);
        ArgumentNullException.ThrowIfNull(request.LevelRoot);
        ArgumentNullException.ThrowIfNull(request.Resources);

        IReadOnlyList<LoadedPack> packs = request.Mission.PacksInLoadOrder;
        List<MtpPack> mtp = new(packs.Count);
        foreach (LoadedPack pack in packs)
        {
            mtp.Add(pack.Pack);
        }

        Igi2ModelTable table = Igi2ModelTable.Create(mtp);

        // A level with no bake is not an error: the original rebuilds them when the archive is absent,
        // which is a tool path rather than a game one. The placements simply carry no lightmap name.
        Igi2LightmapIndex? lightmaps = request.Mission.TryReadLightmapArchive(request.Resources, out IresArchive? archive)
            && archive is not null
                ? Igi2LightmapIndex.Read(archive)
                : null;

        Igi2DrawableSceneResult result = Igi2DrawableScene.Build(
            request.Mission,
            request.LevelRoot,
            table,
            lightmaps,
            packs,
            request.Logger,
            request.Services is { } services && services.TryGet(out Igi2MagicObjTypes? magic) ? magic : null);

        // The ground the player stands on, from the same level the placements came from. Built with no
        // logger because the presenter builds it again to draw it and reports it there; one line about a
        // level's tiles is enough.
        Igi2TerrainSurface surface = Igi2TerrainSurface.Build(request.LevelRoot, request.Resources);
        Igi2Ground ground = new(surface);

        LoadedMission mission = request.Mission;
        return new WorldBuildResult(
            result.Scene,
            result.UnresolvedCount,
            result.LightmappedCount,
            ground.IsEmpty ? null : ground,
            Igi2RotateAttachments.Build(request.LevelRoot, result.Attachments),
            ObjectCollisionIndex.Build(
                result.Scene.Placements,
                AssetFormatProfile.Igi2.Collision,
                name => mission.TryGetModel(name, out MefModel? model) ? model : null));
    }

    /// <inheritdoc/>
    public override string ToString() => "I.G.I. 2 scene walk";
}
