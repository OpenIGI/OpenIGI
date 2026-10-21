using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>Prepares the first-person rig's private foreground depth layer.</summary>
/// <remarks>
/// The hands and held object are presented after the world. Their private depth attachment must begin
/// at the far value: each part can still occlude the others, but no wall already written by the world
/// pass can cut through them. <see cref="ViewModelProjection"/> supplies the retail layer-zero projection
/// used inside this isolated depth layer.
/// </remarks>
public static class ViewModelDepth
{
    /// <summary>Discards world depth and enables ordinary depth testing for the first-person rig.</summary>
    public static void Begin(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.ClearDepth();
        device.SetDepthState(DepthState.TestAndWrite);
    }
}
