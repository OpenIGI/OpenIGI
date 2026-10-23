namespace OpenIGI.Scripting.Qvm;

/// <summary>
/// A forward reference in a <see cref="QvmCodeWriter"/>, resolved when the code is finished.
/// </summary>
public sealed class QvmLabel
{
    internal QvmLabel(int id) => Id = id;

    /// <summary>Gets the label's ordinal within the writer that created it, used in diagnostics.</summary>
    public int Id { get; }

    /// <summary>Gets a value indicating whether the label has been placed.</summary>
    public bool IsMarked => Position >= 0;

    internal int Position { get; set; } = -1;
}
