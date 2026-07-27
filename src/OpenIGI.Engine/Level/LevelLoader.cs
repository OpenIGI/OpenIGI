using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Engine.Level;

/// <summary>
/// Builds and tears down a level's subtree: empties the level root, runs its <c>objects.qvm</c>, and
/// restarts the level clock.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>Level_Load</c> (<c>0x4F0D40</c>) and <c>Level_Unload</c> (<c>0x4F0E90</c>). It is the
/// script-driven half of loading a mission; the rest — terrain, packs, sounds, sprites, AI graphs — is
/// M6's and hangs off the same sequence.
/// </para>
/// <para>
/// The step order is exact and each part of it matters. Ids are released before the script runs, because
/// level data reuses them. The <c>Task_New</c> symbol is rebound to the level root, because that is what
/// makes the script's top-level tasks attach there. <see cref="TaskMessage.PostLoad"/> is a full
/// recursive broadcast, unlike the per-tick update. And the clock reset comes last, so a task's
/// <c>PostLoad</c> handler still sees the previous level's timings.
/// </para>
/// </remarks>
public sealed class LevelLoader
{
    private const string LogCategory = "Level";

    /// <summary>The file each mission's task tree is built from, relative to the mission directory.</summary>
    public const string ObjectsScriptName = "objects.qvm";

    private readonly TaskTree _tree;
    private readonly TaskScriptCommands _commands;
    private readonly NativeRegistry _natives;
    private readonly ResourceRegistry _resources;
    private readonly IIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="LevelLoader"/> class.</summary>
    /// <param name="levelRoot">The task the level's subtree hangs off.</param>
    /// <param name="commands">The <c>Task_New</c> commands the level script drives.</param>
    /// <param name="natives">The script namespace the level script resolves against.</param>
    /// <param name="resources">Where level scripts are read from.</param>
    /// <param name="logger">The logger for load diagnostics; defaults to a sink that discards them.</param>
    /// <exception cref="ArgumentNullException">Any required argument is <see langword="null"/>.</exception>
    public LevelLoader(
        LevelTask levelRoot,
        TaskScriptCommands commands,
        NativeRegistry natives,
        ResourceRegistry resources,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(resources);

        LevelRoot = levelRoot;
        _tree = levelRoot.Tree;
        _commands = commands;
        _natives = natives;
        _resources = resources;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Gets the task the level's subtree hangs off.</summary>
    public LevelTask LevelRoot { get; }

    /// <summary>Gets the path of the level script most recently loaded.</summary>
    public string? LoadedScriptPath { get; private set; }

    /// <summary>Destroys the level's subtree and releases every script id.</summary>
    /// <remarks>
    /// The level root itself survives — it is a fixture of the tree, not part of the level. Ids are
    /// released rather than the tasks holding them being deleted; the two are separate operations in the
    /// original and conflating them was an error in the earlier gameplay note.
    /// </remarks>
    public void Unload()
    {
        _tree.DeleteChildren(LevelRoot);
        _tree.ClearIds();
        LoadedScriptPath = null;
        _logger.Information(LogCategory, "Unloaded the level subtree.");
    }

    /// <summary>Loads a mission's task tree from its <c>objects.qvm</c>.</summary>
    /// <param name="missionPath">The mission directory, without a trailing separator.</param>
    /// <param name="tickRate">The simulation rate the level runs at.</param>
    /// <exception cref="ArgumentException"><paramref name="missionPath"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    /// <exception cref="EngineFatalException">The script is missing or faults.</exception>
    public void LoadMission(string missionPath, int tickRate = GameClock.DefaultTickRate)
    {
        ArgumentException.ThrowIfNullOrEmpty(missionPath);
        LoadScript($"{missionPath.TrimEnd('/', '\\')}/{ObjectsScriptName}", tickRate);
    }

    /// <summary>Loads a level script and restarts the level clock.</summary>
    /// <param name="scriptPath">The resource path of the compiled level script.</param>
    /// <param name="tickRate">The simulation rate the level runs at.</param>
    /// <exception cref="ArgumentException"><paramref name="scriptPath"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is not positive.</exception>
    /// <exception cref="EngineFatalException">The script is missing or faults.</exception>
    /// <remarks>
    /// A script that cannot be loaded is fatal, with the original's wording. A script that faults
    /// mid-run is fatal too — the original has no such check because its interpreter cannot report one,
    /// but continuing with a half-built level is not a behaviour worth reproducing.
    /// </remarks>
    public void LoadScript(string scriptPath, int tickRate = GameClock.DefaultTickRate)
    {
        ArgumentException.ThrowIfNullOrEmpty(scriptPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tickRate);

        _tree.ClearIds();
        _commands.SetSymbolContext(LevelRoot);

        if (!_resources.TryLoad(scriptPath, out ReadOnlyMemory<byte> bytes))
        {
            throw new EngineFatalException($"Level_Load(): Couldn't load script: {scriptPath}");
        }

        QvmProgram program = QvmProgram.Read(bytes);

        // After the load and before the run, exactly as the original orders it: a script that failed to
        // compile leaves the previous load's state alone.
        _commands.BeginLoad();

        var interpreter = new QvmInterpreter(program, _natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException(
                $"Level_Load(): {scriptPath} faulted with {error}: {interpreter.ErrorMessage}");
        }

        _tree.Send(LevelRoot, TaskMessage.PostLoad);
        LevelRoot.ResetClock(tickRate);
        LoadedScriptPath = scriptPath;

        _logger.Information(
            LogCategory,
            $"Loaded '{scriptPath}' at {tickRate} Hz ({LevelRoot.MillisecondsPerTick} ms per tick).");
    }
}
