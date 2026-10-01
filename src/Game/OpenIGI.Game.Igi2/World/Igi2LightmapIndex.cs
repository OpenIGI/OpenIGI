using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One baked lightmap a level ships, and which model of which object it belongs to.</summary>
/// <param name="ResourceName">The name the entry is registered under, ready to load.</param>
/// <param name="Variant">
/// Which of the object's model groups this belongs to: zero for a task with a single group, and
/// otherwise the position of the letter in <see cref="ObjectLightmap.VariantLetters"/>.
/// </param>
/// <param name="ModelIndex">The model's position in the object's enumeration, counted from zero.</param>
/// <param name="Lod">The level of detail, counted from one.</param>
public readonly record struct Igi2LightmapBinding(string ResourceName, int Variant, int ModelIndex, int Lod);

/// <summary>
/// A level's baked object lightmaps, indexed by the task each one belongs to.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. 2 does not name a lightmap after the object it lights. It names it after the object's
/// <b>level task id</b> — the first argument of <c>Task_New</c> — which is why binding lightmaps needs
/// the scene to carry that id and why <see cref="Igi2Placement.LevelId"/> exists.
/// </para>
/// <para>
/// The name is written by <c>LightmapInfo_AddLightmapVirModelToEnumeration</c>
/// (<c>igi2ed 0x625A20</c>) as <c>%04d_%04d_%1d.olm</c>, or <c>%04d%c_%04d_%1d.olm</c> when the object
/// has more than one group of models: a task id, an optional group letter, a model index and a level of
/// detail counted from one.
/// </para>
/// <para>
/// <b>This reads the index out of the archive rather than recomputing it.</b> The model index is a
/// running counter the original increments as it walks a task and its children
/// (<c>RigidDynCubeObj_EnumerateLightmapVirModelsHandler</c>, <c>igi2ed 0x6266E0</c>), and reproducing
/// it would mean reimplementing one enumeration handler per task type — for a number the shipped
/// archive already states. The task id is the part a renderer cannot get from the archive, and that is
/// the part this binds.
/// </para>
/// </remarks>
public sealed class Igi2LightmapIndex
{
    private readonly Dictionary<int, Igi2LightmapBinding[]> _byTask;

    private Igi2LightmapIndex(Dictionary<int, Igi2LightmapBinding[]> byTask, int entries)
    {
        _byTask = byTask;
        Count = entries;
    }

    /// <summary>Gets how many lightmap entries the level ships.</summary>
    public int Count { get; }

    /// <summary>Gets how many distinct objects have baked lightmaps.</summary>
    public int ObjectCount => _byTask.Count;

    /// <summary>Gets the level task id of every object with baked lightmaps.</summary>
    public IEnumerable<int> TaskIds => _byTask.Keys;

    /// <summary>The archive a level keeps its object lightmaps in, relative to the level directory.</summary>
    /// <remarks>
    /// The path on disk, which every level agrees on. What the entries inside are <em>named</em> does not
    /// agree — see <see cref="EntryName"/>.
    /// </remarks>
    public const string ArchivePath = "lightmaps/lightmaps.res";

    /// <summary>Indexes every <c>.olm</c> entry an archive holds.</summary>
    /// <param name="archive">The level's <c>lightmaps.res</c>.</param>
    /// <returns>The index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="archive"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Entries whose names do not parse are skipped rather than rejected. The archive is shared with
    /// whatever else a level bakes, and a renderer that refused to start because one name was unfamiliar
    /// would be stricter than the original, which simply looks a name up and finds nothing.
    /// </remarks>
    public static Igi2LightmapIndex Read(IresArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        Dictionary<int, List<Igi2LightmapBinding>> byTask = [];
        int entries = 0;

        foreach (IresEntry entry in archive.Entries)
        {
            if (!entry.Name.EndsWith(ObjectLightmap.Extension, StringComparison.OrdinalIgnoreCase)
                || !ObjectLightmap.TryParseIgi2Name(entry.Name, out int taskId, out int variant, out int modelIndex, out int lod))
            {
                continue;
            }

            entries++;
            if (!byTask.TryGetValue(taskId, out List<Igi2LightmapBinding>? bindings))
            {
                bindings = [];
                byTask[taskId] = bindings;
            }

            bindings.Add(new Igi2LightmapBinding(entry.Name, variant, modelIndex, lod));
        }

        Dictionary<int, Igi2LightmapBinding[]> sorted = new(byTask.Count);
        foreach ((int taskId, List<Igi2LightmapBinding> bindings) in byTask)
        {
            // Group, then model, then level of detail — the order the enumeration produced them in, so a
            // caller walking the array sees an object's models in the order the original would draw them.
            bindings.Sort(static (a, b) =>
            {
                int order = a.Variant.CompareTo(b.Variant);
                order = order != 0 ? order : a.ModelIndex.CompareTo(b.ModelIndex);
                return order != 0 ? order : a.Lod.CompareTo(b.Lod);
            });

            sorted[taskId] = [.. bindings];
        }

        return new Igi2LightmapIndex(sorted, entries);
    }

    /// <summary>Gets the lightmaps baked for one placement.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="bindings">Receives the bindings, ordered by group, then model, then level of detail.</param>
    /// <returns><see langword="false"/> when the object has no baked lightmaps.</returns>
    /// <remarks>
    /// A placement with no lightmaps is ordinary, not a fault: the original bakes them only for models
    /// whose mesh is <see cref="MefMeshFormat.Lightmapped"/> and which have triangles to light. A
    /// placement with no level id has none either, since the name is built from that id.
    /// </remarks>
    public bool TryGetBindings(Igi2Placement placement, [NotNullWhen(true)] out IReadOnlyList<Igi2LightmapBinding>? bindings)
    {
        bindings = null;
        if (_byTask.TryGetValue(placement.LevelId, out Igi2LightmapBinding[]? found))
        {
            bindings = found;
        }

        return bindings is not null;
    }

    /// <summary>Gets the lightmap for one level of detail of one of an object's models.</summary>
    /// <param name="placement">The placement.</param>
    /// <param name="variant">The model group, zero when the object has only one.</param>
    /// <param name="modelIndex">The model's position in the object's enumeration.</param>
    /// <param name="lod">The level of detail, counted from one.</param>
    /// <param name="resourceName">Receives the name the entry is registered under.</param>
    /// <returns><see langword="false"/> when the level bakes no such lightmap.</returns>
    public bool TryGetLightmap(
        Igi2Placement placement,
        int variant,
        int modelIndex,
        int lod,
        [NotNullWhen(true)] out string? resourceName)
    {
        resourceName = null;
        if (!TryGetBindings(placement, out IReadOnlyList<Igi2LightmapBinding>? bindings))
        {
            return false;
        }

        foreach (Igi2LightmapBinding binding in bindings)
        {
            if (binding.Variant == variant && binding.ModelIndex == modelIndex && binding.Lod == lod)
            {
                resourceName = binding.ResourceName;
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds the bare file name the original identifies a lightmap by.</summary>
    /// <param name="taskId">The object's level task id.</param>
    /// <param name="variant">The model group, or zero for an object with only one.</param>
    /// <param name="modelIndex">The model's position in the object's enumeration.</param>
    /// <param name="lod">The level of detail, counted from one.</param>
    /// <returns>The file name, with no directory and no alias.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A part is outside what the format can express.</exception>
    /// <remarks>
    /// <para>
    /// The inverse of <see cref="ObjectLightmap.TryParseIgi2Name"/>, and written to match the original's
    /// two format strings exactly: the lettered form is taken only when the group is non-zero, which is
    /// why index zero of <see cref="ObjectLightmap.VariantLetters"/> — the <c>!</c> — is never emitted.
    /// </para>
    /// <para>
    /// <b>Bare, not alias-qualified, and that is not a simplification.</b> The original sprintfs exactly
    /// this and looks it up in <c>LightmapInfo_ptLightmapVirModelHashTable</c>; it never builds a
    /// resource path. Nor could it usefully: 23 of the 24 shipped levels register their entries as
    /// <c>LOCAL:lightmaps/…</c>, but <c>location3/level1</c> registers
    /// <c>LOCAL:missions/location3/level1/lightmaps/…</c>. A renderer that constructed a resource name
    /// would work on every level but that one. So loading goes through
    /// <see cref="Igi2LightmapBinding.ResourceName"/>, which is whatever the archive actually says, and
    /// this method is for identity and diagnostics.
    /// </para>
    /// </remarks>
    public static string EntryName(int taskId, int variant, int modelIndex, int lod)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(taskId);
        ArgumentOutOfRangeException.ThrowIfNegative(variant);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(variant, ObjectLightmap.VariantLetters.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(modelIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(lod);

        return variant == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{taskId:D4}_{modelIndex:D4}_{lod:D1}.olm")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{taskId:D4}{ObjectLightmap.VariantLetters[variant]}_{modelIndex:D4}_{lod:D1}.olm");
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 lightmap index, {Count} maps over {ObjectCount} objects";
}
