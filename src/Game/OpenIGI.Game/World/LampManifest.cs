using System.Text.Json;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Game.World;

/// <summary>
/// The lights the game has, named by hand rather than inferred.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an authored list exists at all.</b> I.G.I. declares no lights, so <see cref="LampDetector"/>
/// works out which geometry is a fitting by its shape and its texture. That inference is good but it is
/// still an inference: it misses fittings whose model does not look the way it expects, and it
/// occasionally lights furniture. Somebody who has walked the level with the target readout open knows
/// the answer outright, and this is where they write it down.
/// </para>
/// <para>
/// <b>One list for the whole game, not one per level.</b> A model is a global asset: <c>226_01_1</c> is
/// the same ceiling fitting in all fourteen missions, and it binds the same textures in every one of
/// them — verified for every model the game names a light, across all fourteen. Nothing about a lamp
/// changes between missions, so keying this by mission only meant re-stating the same answer up to
/// fourteen times and, in practice, forgetting it thirteen: the file named ten models for mission 1 and
/// left every other level to the detector alone.
/// </para>
/// <para>
/// <b>Additive, not exclusive.</b> Naming a model here makes it a lamp; it does not switch the detector
/// off for everything else. A fitting modelled INTO a building shell — a garage's ceiling strip, an
/// elevator car's light — has no model of its own to name and is still found by shape, so the two run
/// together. <c>lamp: false</c> is how a detection that fires on the wrong thing is struck out without
/// touching any code.
/// </para>
/// <para>
/// <b>A model is not all lamp.</b> A vending machine is a cabinet with one lit panel and a monitor is a
/// desk with a screen on it, so an entry names which of the model's sub-meshes emit, by the texture each
/// draws with. Naming none lets the brightest sub-mesh be chosen, which is right for a fitting whose
/// glass is plainly its brightest part and wrong for the two cases above — hence the option.
/// </para>
/// <para>
/// Data rather than code, and loaded the way the material and fog files are, so a level can be
/// re-judged by editing a file rather than by rebuilding.
/// </para>
/// </remarks>
public sealed class LampManifest
{
    private const string LogCategory = "Emissive";

    private readonly List<Entry> _entries = [];

    private LampManifest()
    {
    }

    /// <summary>Gets a manifest that names nothing, leaving every level to the detector alone.</summary>
    public static LampManifest Empty { get; } = new();

    /// <summary>Gets what a person has named, which applies to every mission.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>One model a person has judged, and what they judged about it.</summary>
    /// <param name="Model">The instance name, as the target readout prints it.</param>
    /// <param name="Textures">
    /// Which of the model's sub-meshes emit, by the texture each draws with. Empty means the brightest
    /// sub-mesh of the model, which is the right answer for an ordinary fitting.
    /// </param>
    /// <param name="Radiance">
    /// How much light the fixture appears to give off. See <see cref="LampDetector.LampRadiance"/> for
    /// what the number means and why it is a radiance rather than a multiplier.
    /// </param>
    /// <param name="IsLamp">
    /// <see langword="false"/> to say this model is NOT a lamp, striking out a detection rather than
    /// adding one.
    /// </param>
    /// <param name="Note">What the model is, for whoever reads the file next.</param>
    /// <param name="Title">Which game the entry is about; unmarked means Project I.G.I.</param>
    public readonly record struct Entry(
        string Model,
        IReadOnlyList<string> Textures,
        float Radiance,
        bool IsLamp,
        string Note,
        string Title = DefaultTitle);

    /// <summary>The game an entry belongs to when it does not say.</summary>
    /// <remarks>
    /// <para>
    /// Every entry written before there was a second game is Project I.G.I.'s, so that is what an
    /// unmarked one means. It is not a formality: the two games name models out of the same numbering
    /// and DO collide. I.G.I. 2 places models called <c>207_01_1</c> and <c>208_01_1</c>, both of which
    /// are hand-named lamps here — of the first game's, not of its own — so an unfiltered list would
    /// have told the sequel that two of its objects glow, and bound them to textures they do not carry.
    /// </para>
    /// </remarks>
    public const string DefaultTitle = "igi1";

    /// <summary>Gets the entries that belong to one game.</summary>
    /// <param name="title">The game's id, as <c>GameTitle.Id</c> gives it.</param>
    /// <returns>The entries marked for that game, or marked for none.</returns>
    public IReadOnlyList<Entry> For(string title)
    {
        List<Entry> mine = [];
        foreach (Entry entry in _entries)
        {
            if (string.Equals(entry.Title, title, StringComparison.OrdinalIgnoreCase))
            {
                mine.Add(entry);
            }
        }

        return mine;
    }

    /// <summary>Reads a manifest.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="logger">Where to report a file that could not be read.</param>
    /// <returns>The manifest, or <see cref="Empty"/> when the file is missing or malformed.</returns>
    /// <remarks>
    /// A broken file never stops the game: every level falls back to the detector alone, which is the
    /// behaviour from before this file existed.
    /// </remarks>
    public static LampManifest Load(string path, IOpenIgiLogger? logger = null)
    {
        IOpenIgiLogger log = logger ?? NullLogger.Instance;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            LampManifest manifest = new();

            if (document.RootElement.TryGetProperty("lamps", out JsonElement lamps)
                && lamps.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in lamps.EnumerateArray())
                {
                    if (ReadEntry(element) is { } entry)
                    {
                        manifest._entries.Add(entry);
                    }
                }
            }

            log.Information(
                LogCategory,
                $"Lamp manifest: {manifest._entries.Count} model(s) named by hand.");

            return manifest;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            log.Warning(LogCategory, $"Could not read {path}: {error.Message}. Lamps will be detected only.");
            return Empty;
        }
    }

    private static Entry? ReadEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("model", out JsonElement model)
            || model.GetString() is not { Length: > 0 } name)
        {
            return null;
        }

        List<string> textures = [];

        if (element.TryGetProperty("textures", out JsonElement named) && named.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement texture in named.EnumerateArray())
            {
                if (texture.GetString() is { Length: > 0 } value)
                {
                    textures.Add(value);
                }
            }
        }

        return new Entry(
            name,
            textures,
            element.TryGetProperty("radiance", out JsonElement radiance)
                && radiance.ValueKind == JsonValueKind.Number
                    ? radiance.GetSingle()
                    : LampDetector.LampRadiance,
            !element.TryGetProperty("lamp", out JsonElement lamp) || lamp.ValueKind != JsonValueKind.False,
            element.TryGetProperty("note", out JsonElement note) ? note.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("title", out JsonElement title) && title.GetString() is { Length: > 0 } which
                ? which
                : DefaultTitle);
    }
}
