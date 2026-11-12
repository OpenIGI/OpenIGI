using System.Text.Json;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// The bomb the bomb modes plant and defuse, built into the game: I.G.I. 2's C4 -- the charge in the hand, the planted bomb
/// and its lower details, every texture they draw with, the plant, defuse and carry clips, the plant, tick and defuse sounds
/// and the icon -- so either title has it whatever is installed.
/// </summary>
/// <remarks>
/// The files are the sequel's own, copied out by <c>ResExtract kit</c>; <c>kit.json</c> beside them gives each model's
/// material slots the texture each draws with, since the pack that said so is not built in.
/// </remarks>
public static class BombKit
{
    /// <summary>The charge carried in the hand.</summary>
    public const string HeldModel = "113_01_1";

    /// <summary>The bomb as it lies planted, at its finest detail.</summary>
    public const string PlantedModel = "113_04_1";

    /// <summary>The planted bomb's lower details, finest first.</summary>
    public static readonly IReadOnlyList<string> PlantedDetails = ["113_04_2", "113_04_3", "113_04_4", "113_04_5"];

    /// <summary>The kneeling plant, a full-body clip.</summary>
    public const string PlantClip = "tba_place_c4";

    /// <summary>The plant as the planter sees it: the first-person arms setting the charge down.</summary>
    public const string FirstPersonPlantClip = "fire_c4_1st";

    /// <summary>The bomb held in first person, standing: the sequel's own C4 names this its first-person stand (<c>weapon.qvm</c>).</summary>
    public const string FirstPersonHoldClip = "walk_c4_1st";

    /// <summary>The bomb held in first person, moving: the sequel's first-person move for the C4.</summary>
    /// <remarks>Its draw, <c>weaponraise_c4_1st</c>, is a one-shot the sequel plays as the weapon comes up, never a stand.</remarks>
    public const string FirstPersonWalkClip = "run_c4_1st";

    /// <summary>The bomb held in a body's hands, as others see its carrier holding it.</summary>
    public const string HoldClip = "tense_c4bomb";

    /// <summary>The defuse, a full-body clip.</summary>
    public const string DefuseClip = "defusing_bomb";

    /// <summary>The plant, said as it is begun.</summary>
    public const string PlantSound = "tba_placec4";

    /// <summary>The planted bomb's tick.</summary>
    public const string TickSound = "tba_c4seconds";

    /// <summary>The planted bomb's faster tick, near the end.</summary>
    public const string LastTickSound = "tba_c4seconds2";

    /// <summary>The defuse, said as it is begun.</summary>
    public const string DefuseSound = "tba_c4defuse";

    /// <summary>The bomb going off, near: I.G.I. 2's bomb's own (<c>explo_proxy</c>).</summary>
    public const string ExplosionSound = "explo_proxy";

    /// <summary>The bomb going off, heard from far away.</summary>
    public const string DistantExplosionSound = "explo_distant";

    /// <summary>The icon, a sprite.</summary>
    public const string Icon = "c4";

    private const string Prefix = "OpenIGI.Game.Bomb.";

    private static readonly Lazy<Dictionary<string, IReadOnlyList<string>>> Slots = new(ReadSlots);

    /// <summary>Reads one of the kit's files.</summary>
    /// <param name="file">Its file name with its extension, such as <c>113_04_1.mef</c>.</param>
    /// <returns>Its bytes, or <see langword="null"/> when the kit has no such file.</returns>
    public static byte[]? Read(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        using Stream? stream = typeof(BombKit).Assembly.GetManifestResourceStream(Prefix + file);
        if (stream is null)
        {
            return null;
        }

        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Reads a model.</summary>
    /// <param name="model">Its name, such as <see cref="PlantedModel"/>.</param>
    /// <returns>Its <c>.mef</c> bytes, or <see langword="null"/>.</returns>
    public static byte[]? Model(string model) => Read(model + ".mef");

    /// <summary>Reads a clip.</summary>
    /// <param name="clip">Its name, such as <see cref="PlantClip"/>.</param>
    /// <returns>Its <c>.iff</c> bytes, or <see langword="null"/>.</returns>
    public static byte[]? Clip(string clip) => Read(clip + ".iff");

    /// <summary>Reads a sound.</summary>
    /// <param name="sound">Its name, such as <see cref="TickSound"/>.</param>
    /// <returns>Its <c>.wav</c> bytes, or <see langword="null"/>.</returns>
    public static byte[]? Sound(string sound) => Read(sound + ".wav");

    /// <summary>Reads a texture.</summary>
    /// <param name="texture">Its name, as <see cref="TextureOf"/> gives it.</param>
    /// <returns>Its <c>.tex</c> bytes, or <see langword="null"/>.</returns>
    public static byte[]? Texture(string texture) => Read(texture + ".tex");

    /// <summary>The texture a model's material slot draws with.</summary>
    /// <param name="model">The model.</param>
    /// <param name="slot">The slot, from the model's draw record.</param>
    /// <returns>The texture's name, or <see langword="null"/> for a slot drawn untextured.</returns>
    public static string? TextureOf(string model, int slot) =>
        Slots.Value.TryGetValue(model, out IReadOnlyList<string>? slots) && slot >= 0 && slot < slots.Count && slots[slot].Length > 0
            ? slots[slot]
            : null;

    /// <summary>Gets the models the kit has, each with its slots' textures.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Models => Slots.Value;

    private static Dictionary<string, IReadOnlyList<string>> ReadSlots()
    {
        Dictionary<string, IReadOnlyList<string>> slots = new(StringComparer.OrdinalIgnoreCase);
        if (Read("kit.json") is not { } json)
        {
            return slots;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("models", out JsonElement models))
        {
            foreach (JsonProperty model in models.EnumerateObject())
            {
                slots[model.Name] = [.. model.Value.EnumerateArray().Select(t => t.GetString() ?? string.Empty)];
            }
        }

        return slots;
    }
}
