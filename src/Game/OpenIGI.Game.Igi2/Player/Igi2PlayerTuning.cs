using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's tuning, read from <c>humanplayer.qvm</c> once at start-up.</summary>
/// <remarks>
/// <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) runs <c>LOCAL:humanplayer/humanplayer.qsc</c> into a
/// <c>HumanPlayerConfig</c> task straight after the AI's settings, before any level: the script declares one
/// <c>HumanPlayerConfigItem</c>, whose figures every player then takes (<c>HumanPlayer_UpdateParameters</c>,
/// <c>0x4BD1F0</c>). The task stays in the tree for the session.
/// </remarks>
public sealed class Igi2PlayerTuning
{
    /// <summary>The script the tuning is read from.</summary>
    public const string ScriptResource = "LOCAL:humanplayer/humanplayer.qvm";

    private const string LogCategory = "Player";

    private readonly IOpenIgiLogger _logger;

    /// <summary>Initializes a new instance of the <see cref="Igi2PlayerTuning"/> class.</summary>
    /// <param name="logger">Where the load reports.</param>
    public Igi2PlayerTuning(IOpenIgiLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Gets the tuning, or <see cref="Igi2HumanPlayerGeneral.Unset"/> before the script has run.</summary>
    public Igi2HumanPlayerGeneral General { get; private set; } = Igi2HumanPlayerGeneral.Unset;

    /// <summary>Runs the script and takes the config item it declares.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace, with the task commands bound.</param>
    /// <param name="tree">The task tree the item is declared into.</param>
    /// <returns><see langword="true"/> when the script declared an item.</returns>
    /// <exception cref="EngineFatalException">The script is missing or faults.</exception>
    public bool Load(ResourceRegistry resources, NativeRegistry natives, TaskTree tree)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(tree);

        if (!resources.TryLoad(ScriptResource, out ReadOnlyMemory<byte> bytes))
        {
            throw new EngineFatalException($"Couldn't load script: {ScriptResource}");
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {ScriptResource}", ex);
        }

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException($"{ScriptResource} faulted with {error}: {interpreter.ErrorMessage}");
        }

        if (Find(tree.Root) is not { } item)
        {
            _logger.Warning(LogCategory, $"{ScriptResource} declared no HumanPlayerConfigItem; the player is untuned.");
            return false;
        }

        General = Igi2HumanPlayerGeneral.From(item);
        _logger.Information(LogCategory, $"Player tuning read from {ScriptResource}.");
        return true;
    }

    private static HumanPlayerConfigItemTask? Find(GameTask task)
    {
        if (task is HumanPlayerConfigItemTask item)
        {
            return item;
        }

        foreach (GameTask child in task.Children)
        {
            if (Find(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
