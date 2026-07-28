using System.Diagnostics.CodeAnalysis;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>
/// The table of mission descriptors, built by running every <c>mission.qvm</c> under
/// <c>LOCAL:missions</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>Mission_InitRegistry</c> (<c>0x484F30</c>): register the <c>DefineMission</c> native and
/// the <c>MISSION_NEXT_MISSION_UNDEFINED</c> constant, empty the table, then walk <c>LOCAL:missions</c>
/// recursively (<c>0x4B1420</c>) running each <c>mission.qvm</c> it finds (<c>Mission_Open</c>,
/// <c>0x484E60</c>).
/// </para>
/// <para>
/// <b>The scan is a startup step, not part of loading a mission.</b> <c>Mission_Load</c> looks a
/// descriptor up by id as its very first action, long before any level script runs, so the table has to
/// be complete beforehand — and a retail <c>mission.qvm</c> holds nothing but its one
/// <c>DefineMission</c> call. An earlier note placed <c>Mission_Open</c> inside <c>Mission_Load</c>;
/// it is not called from there at all, and running a <c>mission.qvm</c> twice would trip the original's
/// own "not unique" abort.
/// </para>
/// </remarks>
public sealed class MissionRegistry
{
    private const string LogCategory = "Mission";

    /// <summary>The native each <c>mission.qvm</c> calls to publish its descriptor.</summary>
    public const string DefineMissionNative = "DefineMission";

    /// <summary>The directory the startup scan walks.</summary>
    public const string MissionRoot = "LOCAL:missions";

    /// <summary>The script each mission directory is expected to hold.</summary>
    public const string MissionScriptName = "mission.qvm";

    private readonly List<MissionDescriptor> _missions = [];
    private readonly Dictionary<int, MissionDescriptor> _byId = [];
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="MissionRegistry"/> class.</summary>
    /// <param name="logger">The logger for scan diagnostics; defaults to a sink that discards them.</param>
    public MissionRegistry(IIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the descriptors, in the order they were defined.</summary>
    public IReadOnlyList<MissionDescriptor> Missions => _missions;

    /// <summary>Gets the number of registered missions.</summary>
    public int Count => _missions.Count;

    /// <summary>Registers the <c>DefineMission</c> native and its companion constant.</summary>
    /// <param name="natives">The script namespace mission scripts resolve against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="natives"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The original registers the native with an argument count of <c>-1</c>, meaning variadic; every
    /// retail caller passes eleven.
    /// </remarks>
    public void Register(NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(natives);

        // The original declares this native with an argument count of -1, meaning variadic. The engine
        // spells the same thing as no declared count, which is what Task_DeclareParameters already uses.
        natives.RegisterFunction(DefineMissionNative, call => Define(call));
        natives.RegisterConstant(MissionDescriptor.NextMissionUndefinedConstant, MissionDescriptor.NextMissionUndefined);
    }

    /// <summary>Looks a mission up by id.</summary>
    /// <param name="id">The mission id.</param>
    /// <param name="descriptor">Receives the descriptor when one is registered.</param>
    /// <returns><see langword="true"/> when the id exists.</returns>
    public bool TryGet(int id, [NotNullWhen(true)] out MissionDescriptor? descriptor) => _byId.TryGetValue(id, out descriptor);

    /// <summary>Looks a mission up by id, failing the way the original does when it is absent.</summary>
    /// <param name="id">The mission id.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="EngineFatalException">No mission carries that id.</exception>
    public MissionDescriptor Require(int id) =>
        TryGet(id, out MissionDescriptor? descriptor)
            ? descriptor
            : throw new EngineFatalException($"The mission ID {id} does not exist.");

    /// <summary>Empties the table.</summary>
    public void Clear()
    {
        _missions.Clear();
        _byId.Clear();
    }

    /// <summary>
    /// Walks <see cref="MissionRoot"/> and runs every <c>mission.qvm</c> beneath it, so that every
    /// mission publishes its descriptor.
    /// </summary>
    /// <param name="fileSystem">The file system the walk resolves against.</param>
    /// <param name="resources">Where the scripts are read from.</param>
    /// <param name="natives">The script namespace, with <see cref="Register"/> already applied.</param>
    /// <returns>The number of missions registered.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="EngineFatalException">A mission script is present but faults.</exception>
    /// <remarks>
    /// Missing scripts are not an error — the walk simply finds none. A script that faults is, because a
    /// half-registered table would surface much later as a missing mission id.
    /// </remarks>
    public int Scan(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);

        Clear();

        List<string> scripts = [];
        Walk(fileSystem, MissionRoot, scripts, depth: 0);

        foreach (string script in scripts)
        {
            string name = ToResourceName(script);
            if (!resources.TryLoad(name, out ReadOnlyMemory<byte> bytes))
            {
                _logger.Warning(LogCategory, $"Mission script '{name}' vanished between the scan and the load.");
                continue;
            }

            QvmProgram program;
            try
            {
                program = QvmProgram.Read(bytes);
            }
            catch (QvmFormatException ex)
            {
                throw new EngineFatalException($"Mission_Open(): Couldn't load script: {name}", ex);
            }

            QvmInterpreter interpreter = new(program, natives);
            QvmError error = interpreter.Run();
            if (error != QvmError.None)
            {
                throw new EngineFatalException($"Mission_Open(): {name} faulted with {error}: {interpreter.ErrorMessage}");
            }
        }

        _logger.Information(LogCategory, $"Registered {Count} mission(s) from {scripts.Count} script(s).");
        return Count;
    }

    /// <summary>
    /// Walks a directory tree collecting every <c>mission.qvm</c>, the way the original's
    /// <c>File_WalkTree</c> (<c>0x4B1420</c>) drives <c>Mission_Open</c>, which recurses whenever an
    /// entry turns out to be a directory.
    /// </summary>
    private static void Walk(VirtualFileSystem fileSystem, string directory, List<string> scripts, int depth)
    {
        // Retail nests two levels — missions/<location>/<level> — and a symlinked or cyclic tree would
        // otherwise walk forever, which is not a failure mode worth inheriting.
        const int maxDepth = 8;
        if (depth > maxDepth)
        {
            return;
        }

        scripts.AddRange(fileSystem.EnumerateFiles(directory, MissionScriptName));

        foreach (string child in fileSystem.EnumerateDirectories(directory))
        {
            Walk(fileSystem, child, scripts, depth + 1);
        }
    }

    private static string ToResourceName(string path)
    {
        string normalized = path.Replace('\\', '/');
        int index = normalized.IndexOf("missions/", StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? $"LOCAL:{normalized[index..]}" : normalized;
    }

    private QvmValue Define(QvmCallContext call)
    {
        MissionDescriptor descriptor = new(
            call.GetInt32(0),
            call.GetString(1),
            call.GetString(2),
            call.GetString(3),
            call.GetInt32(4),
            Normalize(call.GetString(5)),
            Normalize(call.GetString(6)),
            Normalize(call.GetString(7)),
            call.GetString(8),
            call.GetString(9),
            call.GetString(10));

        if (!_byId.TryAdd(descriptor.Id, descriptor))
        {
            throw new EngineFatalException($"Mission ID {descriptor.Id} not unique");
        }

        _missions.Add(descriptor);
        return QvmValue.FromInt32(1);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}
