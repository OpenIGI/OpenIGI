namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A task with no behaviour of its own that keeps its children ticking.
/// </summary>
/// <remarks>
/// The original registers one shared forwarder, <c>Task_UpdateChildren</c> (<c>0x488700</c>), as the
/// update method of both <c>Container</c> (<c>0x505C90</c>) and <c>CameraBase</c> (<c>0x4EE970</c>).
/// Forwarding is the entire behaviour: level data uses these purely to group things without cutting
/// them off from the tick.
/// </remarks>
public class ContainerTask : GameTask
{
    /// <inheritdoc/>
    protected override void OnUpdate() => ForwardToChildren(TaskMessage.Update);
}
