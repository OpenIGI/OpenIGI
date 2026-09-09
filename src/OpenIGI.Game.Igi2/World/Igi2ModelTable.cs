using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// What a level's <c>.mtp</c> packs turn a placed model name into: a mesh file, and the textures it
/// wears.
/// </summary>
/// <remarks>
/// <para>
/// A level does not name mesh files. It names <b>virtual models</b>, and a pack's <c>VNAM</c> and
/// <c>INST</c> chunks turn each one into a mesh from <c>MODS</c> plus a set of material slots. Several
/// virtual models routinely share a mesh: <c>location1</c>'s <c>rsg1_1</c>, <c>rsg2_1</c> and
/// <c>rsg3_1</c> all draw model <c>rsg_1</c> with texture sets <c>[2,3]</c>, <c>[2,4]</c> and
/// <c>[2,5]</c> — one soldier mesh, three uniforms. That is why a name like <c>rsg3_1</c> is nowhere on
/// disk while the level asks for it 900 times.
/// </para>
/// <para>
/// Two packs are consulted, nearest first: the level's own, then its location's common one. That is not
/// a convention invented here — <c>MTP_ParseMTP</c> (<c>igi2.exe 0x4F5DE0</c>) resolves a model index
/// below <see cref="PackId.CommonBase"/> against the pack being parsed and one at or above it against
/// the head of the pack list, which is the common pack. <see cref="PackId"/> already carries that rule.
/// </para>
/// <para>
/// The container is the same one Project I.G.I. uses, chunk for chunk, so
/// <see cref="MtpPack"/> reads both games unchanged. What differs is only how much of it a level leans
/// on: the first game names most of its meshes directly, and the sequel routes its characters through
/// the table.
/// </para>
/// </remarks>
public sealed class Igi2ModelTable
{
    private readonly IReadOnlyList<MtpPack> _packs;
    private readonly Dictionary<string, Entry> _byName;

    private Igi2ModelTable(IReadOnlyList<MtpPack> packs, Dictionary<string, Entry> byName)
    {
        _packs = packs;
        _byName = byName;
    }

    /// <summary>Gets how many virtual models the packs name between them.</summary>
    public int Count => _byName.Count;

    /// <summary>Builds a table over the packs a level has open, in the order the original loads them.</summary>
    /// <param name="packsInLoadOrder">
    /// The packs, nearest the front of the original's list first: the global common pack, then the
    /// <c>new</c> pack, then the location's, then the level's.
    /// </param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="packsInLoadOrder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">No packs were given, or one of them is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>The first pack to name a model wins</b>, which is the opposite of what this used to do.
    /// <c>MTP_VirModel_GetPtr</c> (<c>igi2.exe 0x4F4CA0</c>) walks <c>MTP_tMTPList</c> from the head and
    /// returns the first match; <c>MTP_Load</c> appends, so the head is the pack loaded first. A level
    /// therefore cannot override a name the global pack already has.
    /// </para>
    /// <para>
    /// The order comes from <c>Game_CreateHandler</c>, which loads four packs in a fixed sequence behind
    /// its own loading-screen captions: <c>common</c> ("Loading common resource archive..."), <c>new</c>,
    /// the location ("Loading LOCATION..."), then the level ("Loading LEVEL MTP...").
    /// </para>
    /// </remarks>
    public static Igi2ModelTable Create(IReadOnlyList<MtpPack> packsInLoadOrder)
    {
        ArgumentNullException.ThrowIfNull(packsInLoadOrder);
        if (packsInLoadOrder.Count == 0 || packsInLoadOrder.Any(static p => p is null))
        {
            throw new ArgumentException("At least one pack is needed, and none may be null.", nameof(packsInLoadOrder));
        }

        Dictionary<string, Entry> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (MtpPack pack in packsInLoadOrder)
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                // First wins: TryAdd, never the indexer.
                byName.TryAdd(instance.Name, new Entry(instance, pack));
            }
        }

        return new Igi2ModelTable([.. packsInLoadOrder], byName);
    }

    /// <summary>Turns a placed model name into the mesh file it draws.</summary>
    /// <param name="virtualModel">The name a placement carries.</param>
    /// <param name="mesh">Receives the mesh name, without a path or an extension.</param>
    /// <returns><see langword="false"/> when neither pack names it, or names a mesh it does not hold.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="virtualModel"/> is <see langword="null"/>.</exception>
    public bool TryResolveMesh(string virtualModel, [NotNullWhen(true)] out string? mesh)
    {
        ArgumentNullException.ThrowIfNull(virtualModel);

        mesh = null;
        if (!_byName.TryGetValue(virtualModel, out Entry entry) || entry.Instance.Model.IsNone)
        {
            return false;
        }

        // A local id resolves against the pack that owns the INST record, not against the level's --
        // MTP_ParseMTP walks ptMTP->tModelList, and ptMTP is the pack being parsed. Reading it as the
        // level's is wrong for every instance the common pack contributes, and wrong quietly: it lands on
        // a real mesh at that index and hands back the wrong model. It cost 358 unresolvable material
        // slots, which is how it surfaced -- the wrong mesh named slots its instance did not have.
        // A common id addresses the head of the pack list -- the global common pack -- and a local one the
        // pack that owns the INST record, which is not the same thing as the level's.
        MtpPack pack = entry.Instance.Model.IsCommon ? _packs[0] : entry.Pack;

        int index = entry.Instance.Model.Index;
        if (index < 0 || index >= pack.ModelNames.Count)
        {
            return false;
        }

        mesh = pack.ModelNames[index];
        return true;
    }

    /// <summary>Gets the instance record a placed model name resolves to.</summary>
    /// <param name="virtualModel">The name a placement carries.</param>
    /// <param name="instance">Receives the record, with its model id and material slots.</param>
    /// <returns><see langword="false"/> when neither pack names it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="virtualModel"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The material slots are what make two placements of one mesh look different, so a renderer wants
    /// the record and not just the mesh name.
    /// </remarks>
    public bool TryGetInstance(string virtualModel, out MtpInstance instance)
    {
        ArgumentNullException.ThrowIfNull(virtualModel);

        bool found = _byName.TryGetValue(virtualModel, out Entry entry);
        instance = entry.Instance;
        return found;
    }

    /// <summary>Gets the texture one of a placed model's material slots draws with.</summary>
    /// <param name="virtualModel">The name a placement carries.</param>
    /// <param name="materialSlot">The slot a mesh draw record names.</param>
    /// <param name="texture">Receives the texture's bare name, without a path or an extension.</param>
    /// <returns><see langword="false"/> when the slot draws untextured or names nothing the pack holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="virtualModel"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The walk is <see cref="MtpPack.TryGetTextureName"/>'s, shared with Project I.G.I., because both
    /// games resolve a slot through <c>GTT</c> to <c>TEXF</c> identically. What is the sequel's own is
    /// only <em>which</em> pack the slot is resolved against — the one that named the instance.
    /// </remarks>
    public bool TryResolveTexture(string virtualModel, int materialSlot, [NotNullWhen(true)] out string? texture)
    {
        ArgumentNullException.ThrowIfNull(virtualModel);

        texture = null;
        return _byName.TryGetValue(virtualModel, out Entry entry)
            && entry.Pack.TryGetTextureName(entry.Instance, materialSlot, out texture);
    }

    /// <summary>Gets the resource name a texture's pixels are registered under.</summary>
    /// <param name="texture">The bare texture name.</param>
    /// <returns>The alias-qualified resource name.</returns>
    /// <exception cref="ArgumentException"><paramref name="texture"/> is null or empty.</exception>
    public static string TextureResourceName(string texture)
    {
        ArgumentException.ThrowIfNullOrEmpty(texture);

        return string.Create(CultureInfo.InvariantCulture, $"LOCAL:textures/{texture}.tex");
    }

    /// <summary>A named instance together with the pack that named it.</summary>
    /// <remarks>
    /// A slot resolves against the <c>GTT</c> and <c>TEXF</c> of the pack the instance came from, so the
    /// two travel together. Recording it while the table is built is also what keeps a lookup a
    /// dictionary hit instead of a scan for the name across both packs.
    /// </remarks>
    private readonly record struct Entry(MtpInstance Instance, MtpPack Pack);

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 model table, {Count} virtual models over {_packs.Count} packs";
}
