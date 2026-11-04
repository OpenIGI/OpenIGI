namespace OpenIGI.Editor.Core.Commands;

/// <summary>
/// The editor's history: executes commands, undoes and redoes them, groups them into transactions, merges
/// the steps of one gesture, and knows whether the document differs from what was last saved.
/// </summary>
public sealed class EditHistory
{
    private readonly List<IEditorCommand> _done = [];
    private readonly List<IEditorCommand> _undone = [];
    private readonly Stack<(string Name, List<IEditorCommand> Commands)> _transactions = new();
    private IEditorCommand? _savePoint;
    private bool _savePointLost;
    private bool _mergeOpen;

    /// <summary>Raised after anything that changes the history or the document through it.</summary>
    public event Action? Changed;

    /// <summary>Gets the applied commands, oldest first: what the history panel lists.</summary>
    public IReadOnlyList<IEditorCommand> Done => _done;

    /// <summary>Gets the undone commands that a redo would reapply, the next one last.</summary>
    public IReadOnlyList<IEditorCommand> Undone => _undone;

    /// <summary>Gets whether there is anything to undo.</summary>
    public bool CanUndo => _done.Count > 0 && _transactions.Count == 0;

    /// <summary>Gets whether there is anything to redo.</summary>
    public bool CanRedo => _undone.Count > 0 && _transactions.Count == 0;

    /// <summary>Gets the name of the command an undo would reverse, or <see langword="null"/>.</summary>
    public string? UndoName => CanUndo ? _done[^1].Name : null;

    /// <summary>Gets the name of the command a redo would reapply, or <see langword="null"/>.</summary>
    public string? RedoName => CanRedo ? _undone[^1].Name : null;

    /// <summary>Gets whether the document differs from the state <see cref="MarkSaved"/> last recorded.</summary>
    public bool IsDirty => _savePointLost || !ReferenceEquals(_savePoint, _done.Count > 0 ? _done[^1] : null);

    /// <summary>Applies a command and records it.</summary>
    /// <param name="command">The command.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public void Execute(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Apply();
        Record(command);
    }

    /// <summary>Records a command whose change has already been applied, such as the final state of a drag.</summary>
    /// <param name="command">The command.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public void Record(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_transactions.TryPeek(out (string Name, List<IEditorCommand> Commands) open))
        {
            open.Commands.Add(command);
            return;
        }

        bool merged = _mergeOpen && _done.Count > 0 && !ReferenceEquals(_done[^1], _savePoint) && _done[^1].TryMerge(command);
        if (!merged)
        {
            _done.Add(command);
        }

        DropRedo();
        _mergeOpen = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Ends the current gesture: the next command starts a new history entry even if it could merge with the last
    /// (a second drag of the same object is a second undo step).
    /// </summary>
    public void BreakMerge() => _mergeOpen = false;

    /// <summary>Starts a transaction: every command until it is disposed becomes one history entry.</summary>
    /// <param name="name">The entry's name.</param>
    /// <returns>A handle; disposing it commits the transaction.</returns>
    public IDisposable BeginTransaction(string name)
    {
        _transactions.Push((name, []));
        return new Transaction(this);
    }

    /// <summary>Reverses the last command.</summary>
    /// <returns><see langword="false"/> when there was nothing to undo.</returns>
    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        IEditorCommand command = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        command.Undo();
        _undone.Add(command);
        _mergeOpen = false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Reapplies the last undone command.</summary>
    /// <returns><see langword="false"/> when there was nothing to redo.</returns>
    public bool Redo()
    {
        if (!CanRedo)
        {
            return false;
        }

        IEditorCommand command = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        command.Apply();
        _done.Add(command);
        _mergeOpen = false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Records that the document as it now stands has been saved.</summary>
    public void MarkSaved()
    {
        _savePoint = _done.Count > 0 ? _done[^1] : null;
        _savePointLost = false;
        _mergeOpen = false;
        Changed?.Invoke();
    }

    /// <summary>Forgets the whole history, as when a different document is opened.</summary>
    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
        _transactions.Clear();
        _savePoint = null;
        _savePointLost = false;
        _mergeOpen = false;
        Changed?.Invoke();
    }

    private void DropRedo()
    {
        // The saved state was reachable only by redo and is gone with it: nothing the history holds is the file now.
        if (_savePoint is not null && _undone.Contains(_savePoint))
        {
            _savePointLost = true;
        }

        _undone.Clear();
    }

    private void Commit()
    {
        (string name, List<IEditorCommand> commands) = _transactions.Pop();
        if (commands.Count == 0)
        {
            return;
        }

        // A transaction is a deliberate unit: it neither merges into the entry before it nor takes the next one in.
        _mergeOpen = false;
        Record(commands.Count == 1 ? commands[0] : new CompositeCommand(name, commands));
        _mergeOpen = false;
    }

    private sealed class Transaction(EditHistory owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Commit();
            }
        }
    }
}
