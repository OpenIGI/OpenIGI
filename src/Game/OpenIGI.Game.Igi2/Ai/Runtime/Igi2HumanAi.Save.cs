using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <content>
/// Its save state, <c>HumanAI_ptSerializeParameterList</c> (humanai.c): its flags, whether it has yet to run, its visibility timer, its
/// nodes, its legal nodes, and its patrols, each by its command's, last end's and path's level ids. Its detection, events, actions,
/// search and movement are not kept; <c>HumanAI_DeSerializeHandler</c> starts them afresh and sends a patrolling soldier on again.
/// </content>
public sealed partial class Igi2HumanAi
{
    // atPatrolPathStackItems: the slots a save always writes.
    private const int SavedPatrols = 8;

    /// <summary>Writes its save state.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(Flags);
        writer.Bool(!HasStarted);
        writer.Int(IgnoreVisibilityFactorTimer);
        writer.Int(CurrentNode);
        writer.Int(CurrentStartNode);
        writer.Int(DestinationNode);
        writer.Int(LegalNodes.Node1);
        writer.Int(LegalNodes.Node2);
        writer.Int(LegalNodes.IsUsed ? 1 : 0);
        writer.Int(Patrols.Count);
        for (int i = 0; i < SavedPatrols; i++)
        {
            Igi2PatrolControl? control = i < Patrols.Count ? Patrols.Items[i] : null;
            writer.Int(control?.Current?.Id ?? -1);
            writer.Int(control?.LastEnd?.Id ?? -1);
            writer.Int(control?.Path?.Id ?? -1);
        }
    }

    /// <summary>
    /// Reads its save state back, <c>HumanAI_DeSerializeHandler</c>: its detection, events and actions are cleared and its weight moved
    /// to its saved node; its channels are let go, its view is the idle one, and its search and movement are dropped; its patrols are
    /// found again by id; and a soldier that was patrolling with somewhere to go patrols again, its way there searched from its node.
    /// </summary>
    /// <param name="reader">The reader.</param>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (IsDetection)
        {
            Detection.Clear();
        }

        Events.Clear();
        Actions.Clear(this);
        Flags = reader.Int();
        reader.Bool();
        IgnoreVisibilityFactorTimer = reader.Int();
        int current = reader.Int();
        CurrentStartNode = reader.Int();
        int destination = reader.Int();
        int legal1 = reader.Int(), legal2 = reader.Int();
        LegalNodes = (legal1, legal2, reader.Int() != 0);
        int patrols = reader.Int();
        (int Current, int LastEnd, int Path)[] saved = new (int, int, int)[SavedPatrols];
        for (int i = 0; i < SavedPatrols; i++)
        {
            saved[i] = (reader.Int(), reader.Int(), reader.Int());
        }

        SetCurrentNode(current);
        HasStarted = true;
        for (int i = 0; i < 11; i++)
        {
            SetChannel(i, 0f);
        }

        UseIdleView();
        PathSearch.Clear();
        DestinationNode = CurrentNode;
        ClearMovementQueue();

        Patrols.Clear();
        for (int i = 0; i < Math.Min(patrols, Igi2PatrolList.Capacity); i++)
        {
            Igi2PatrolControl control = new();
            if (World.PatrolPath(saved[i].Path) is { } path)
            {
                control.Restore(path, saved[i].Current, saved[i].LastEnd);
            }

            Patrols.Push(control);
        }

        if (patrols > 0 && CurrentNode != -1 && destination != -1)
        {
            Resume(destination);
        }
    }

    // A patrol taken up again at its command, as an action of the patrol's rank, and the way to where the soldier was going.
    private void Resume(int destination)
    {
        if (Patrols.Top is not { Path: { } path } top)
        {
            return;
        }

        Igi2AiContent.PatrolContext context = new()
        {
            PatrolPathId = path.Id,
            StartIndex = Math.Max(0, path.Commands.ToList().IndexOf(top.Current!)),
        };
        Igi2AiActionItem item = Actions.Take();
        if (Actions.TakeContext())
        {
            item.Context = context;
        }

        item.Flags = 0;
        item.Delay = 0;
        item.Priority = Events.RankOf(28);
        item.Init = Igi2AiContent.PatrolInit;
        item.Run = Igi2AiContent.PatrolRun;
        item.Delete = Igi2AiContent.PatrolDelete;
        item.SquadOrder = 0;
        AddAction(item);

        if (Graph is { } graph && PathSearch.CreatePath(graph, CurrentNode, destination, uint.MaxValue))
        {
            DestinationNode = destination;
            CurrentStartNode = CurrentNode;
            BuildMovementQueue(graph);
            if (_movementQueue.Count > 0)
            {
                graph.AddWeight(_movementQueue[0].NodeId, -20480f);
                _movementQueue.RemoveAt(0);
            }
        }
    }
}
