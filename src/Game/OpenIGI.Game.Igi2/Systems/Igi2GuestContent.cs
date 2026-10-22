using System.Text;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Titles;
using OpenIGI.Formats;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>
/// What every I.G.I. 2 soldier another game's level hosts is made of, read once a session from the I.G.I. 2 installation: its
/// tables, its packs' models and clips, its sounds and its generic scripts.
/// </summary>
/// <remarks>
/// <para>
/// The packs are opened in I.G.I. 2's own order (<c>Game_CreateHandler</c>): the global common pack, <c>new</c>, then each
/// location's pack that holds a model the host dresses a soldier in. A soldier's body is in its location's pack, the clips and
/// the rigs in the common one.
/// </para>
/// <para>
/// The sounds are I.G.I. 2's common ones and those locations': its mission sounds are a level's own, and the level here is the
/// host's.
/// </para>
/// </remarks>
public sealed class Igi2GuestContent
{
    private Igi2GuestContent(GuestTitle guest, IReadOnlyList<LoadedPack> packs, Igi2AnimationLibrary clips, Func<string, MefModel?> models, Igi2SoundDefinitions sounds, Igi2GenericScripts scripts)
    {
        Guest = guest;
        Packs = packs;
        Clips = clips;
        Models = models;
        SoundDefinitions = sounds;
        Scripts = scripts;
    }

    /// <summary>Gets I.G.I. 2, started as a guest.</summary>
    public GuestTitle Guest { get; }

    /// <summary>Gets the packs, in the order they were opened.</summary>
    public IReadOnlyList<LoadedPack> Packs { get; }

    /// <summary>Gets the clips the soldiers move by.</summary>
    public Igi2AnimationLibrary Clips { get; }

    /// <summary>Gets the models, by name.</summary>
    public Func<string, MefModel?> Models { get; }

    /// <summary>Gets the sounds' definitions.</summary>
    public Igi2SoundDefinitions SoundDefinitions { get; }

    /// <summary>Gets the directories whose sound archives and files the definitions play, in mount order.</summary>
    public IReadOnlyList<string> SoundBanks { get; init; } = [];

    /// <summary>Gets the scripts I.G.I. 2's own soldiers and squads mostly run.</summary>
    public Igi2GenericScripts Scripts { get; }

    /// <summary>Reads the content.</summary>
    /// <param name="dataRoot">Where I.G.I. 2 is installed.</param>
    /// <param name="models">The models the host dresses soldiers in, which decide which locations' packs are opened.</param>
    /// <param name="logger">Where diagnostics go.</param>
    /// <returns>The content.</returns>
    public static Igi2GuestContent Load(string dataRoot, IEnumerable<string> models, IOpenIgiLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataRoot);
        ArgumentNullException.ThrowIfNull(models);
        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        GuestTitle guest = GuestTitle.Start(new Igi2Runtime(), dataRoot, log);

        List<LoadedPack> packs =
        [
            LoadedPack.Open("LOCAL:common/", "common", guest.Resources, log, AssetFormatProfile.Igi2),
            LoadedPack.Open("LOCAL:common/", "new", guest.Resources, log, AssetFormatProfile.Igi2),
        ];
        List<string> soundDirectories = [Igi2SoundDefinitions.CommonDirectory];
        foreach (string location in Locations(dataRoot, [.. models]))
        {
            string directory = $"LOCAL:missions/{location}/common/";
            packs.Add(LoadedPack.Open(directory, location, guest.Resources, log, AssetFormatProfile.Igi2));
            soundDirectories.Add($"{directory}sounds");
        }

        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, guest.Resources);
        Func<string, MefModel?> byName = Igi2DrawableScene.Models(Igi2ModelTable.Create([.. packs.Select(static p => p.Pack)]), packs);
        Igi2SoundDefinitions sounds = guest.Session.Services.TryGet(out Igi2SoundDefinitions? defined) ? defined : new Igi2SoundDefinitions(log);
        sounds.Load(guest.Resources, soundDirectories);
        return new Igi2GuestContent(guest, packs, clips, byName, sounds, Igi2GenericScripts.Find(dataRoot))
        {
            // The banks the definitions play from: each sounds directory and its language's, as a mission of the game's own
            // mounts them (Igi2SoundDefinitions.BankDirectories).
            SoundBanks = [.. soundDirectories.SelectMany(static directory => new[] { directory, $"{directory}/{Igi2SoundDefinitions.DefaultLanguage}" })],
        };
    }

    // The locations whose packs name a model the host dresses a soldier in, in their own order.
    private static IEnumerable<string> Locations(string dataRoot, HashSet<string> wanted)
    {
        string missions = Path.Combine(dataRoot, "MISSIONS");
        foreach (string directory in Directory.EnumerateDirectories(missions, "location*").Order(StringComparer.OrdinalIgnoreCase))
        {
            string location = Path.GetFileName(directory).ToLowerInvariant();
            string pack = Path.Combine(directory, "common", $"{location}.mtp");
            if (File.Exists(pack) && wanted.Any(Encoding.Latin1.GetString(File.ReadAllBytes(pack)).Contains))
            {
                yield return location;
            }
        }
    }
}
