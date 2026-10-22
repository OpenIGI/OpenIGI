using OpenIGI.Core;
using OpenIGI.Engine.Vfs;

namespace OpenIGI.Game.Igi2.Scripting;

/// <summary>
/// I.G.I. 2's <c>MISSION:</c> device over another game's mission: the mission's own files, and I.G.I. 2's generic scripts for
/// the soldiers and squads it has none for.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. 2's minds read their scripts and graphs through <c>MISSION:</c>. The graphs are the mission's own: Project I.G.I. keeps
/// them at <c>graphs/graph&lt;id&gt;.dat</c> in the same named-field format, so I.G.I. 2's AI walks the graphs its levels were
/// authored with. A soldier's or a squad's script, <c>AI/&lt;id&gt;.qvm</c> or <c>AI/Squad_&lt;id&gt;.qvm</c>, is the generic one
/// (<see cref="Igi2GenericScripts"/>): the host's own scripts beside it are compiled against the host's natives.
/// </para>
/// </remarks>
public sealed class Igi2HostMissionDevice : IFileDevice
{
    /// <summary>The device's name.</summary>
    public const string DeviceName = "MISSION";

    private readonly LocalFileDevice _mission;
    private readonly Igi2GenericScripts _scripts;

    /// <summary>Initializes a new instance of the <see cref="Igi2HostMissionDevice"/> class.</summary>
    /// <param name="missionDirectory">The host mission's directory.</param>
    /// <param name="scripts">I.G.I. 2's generic scripts.</param>
    public Igi2HostMissionDevice(string missionDirectory, Igi2GenericScripts scripts)
    {
        ArgumentException.ThrowIfNullOrEmpty(missionDirectory);
        _mission = new LocalFileDevice(missionDirectory, DeviceName);
        _scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
    }

    /// <inheritdoc/>
    public string Name => DeviceName;

    /// <inheritdoc/>
    public bool Exists(string path) => Script(path) is not null || _mission.Exists(path);

    /// <inheritdoc/>
    public bool TryOpenRead(string path, out Stream? stream)
    {
        if (Script(path) is { } script)
        {
            stream = new MemoryStream(script, writable: false);
            return true;
        }

        return _mission.TryOpenRead(path, out stream);
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern) => _mission.EnumerateFiles(path, searchPattern);

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateDirectories(string path) => _mission.EnumerateDirectories(path);

    // The generic script a path names: a soldier's AI/<id>.qvm or a squad's AI/Squad_<id>.qvm.
    private byte[]? Script(string path)
    {
        string normal = ResourcePath.Normalize(path).TrimStart(ResourcePath.DirectorySeparator);
        if (!normal.StartsWith("ai/", StringComparison.OrdinalIgnoreCase)
            || !normal.EndsWith(".qvm", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ReadOnlySpan<char> stem = normal.AsSpan(3, normal.Length - 7);
        if (stem.StartsWith("squad_", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(stem[6..], out _) ? _scripts.Squad : null;
        }

        return int.TryParse(stem, out _) ? _scripts.Soldier : null;
    }
}
