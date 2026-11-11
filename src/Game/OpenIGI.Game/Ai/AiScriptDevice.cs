using OpenIGI.Core;
using OpenIGI.Engine.Vfs;

namespace OpenIGI.Game.Ai;

/// <summary>
/// A <c>MISSION:</c> device for one game's soldiers in another game's level: the scripts written for them
/// (<see cref="SoldierScripts"/>, <c>AI/&lt;id&gt;.qvm</c>) over the level's own files, where it has a directory.
/// </summary>
/// <remarks>
/// A soldier's mind reads its own script through <c>MISSION:</c>. The hosting level's own scripts are compiled against the
/// hosting game's natives, so the guest's soldiers read the ones written for them (<see cref="AiGuardScript.Build"/>), and a
/// game reads what it has for a soldier none was written for (<see cref="Fallback"/>).
/// </remarks>
public class AiScriptDevice : IFileDevice
{
    /// <summary>The device's name.</summary>
    public const string DeviceName = "MISSION";

    private readonly LocalFileDevice? _mission;

    /// <summary>Initializes a new instance of the <see cref="AiScriptDevice"/> class.</summary>
    /// <param name="missionDirectory">The hosting mission's directory, whose files are read through; <see langword="null"/> for none.</param>
    public AiScriptDevice(string? missionDirectory = null) =>
        _mission = string.IsNullOrEmpty(missionDirectory) ? null : new LocalFileDevice(missionDirectory, DeviceName);

    /// <inheritdoc/>
    public string Name => DeviceName;

    /// <summary>Gets the scripts written for the soldiers, by their minds' ids, which a soldier reads in place of any other.</summary>
    public IDictionary<int, byte[]> SoldierScripts { get; } = new Dictionary<int, byte[]>();

    /// <inheritdoc/>
    public bool Exists(string path) => Script(path) is not null || (_mission?.Exists(path) ?? false);

    /// <inheritdoc/>
    public bool TryOpenRead(string path, out Stream? stream)
    {
        if (Script(path) is { } script)
        {
            stream = new MemoryStream(script, writable: false);
            return true;
        }

        stream = null;
        return _mission?.TryOpenRead(path, out stream) ?? false;
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern) => _mission?.EnumerateFiles(path, searchPattern) ?? [];

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateDirectories(string path) => _mission?.EnumerateDirectories(path) ?? [];

    /// <summary>The script an <c>AI/&lt;stem&gt;.qvm</c> no soldier's own script answers reads, if any.</summary>
    /// <param name="stem">The file's name, without its directory or extension.</param>
    /// <returns>The script, or <see langword="null"/> for the level's own file, where it has one.</returns>
    protected virtual byte[]? Fallback(ReadOnlySpan<char> stem) => null;

    // The script a path names: a soldier's AI/<id>.qvm, its own where one was written, else what the game falls back to.
    private byte[]? Script(string path)
    {
        string normal = ResourcePath.Normalize(path).TrimStart(ResourcePath.DirectorySeparator);
        if (!normal.StartsWith("ai/", StringComparison.OrdinalIgnoreCase)
            || !normal.EndsWith(".qvm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ReadOnlySpan<char> stem = normal.AsSpan(3, normal.Length - 7);
        return int.TryParse(stem, out int id) && SoldierScripts.TryGetValue(id, out byte[]? own) ? own : Fallback(stem);
    }
}
