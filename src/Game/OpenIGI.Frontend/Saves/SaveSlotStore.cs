using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Frontend.Saves;

/// <summary>
/// Reads and writes the port's profile-scoped save slots — a small JSON manifest beside a PNG screenshot
/// per slot — in a directory of the port's own, never the game installation.
/// </summary>
/// <remarks>
/// <para>
/// This is a new, self-contained format rather than an extension of the retail config: a JSON array of
/// <see cref="SaveSlot"/> in <c>saves.json</c>, and a profile-qualified <c>slot-N-*.png</c> next to it
/// holding the frame the save was taken on. Keeping it separate means it can grow (more per-slot detail, a thumbnail size
/// change) without any risk to the byte-compatible <c>config.qvm</c> the profile system guards.
/// </para>
/// <para>
/// Load is defensive: a missing or corrupt manifest is an empty slot list, never a throw out of a
/// constructor, so a hand-edited or partially-written file cannot stop the shell opening.
/// </para>
/// </remarks>
public sealed partial class SaveSlotStore
{
    private const string ManifestName = "saves.json";
    private readonly string _directory;
    private readonly List<SaveSlot> _slots = [];

    /// <summary>Initializes a new instance of the <see cref="SaveSlotStore"/> class and loads any manifest.</summary>
    /// <param name="directory">The directory slots and screenshots live in; created on first save.</param>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <see langword="null"/>.</exception>
    public SaveSlotStore(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
        Reload();
    }

    /// <summary>Gets the slots currently known, ordered by slot index.</summary>
    public IReadOnlyList<SaveSlot> Slots => _slots;

    /// <summary>Gets the directory slots are stored in.</summary>
    public string Directory => _directory;

    /// <summary>Re-reads the manifest from disk, replacing the in-memory list.</summary>
    public void Reload()
    {
        _slots.Clear();
        string path = Path.Combine(_directory, ManifestName);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            SaveSlot[]? loaded = JsonSerializer.Deserialize(
                File.ReadAllText(path), SaveSlotJsonContext.Default.SaveSlotArray);
            if (loaded is not null)
            {
                _slots.AddRange(loaded);
                _slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A corrupt manifest is treated as no saves rather than crashing the shell.
            _slots.Clear();
        }
    }

    /// <summary>Finds the first slot by index.</summary>
    /// <remarks>Kept for callers that predate profile-scoped slots; new UI code should include the profile.</remarks>
    /// <param name="slot">The slot index.</param>
    /// <returns>The slot, or <see langword="null"/> when it is empty.</returns>
    public SaveSlot? Find(int slot) => _slots.Find(s => s.Slot == slot);

    /// <summary>Finds a slot belonging to a particular profile.</summary>
    public SaveSlot? Find(string profileName, int slot)
    {
        ArgumentNullException.ThrowIfNull(profileName);
        return _slots.Find(s => s.Slot == slot
            && string.Equals(s.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Writes a slot, replacing any existing one at the same index, with its screenshot.</summary>
    /// <param name="slot">The slot index.</param>
    /// <param name="profileName">The profile the save belongs to.</param>
    /// <param name="missionId">The mission the save is at.</param>
    /// <param name="missionName">The mission's display name.</param>
    /// <param name="savedAt">When the save was written.</param>
    /// <param name="screenshot">The captured frame, or empty to save without one.</param>
    /// <param name="width">The screenshot width in pixels.</param>
    /// <param name="height">The screenshot height in pixels.</param>
    /// <param name="mods">The mods that change play the save was made with; none by default.</param>
    /// <returns>The written slot.</returns>
    /// <exception cref="ArgumentException">The screenshot is non-empty but shorter than <paramref name="width"/>×<paramref name="height"/>.</exception>
    public SaveSlot Save(
        int slot,
        string profileName,
        int missionId,
        string missionName,
        DateTimeOffset savedAt,
        ReadOnlySpan<Bgra32> screenshot,
        int width,
        int height,
        IReadOnlyList<OpenIGI.Core.Content.ModReference>? mods = null)
    {
        System.IO.Directory.CreateDirectory(_directory);

        SaveSlot? existing = Find(profileName, slot);
        if (existing is not null)
        {
            DeleteScreenshot(existing);
        }

        string? screenshotFile = null;
        if (!screenshot.IsEmpty)
        {
            if (screenshot.Length < width * height)
            {
                throw new ArgumentException(
                    $"Screenshot is {screenshot.Length} pixels, need {width}×{height}.", nameof(screenshot));
            }

            screenshotFile = ScreenshotFileName(slot, profileName);
            PngWriter.Write(Path.Combine(_directory, screenshotFile), screenshot[..(width * height)], width, height);
        }

        var record = new SaveSlot(slot, profileName, missionId, missionName, savedAt, screenshotFile)
        {
            Mods = mods is { Count: > 0 } ? [.. mods] : null,
        };
        _slots.RemoveAll(s => s.Slot == slot
            && string.Equals(s.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
        _slots.Add(record);
        _slots.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        Flush();
        return record;
    }

    /// <summary>Deletes a slot and its screenshot.</summary>
    /// <param name="slot">The slot index.</param>
    /// <returns><see langword="true"/> when a slot was removed.</returns>
    public bool Delete(int slot)
    {
        SaveSlot? existing = Find(slot);
        if (existing is null)
        {
            return false;
        }

        return Delete(existing.ProfileName, slot);
    }

    /// <summary>Deletes a specific profile's slot and screenshot.</summary>
    public bool Delete(string profileName, int slot)
    {
        ArgumentNullException.ThrowIfNull(profileName);
        SaveSlot? existing = Find(profileName, slot);
        if (existing is null)
        {
            return false;
        }

        _slots.RemoveAll(s => s.Slot == slot
            && string.Equals(s.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
        DeleteScreenshot(existing);

        Flush();
        return true;
    }

    /// <summary>Deletes every slot belonging to a player profile, including their screenshots.</summary>
    /// <param name="profileName">The profile name to remove saves for.</param>
    /// <returns>The number of removed slots.</returns>
    public int DeleteByProfile(string profileName)
    {
        ArgumentNullException.ThrowIfNull(profileName);

        SaveSlot[] matches = _slots
            .Where(slot => string.Equals(slot.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (SaveSlot slot in matches)
        {
            Delete(slot.ProfileName, slot.Slot);
        }

        return matches.Length;
    }

    /// <summary>Gets the absolute path of a slot's screenshot, or null when it has none.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The screenshot path, or <see langword="null"/>.</returns>
    public string? ScreenshotPath(SaveSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return slot.ScreenshotFile is { } file ? Path.Combine(_directory, file) : null;
    }

    private void Flush()
    {
        System.IO.Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, ManifestName);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(
                _slots.ToArray(), SaveSlotJsonContext.Default.SaveSlotArray));
        }
        catch (IOException)
        {
            // A read-only location must not crash a save; the slot stays in memory for this session.
        }
    }

    private static string ScreenshotFileName(int slot, string profileName) =>
        $"slot-{slot}-{Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(profileName))}.png";

    private void DeleteScreenshot(SaveSlot slot)
    {
        if (slot.ScreenshotFile is not { } file)
        {
            return;
        }

        string screenshotPath = Path.Combine(_directory, file);
        if (!File.Exists(screenshotPath))
        {
            return;
        }

        try
        {
            File.Delete(screenshotPath);
        }
        catch (IOException)
        {
            // A screenshot we cannot delete does not stop the slot being forgotten.
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(SaveSlot[]))]
    private sealed partial class SaveSlotJsonContext : JsonSerializerContext;
}
