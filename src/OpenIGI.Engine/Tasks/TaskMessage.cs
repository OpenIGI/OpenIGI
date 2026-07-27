namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A message broadcast through the task tree.
/// </summary>
/// <remarks>
/// <para>
/// The original dispatches through a flat <c>int[254][384]</c> vtable indexed <c>384 * msgId + typeId</c>
/// (<c>0xA96AE0</c>). Ids 0–3 are fixed; everything else is handed out at start-up by <c>Msg_Alloc</c>
/// (<c>0x4017C0</c>) starting at 32. The numeric values below match the fixed ids so the mapping back to
/// the disassembly stays readable.
/// </para>
/// <para>
/// Only <see cref="Update"/>, <see cref="Render"/> and <see cref="PostLoad"/> are ever broadcast through
/// the tree. Construction, destruction, parameter binding and variable export are sent to a single task
/// directly, exactly as the original does.
/// </para>
/// </remarks>
public enum TaskMessage
{
    /// <summary>Construction. Sent by <see cref="TaskTree.New"/> after the task is linked to its parent.</summary>
    Create = 0,

    /// <summary>The 30 Hz logic tick. Broadcast depth-first over the whole tree.</summary>
    Update = 1,

    /// <summary>Destruction. Sent by <see cref="TaskTree.Delete"/> after every child has been destroyed.</summary>
    Destroy = 2,

    /// <summary>Destruction, before children are touched. Sent by <see cref="TaskTree.Delete"/> first.</summary>
    PreDestroy = 3,

    /// <summary>Rendering. The same tree walk as <see cref="Update"/>, and it must not mutate logic state.</summary>
    Render = 32,

    /// <summary>
    /// Sent to one task once its script parameters are bound and its nested children exist.
    /// </summary>
    /// <remarks>The original's <c>dword_54DDB0</c>, read through <c>0x4F1230</c>.</remarks>
    Parsed = 33,

    /// <summary>
    /// Broadcast to the whole tree once, after a level script has finished running.
    /// </summary>
    /// <remarks>
    /// The original's <c>byte_54E0E5</c>, read through <c>0x4F1A70</c> and sent by <c>Level_Load</c>
    /// (<c>0x4F0D40</c>). Distinct from <see cref="Parsed"/>.
    /// </remarks>
    PostLoad = 34,
}
