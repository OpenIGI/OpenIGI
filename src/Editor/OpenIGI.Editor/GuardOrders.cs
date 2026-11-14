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

    // Scripts given to minds placed in the editor (a prefab's guards), by mind id: read as theirs until the mission is saved,
    // then written for each mind still in it.
    private readonly Dictionary<int, byte[]> _adopted = [];
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
            _adopted.Clear();
        }

        if (!_read.TryGetValue(ai.Id, out AiGuardOrders? orders))
        {
            _read[ai.Id] = orders = Read(ai.Id);
        }

        return orders;
    }

    /// <summary>
    /// Makes sure a guard's script gives these orders when the mission is next saved, as part of the save rather than a step
    /// of its own: what a guard's patrol, laid with the patrol tool, needs its script to say.
    /// </summary>
    /// <param name="soldier">The soldier.</param>
    /// <param name="orders">The orders its script must give.</param>
    /// <returns>Why it cannot, or null.</returns>
    public string? Require(MissionTask soldier, AiGuardOrders orders)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        ArgumentNullException.ThrowIfNull(orders);
        if (PatrolPaths.Ai(soldier) is not { Id: >= 0 } ai || _pending.ContainsKey(ai.Id))
        {
            return null;
        }

        ReadOnlyMemory<byte>? existing = Script(ai.Id);
        if (existing is not null && Of(soldier) == orders)
        {
            return null;
        }

        if (existing is { } bytes && Write(bytes, orders) is null)
        {
            return $"{TaskPresentation.Name(soldier)}'s own AI script is laid out in a way its patrol cannot be put in without losing what else it does; start the patrol from its script in the Script panel.";
        }

        _pending[ai.Id] = orders;
        return null;
    }

    /// <summary>The step that gives a guard new orders, written to its script when the mission is saved.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <param name="orders">The orders.</param>
    /// <param name="refusal">Receives why the orders cannot be given, when they cannot.</param>
    /// <returns>The step, or null for a soldier with no mind or a script that cannot be changed so.</returns>
    /// <remarks>
    /// A guard with a script of its own keeps it, changed only where the orders are said (<see cref="AiGuardScriptEdit"/>);
    /// a script laid out so that the change cannot be made and read back the same is not changed at all.
    /// </remarks>
    public IEditorCommand? Set(MissionTask soldier, AiGuardOrders orders, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        ArgumentNullException.ThrowIfNull(orders);
        refusal = null;
        if (PatrolPaths.Ai(soldier) is not { Id: >= 0 } ai)
        {
            refusal = "It has no mind with an id, so it has no script to give orders in.";
            return null;
        }

        if (Script(ai.Id) is { } existing && Write(existing, orders) is null)
        {
            refusal = $"Its script ({MissionValidator.AiScriptPath(ai.Id)}) is laid out in a way this cannot be changed without losing what else it does; change it in the Script panel.";
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

    /// <summary>Gives a mind placed in the editor its script, written with the mission when it is saved and the mind is still in it.</summary>
    /// <param name="aiId">The mind's id.</param>
    /// <param name="script">The script's bytes.</param>
    public void Adopt(int aiId, byte[] script)
    {
        _adopted[aiId] = script;
        _read.Remove(aiId);
    }

    /// <summary>A mind's script as it will be saved: with the orders changed since if any, else as the game finds it.</summary>
    /// <param name="aiId">The mind's id.</param>
    /// <returns>The script's bytes; null when it has none, or its changed orders cannot be written into it.</returns>
    public byte[]? Current(int aiId) =>
        _pending.TryGetValue(aiId, out AiGuardOrders? orders)
            ? Script(aiId) is { } existing ? Write(existing, orders) : AiGuardScript.NewGuard(orders, Squads)
            : Script(aiId)?.ToArray();

    // A mind's script as the game finds it, or null when it has none.
    private ReadOnlyMemory<byte>? Script(int aiId) =>
        _adopted.TryGetValue(aiId, out byte[]? adopted) ? adopted
        : _workspace.Mission is { } mission && _host.TryReadFile("LOCAL:" + MissionWorkspace.GamePath(mission, MissionValidator.AiScriptPath(aiId)), out ReadOnlyMemory<byte> bytes)
            ? bytes
            : null;

    // A script changed to orders, or null when it cannot be.
    private byte[]? Write(ReadOnlyMemory<byte> existing, AiGuardOrders orders)
    {
        try
        {
            return AiGuardScriptEdit.Apply(existing, orders, Squads);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or QvmFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private bool Squads => Equals(_host.Title, global::OpenIGI.Core.Titles.GameTitle.Igi2);

    private AiGuardOrders Read(int aiId)
    {
        if (Script(aiId) is not { } bytes)
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

        foreach ((int aiId, AiGuardOrders orders) in _pending)
        {
            // A script of its own changed where the orders are said; a guard with none given one in the games' shape.
            byte[]? bytes = Script(aiId) is { } existing ? Write(existing, orders) : AiGuardScript.NewGuard(orders, Squads);
            if (bytes is null)
            {
                _host.Logger.Warning("Editor", $"The script of mind {aiId} could not be changed to its new orders; it is left as it was.");
                continue;
            }

            _workspace.WriteToProject(MissionWorkspace.GamePath(mission, MissionValidator.AiScriptPath(aiId)), bytes);
            _read[aiId] = orders;
        }

        // Placed minds whose orders were not changed since, as they came, where the mind is still in the mission.
        HashSet<int> minds = [.. _workspace.Document?.EnumerateTasks().Where(static t => t.TypeName == Core.Prefabs.Prefab.MindType).Select(static t => t.Id) ?? []];
        foreach ((int aiId, byte[] script) in _adopted)
        {
            if (!_pending.ContainsKey(aiId) && minds.Contains(aiId))
            {
                _workspace.WriteToProject(MissionWorkspace.GamePath(mission, MissionValidator.AiScriptPath(aiId)), script);
            }
        }

        _pending.Clear();
        _adopted.Clear();
    }
}
