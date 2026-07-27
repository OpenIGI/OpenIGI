namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A container whose subtree can be frozen wholesale.
/// </summary>
/// <remarks>
/// <c>Dynamic</c>'s update (<c>0x4F0660</c>) is the same child forwarder <see cref="ContainerTask"/>
/// uses, wrapped in a test of one global flag. That flag is the only thing separating the two types,
/// and it is what lets everything dynamic in a level be stopped in a single move.
/// </remarks>
/// <seealso cref="TaskTree.AreDynamicTasksFrozen"/>
public sealed class DynamicTask : GameTask
{
    /// <inheritdoc/>
    protected override void OnUpdate()
    {
        if (Tree.AreDynamicTasksFrozen)
        {
            return;
        }

        ForwardToChildren(TaskMessage.Update);
    }
}
