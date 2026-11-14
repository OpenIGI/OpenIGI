using OpenIGI.Core.Diagnostics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Viewport;
using OpenIGI.Game.Ai;
using OpenIGI.Host.Editor;
using OpenIGI.Scripting.Qvm;

namespace OpenIGI.Editor;

/// <summary>
/// What each guard of the open mission is told by its own script (<see cref="AiGuardOrders"/>): read from
/// <c>MISSION:AI/&lt;mind id&gt;.qvm</c> as the game finds it, changed in the editor as undoable steps, and written back as a
/// script in the games' own shape (<see cref="AiGuardScript.Build"/>) when the mission is saved.
/// </summary>
internal sealed class GuardOrders
{
    private readonly IEditorHost _host;
    private readonly MissionWorkspace _workspace;
    private readonly Dictionary<int, AiGuardOrders> _read = [];
    private readonly Dictionary<int, AiGuardOrders> _pending = [];
    private int? _readFor;

    /// <summary>Initializes a new instance of the <see cref="GuardOrders"/> class.</summary>
    /// <param name="host">The editor host, for the scripts as the game finds them.</param>
    /// <param name="workspace">The open mission, saved with the orders changed in it.</param>
    public GuardOrders(IEditorHost host, MissionWorkspace workspace)
    {
        _host = host;
        _workspace = workspace;
        workspace.Saving += Save;
    }

    /// <summary>Raised when a guard's orders change.</summary>
    public event Action? Changed;

    /// <summary>Gets whether a mind has orders changed since the mission was saved, which its script is written from.</summary>
    /// <param name="aiId">The <c>HumanAI</c> task's id.</param>
    /// <returns>Whether it has.</returns>
    public bool HasPending(int aiId) => _pending.ContainsKey(aiId);

    /// <summary>Gets a guard's orders as they now stand: changed in the editor, or read off its script.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns>The orders; <see cref="AiGuardOrders.None"/> for a soldier with no mind or no script.</returns>
    public AiGuardOrders Of(MissionTask soldier)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        if (PatrolPaths.Ai(soldier) is not { Id: >= 0 } ai)
        {
            return AiGuardOrders.None;
        }

        if (_pending.TryGetValue(ai.Id, out AiGuardOrders? pending))
        {
            return pending;
        }

        if (_readFor != _workspace.Mission?.Id)
        {
            _readFor = _workspace.Mission?.Id;
            _read.Clear();
            _pending.Clear();
        }

        if (!_read.TryGetValue(ai.Id, out AiGuardOrders? orders))
        {
            _read[ai.Id] = orders = Read(ai.Id);
        }

        return orders;
    }

    /// <summary>The step that gives a guard new orders, written to its script when the mission is saved.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <param name="orders">The orders.</param>
    /// <returns>The step, or null for a soldier with no mind.</returns>
    public IEditorCommand? Set(MissionTask soldier, AiGuardOrders orders)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        ArgumentNullException.ThrowIfNull(orders);
        if (PatrolPaths.Ai(soldier) is not { Id: >= 0 } ai)
        {
            return null;
        }

        int id = ai.Id;
        bool had = _pending.TryGetValue(id, out AiGuardOrders? before);
        return new DelegateCommand(
            $"Change {TaskPresentation.Name(soldier)}'s Orders",
            () =>
            {
                _pending[id] = orders;
                Changed?.Invoke();
            },
            () =>
            {
                if (had)
                {
                    _pending[id] = before!;
                }
                else
                {
                    _pending.Remove(id);
                }

                Changed?.Invoke();
            });
    }

    private AiGuardOrders Read(int aiId)
    {
        if (_workspace.Mission is not { } mission
            || !_host.TryReadFile("LOCAL:" + MissionWorkspace.GamePath(mission, MissionValidator.AiScriptPath(aiId)), out ReadOnlyMemory<byte> bytes))
        {
            return AiGuardOrders.None;
        }

        try
        {
            return AiGuardScript.Read(QvmProgram.Read(bytes));
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or QvmFormatException)
        {
            _host.Logger.Warning("Editor", $"The AI script of mind {aiId} could not be read: {error.Message}");
            return AiGuardOrders.None;
        }
    }

    // The scripts of every guard whose orders were changed, each in the games' own shape, into the project.
    private void Save()
    {
        if (_workspace.Mission is not { } mission)
        {
            return;
        }

        bool squads = Equals(_host.Title, global::OpenIGI.Core.Titles.GameTitle.Igi2);
        foreach ((int aiId, AiGuardOrders orders) in _pending)
        {
            _workspace.WriteToProject(MissionWorkspace.GamePath(mission, MissionValidator.AiScriptPath(aiId)), AiGuardScript.Build(orders, squads));
            _read[aiId] = orders;
        }

        _pending.Clear();
    }
}
