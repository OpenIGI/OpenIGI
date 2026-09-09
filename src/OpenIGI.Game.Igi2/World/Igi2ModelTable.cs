using System.Diagnostics.CodeAnalysis;
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
    private readonly MtpPack _level;
    private readonly MtpPack? _common;
    private readonly Dictionary<string, MtpInstance> _byName;

    private Igi2ModelTable(MtpPack level, MtpPack? common, Dictionary<string, MtpInstance> byName)
    {
        _level = level;
        _common = common;
        _byName = byName;
    }

    /// <summary>Gets how many virtual models the two packs name between them.</summary>
    public int Count => _byName.Count;

    /// <summary>Builds a table over a level's pack and the common pack behind it.</summary>
    /// <param name="level">The level's own pack.</param>
    /// <param name="common">The location's common pack, or null when the level has none.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="level"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The level's own names win, which is the order the original resolves in. A name in both packs is
    /// the level overriding what its neighbours share, and taking the level's is what makes that work.
    /// </remarks>
    public static Igi2ModelTable Create(MtpPack level, MtpPack? common)
    {
        ArgumentNullException.ThrowIfNull(level);

        Dictionary<string, MtpInstance> byName = new(StringComparer.OrdinalIgnoreCase);
        if (common is not null)
        {
            foreach (MtpInstance instance in common.Instances)
            {
                byName[instance.Name] = instance;
            }
        }

        foreach (MtpInstance instance in level.Instances)
        {
            byName[instance.Name] = instance;
        }

        return new Igi2ModelTable(level, common, byName);
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
        if (!_byName.TryGetValue(virtualModel, out MtpInstance instance) || instance.Model.IsNone)
        {
            return false;
        }

        MtpPack? pack = instance.Model.IsCommon ? _common : _level;
        if (pack is null)
        {
            return false;
        }

        int index = instance.Model.Index;
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
        return _byName.TryGetValue(virtualModel, out instance);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 model table, {Count} virtual models over {_level.ModelNames.Count} meshes";
}
