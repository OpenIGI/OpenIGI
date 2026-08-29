using System.Text.Json;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Game.World;

/// <summary>
/// The lights a level has, named by hand rather than inferred.
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
/// <b>Additive, not exclusive.</b> Naming a model here makes it a lamp; it does not switch the detector
/// off for everything else. A level is large and a hand-written list will be partial for a long time,
/// so the two run together — and <c>lamp: false</c> is how a detection that fires on the wrong thing is
/// struck out without touching any code.
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

    private readonly Dictionary<int, List<Entry>> _byMission = [];

    private LampManifest()
    {
    }

    /// <summary>Gets a manifest that names nothing, leaving every level to the detector alone.</summary>
    public static LampManifest Empty { get; } = new();

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
    public readonly record struct Entry(
        string Model,
        IReadOnlyList<string> Textures,
        float Radiance,
        bool IsLamp,
        string Note);

    /// <summary>Reads a manifest.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="logger">Where to report a file that could not be read.</param>
    /// <returns>The manifest, or <see cref="Empty"/> when the file is missing or malformed.</returns>
    /// <remarks>
    /// A broken file never stops the game: every level falls back to the detector alone, which is the
    /// behaviour from before this file existed.
    /// </remarks>
    public static LampManifest Load(string path, IIgiLogger? logger = null)
    {
        IIgiLogger log = logger ?? NullLogger.Instance;

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

            if (document.RootElement.TryGetProperty("missions", out JsonElement missions)
                && missions.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty mission in missions.EnumerateObject())
                {
                    if (!int.TryParse(mission.Name, out int id) || mission.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    List<Entry> entries = [];

                    foreach (JsonElement element in mission.Value.EnumerateArray())
                    {
                        if (ReadEntry(element) is { } entry)
                        {
                            entries.Add(entry);
                        }
                    }

                    if (entries.Count > 0)
                    {
                        manifest._byMission[id] = entries;
                    }
                }
            }

            log.Information(
                LogCategory,
                $"Lamp manifest: {manifest._byMission.Count} mission(s) named by hand.");

            return manifest;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            log.Warning(LogCategory, $"Could not read {path}: {error.Message}. Lamps will be detected only.");
            return Empty;
        }
    }

    /// <summary>Gets what a person named for one mission.</summary>
    /// <param name="missionId">The mission.</param>
    /// <returns>Its entries, which may be empty.</returns>
    public IReadOnlyList<Entry> For(int missionId) =>
        _byMission.TryGetValue(missionId, out List<Entry>? entries) ? entries : [];

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
            element.TryGetProperty("note", out JsonElement note) ? note.GetString() ?? string.Empty : string.Empty);
    }
}
