using OpenIGI.Engine.Resources;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>
/// Every clip a level has loaded, filed by the rig it was authored on and by its own name: the hierarchy table
/// (<c>Hierarchy_ptHashTable</c>) the engine looks animations up in.
/// </summary>
/// <remarks>
/// <para>
/// A rig is identified by the shape of its tree alone, the child-count bytes of its hierarchy
/// (<c>Hierarchy_Compare</c>, <c>igi2 0x57EBB0</c>), so every model and every clip that shares a skeleton shares one
/// table. A bone model registers its rig as its pack's models load (<c>MTP_ModelLoad</c>, <c>0x4F4F00</c>); a clip
/// registers under its header's name with the rig it carries (<c>Hierarchy_LoadAnim</c>, <c>0x57F060</c>), and is
/// dropped when no model has registered that rig yet.
/// </para>
/// <para>
/// Each pack loads its models and then the clips its <c>BANM</c> chunk names, <c>LOCAL:anims/&lt;name&gt;.iff</c>
/// (<c>MTP_ParseMTP</c>, <c>0x4F5DE0</c>), packs in load order. A name is compared exactly, case and all, and the
/// first clip registered under a name is the one found (a chained table appending at the tail and searching from
/// the head).
/// </para>
/// </remarks>
public sealed class Igi2AnimationLibrary
{
    private readonly Dictionary<string, Dictionary<string, Igi2AnimationClip>> _rigs = new(StringComparer.Ordinal);

    /// <summary>Gets how many rigs are registered.</summary>
    public int RigCount => _rigs.Count;

    /// <summary>Gets how many clips are registered, across every rig.</summary>
    public int ClipCount => _rigs.Values.Sum(static r => r.Count);

    /// <summary>Loads the library a level's packs build, in their load order.</summary>
    /// <param name="packs">The packs, in load order.</param>
    /// <param name="resources">Where the packs' archives registered the clips.</param>
    /// <returns>The library.</returns>
    public static Igi2AnimationLibrary Load(IEnumerable<LoadedPack> packs, ResourceRegistry resources)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(resources);

        Igi2AnimationLibrary library = new();
        foreach (LoadedPack pack in packs)
        {
            foreach (string model in pack.Pack.ModelNames)
            {
                if (pack.TryGetArchiveModel(model, out MefModel? mef) && mef?.BoneHierarchy is { } rig)
                {
                    library.RegisterRig(rig);
                }
            }

            foreach (string clip in pack.Pack.AnimationSetNames)
            {
                if (resources.TryLoad(ClipResourceName(clip), out ReadOnlyMemory<byte> bytes))
                {
                    library.Register(Igi2AnimationClip.Read(bytes));
                }
            }
        }

        return library;
    }

    /// <summary>The resource a pack's clip is loaded from: <c>LOCAL:anims/&lt;name&gt;.iff</c>.</summary>
    /// <param name="clip">The name the pack's <c>BANM</c> chunk gives.</param>
    /// <returns>The resource name.</returns>
    public static string ClipResourceName(string clip) => $"LOCAL:anims/{clip}.iff";

    /// <summary>Registers a rig, as a bone model does when it loads.</summary>
    /// <param name="rig">The model's hierarchy.</param>
    public void RegisterRig(Igi2BoneHierarchy rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        _rigs.TryAdd(Key(rig), new Dictionary<string, Igi2AnimationClip>(StringComparer.Ordinal));
    }

    /// <summary>Registers a clip under its own name with the rig it was authored on.</summary>
    /// <param name="clip">The clip.</param>
    /// <returns><see langword="false"/> when its rig is not registered or the name is already taken there.</returns>
    public bool Register(Igi2AnimationClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return _rigs.TryGetValue(Key(clip.Hierarchy), out Dictionary<string, Igi2AnimationClip>? clips)
            && clips.TryAdd(clip.Name, clip);
    }

    /// <summary>Finds a rig's clip by name.</summary>
    /// <param name="rig">The rig.</param>
    /// <param name="name">The clip's name, compared exactly.</param>
    /// <returns>The clip, or <see langword="null"/>.</returns>
    public Igi2AnimationClip? Find(Igi2BoneHierarchy rig, string name)
    {
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(name);
        return _rigs.TryGetValue(Key(rig), out Dictionary<string, Igi2AnimationClip>? clips)
            && clips.TryGetValue(name, out Igi2AnimationClip? clip)
                ? clip
                : null;
    }

    private static string Key(Igi2BoneHierarchy rig) => Convert.ToHexString([.. rig.ChildCounts]);
}
