using OpenIGI.Formats;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;

namespace OpenIGI.TestSupport;

/// <summary>I.G.I. 2's shared model archives, <c>COMMON/models/{common,new}.res</c>, opened once.</summary>
/// <remarks>Every character model and every animation clip the game ships lives in these two.</remarks>
public static class Igi2CommonModels
{
    private static readonly string[] ArchiveNames = ["common.res", "new.res"];

    private static readonly Lazy<IReadOnlyList<IresArchive>> Loaded = new(static () =>
    {
        string models = Path.Combine(GameDataSource.Igi2.RequireRoot(), "COMMON", "models");
        return [.. ArchiveNames.Select(a => IresArchive.Read(File.ReadAllBytes(Path.Combine(models, a))))];
    });

    /// <summary>Gets the two archives, in load order.</summary>
    public static IReadOnlyList<IresArchive> Archives => Loaded.Value;

    /// <summary>Gets every animation clip, by file name.</summary>
    /// <returns>The clips, read.</returns>
    public static IEnumerable<(string Name, Igi2AnimationClip Clip)> Clips() =>
        Entries(".iff").Select(static e => (Path.GetFileNameWithoutExtension(e.Name), Igi2AnimationClip.Read(e.Data)));

    /// <summary>Gets every render model, by file name.</summary>
    /// <returns>The models, read under the sequel's format profile.</returns>
    /// <remarks>
    /// A <c>.mef</c> without a mesh header is a shadow volume kept beside the models, which the pack
    /// loader also sets aside; it is not a model and is skipped here.
    /// </remarks>
    public static IEnumerable<(string Name, MefModel Model)> Models() =>
        Entries(".mef")
            .Select(static e => (Entry: e, Container: IlffContainer.Read(e.Data)))
            .Where(static e => e.Container.TryFindChunk(MefTags.Mesh, out _))
            .Select(static e => (Path.GetFileNameWithoutExtension(e.Entry.Name), MefModel.FromContainer(e.Container, AssetFormatProfile.Igi2)));

    private static IEnumerable<IresEntry> Entries(string extension) =>
        Archives.SelectMany(static a => a.Entries).Where(e => e.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
