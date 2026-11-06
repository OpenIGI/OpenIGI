namespace OpenIGI.Editor.Core.Commands;

/// <summary>One undoable change to a document.</summary>
/// <remarks>
/// A command is constructed with everything it needs to apply and reverse itself, and the
/// <see cref="EditHistory"/> is the only thing that calls <see cref="Apply"/> and <see cref="Undo"/>, so every edit
/// the editor makes — from a typed field to a gizmo drag to a paste — is one path with one history.
/// </remarks>
public interface IEditorCommand
{
    /// <summary>Gets the name the history panel and the Edit menu show ("Move Crate").</summary>
    string Name { get; }

    /// <summary>Applies the change. Called once on execution and again on every redo.</summary>
    void Apply();

    /// <summary>Reverses the change exactly.</summary>
    void Undo();

    /// <summary>
    /// Absorbs a command executed right after this one, when the two are one gesture (the frames of a drag,
    /// the keystrokes of a field), so a single undo reverses the whole gesture.
    /// </summary>
    /// <param name="following">The command that was just applied.</param>
    /// <returns><see langword="true"/> when <paramref name="following"/> is now part of this command.</returns>
    bool TryMerge(IEditorCommand following) => false;

    /// <summary>
    /// Gets whether <see cref="TryMerge"/> would absorb a command, without absorbing it, so a group can merge all of its
    /// parts or none.
    /// </summary>
    /// <param name="following">The command that was just applied.</param>
    /// <returns><see langword="true"/> when <see cref="TryMerge"/> would succeed.</returns>
    bool CanMerge(IEditorCommand following) => false;
}

/// <summary>A command made of others, applied in order and reversed in reverse order.</summary>
/// <param name="name">The name shown for the whole group.</param>
/// <param name="commands">The commands, already applied when the group is built by a transaction.</param>
public sealed class CompositeCommand(string name, IReadOnlyList<IEditorCommand> commands) : IEditorCommand
{
    /// <inheritdoc/>
    public string Name { get; } = name;

    /// <summary>Gets the commands in the group.</summary>
    public IReadOnlyList<IEditorCommand> Commands { get; } = commands;

    /// <inheritdoc/>
    public void Apply()
    {
        foreach (IEditorCommand command in Commands)
        {
            command.Apply();
        }
    }

    /// <inheritdoc/>
    public void Undo()
    {
        for (int i = Commands.Count - 1; i >= 0; i--)
        {
            Commands[i].Undo();
        }
    }

    /// <inheritdoc/>
    /// <remarks>A group merges with a group of the same name whose parts each merge with its own, part for part.</remarks>
    public bool CanMerge(IEditorCommand following)
    {
        if (following is not CompositeCommand group || group.Name != Name || group.Commands.Count != Commands.Count)
        {
            return false;
        }

        for (int i = 0; i < Commands.Count; i++)
        {
            if (!Commands[i].CanMerge(group.Commands[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TryMerge(IEditorCommand following)
    {
        if (!CanMerge(following))
        {
            return false;
        }

        CompositeCommand group = (CompositeCommand)following;
        for (int i = 0; i < Commands.Count; i++)
        {
            Commands[i].TryMerge(group.Commands[i]);
        }

        return true;
    }
}

/// <summary>A command built from a pair of delegates, for small edits that need no class of their own.</summary>
/// <param name="name">The name shown in the history.</param>
/// <param name="apply">Applies the change.</param>
/// <param name="undo">Reverses it.</param>
public sealed class DelegateCommand(string name, Action apply, Action undo) : IEditorCommand
{
    /// <inheritdoc/>
    public string Name { get; } = name;

    /// <inheritdoc/>
    public void Apply() => apply();

    /// <inheritdoc/>
    public void Undo() => undo();
}
