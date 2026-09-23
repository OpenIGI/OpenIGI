using OpenIGI.Rendering.Rhi;

namespace OpenIGI.TestSupport.Rhi;

/// <summary>One thing a frame asked the device to do.</summary>
/// <remarks>
/// <para>
/// The point of recording rather than drawing: what a pass class is worth testing for is the SHAPE of
/// the frame it produces — which passes run, in what order, with what attached and what bound — and none
/// of that needs a GPU to check. A picture can only be compared against another picture, which means a
/// reference that goes stale and a failure that says only "different".
/// </para>
/// <para>
/// Every record is what the caller asked for, not what a backend would make of it. A test that asserts
/// on these is asserting about the renderer, which is the thing being moved.
/// </para>
/// </remarks>
public abstract record RecordedRhiCommand
{
    /// <summary>A render pass began.</summary>
    /// <param name="Label">Its name.</param>
    /// <param name="ColorAttachments">What it draws into, by label.</param>
    /// <param name="DepthAttachment">The depth target's label, or null for none.</param>
    /// <param name="Clears">Which colour attachments are cleared rather than loaded.</param>
    public sealed record BeginRenderPass(
        string Label,
        IReadOnlyList<string> ColorAttachments,
        string? DepthAttachment,
        IReadOnlyList<bool> Clears) : RecordedRhiCommand;

    /// <summary>A compute pass began.</summary>
    /// <param name="Label">Its name.</param>
    public sealed record BeginComputePass(string Label) : RecordedRhiCommand;

    /// <summary>A pass ended.</summary>
    /// <param name="Label">The name of the pass that ended.</param>
    public sealed record EndPass(string Label) : RecordedRhiCommand;

    /// <summary>A pipeline was bound.</summary>
    /// <param name="Label">Its name.</param>
    public sealed record SetPipeline(string Label) : RecordedRhiCommand;

    /// <summary>A bind group was bound.</summary>
    /// <param name="Index">Which group.</param>
    /// <param name="Label">The group's name.</param>
    /// <param name="DynamicOffsets">The offsets it was bound at.</param>
    public sealed record SetBindGroup(
        int Index,
        string Label,
        IReadOnlyList<uint> DynamicOffsets) : RecordedRhiCommand;

    /// <summary>Push data was set.</summary>
    /// <param name="Bytes">What was pushed, copied.</param>
    /// <param name="Offset">Where in the block it went.</param>
    public sealed record SetPushData(IReadOnlyList<byte> Bytes, int Offset) : RecordedRhiCommand;

    /// <summary>A draw was recorded.</summary>
    /// <param name="VertexCount">How many vertices.</param>
    /// <param name="InstanceCount">How many instances.</param>
    /// <param name="Indexed">Whether it went through the index buffer.</param>
    public sealed record Draw(int VertexCount, int InstanceCount, bool Indexed) : RecordedRhiCommand;

    /// <summary>The viewport was set.</summary>
    /// <param name="X">Its left edge.</param>
    /// <param name="Y">Its top edge.</param>
    /// <param name="Width">Its width.</param>
    /// <param name="Height">Its height.</param>
    /// <param name="MinDepth">The near end of its depth range.</param>
    /// <param name="MaxDepth">The far end.</param>
    public sealed record SetViewport(
        float X,
        float Y,
        float Width,
        float Height,
        float MinDepth,
        float MaxDepth) : RecordedRhiCommand;

    /// <summary>The scissor was set.</summary>
    /// <param name="X">Its left edge.</param>
    /// <param name="Y">Its top edge.</param>
    /// <param name="Width">Its width.</param>
    /// <param name="Height">Its height.</param>
    public sealed record SetScissor(int X, int Y, int Width, int Height) : RecordedRhiCommand;

    /// <summary>A dispatch was recorded.</summary>
    /// <param name="X">Workgroups along x.</param>
    /// <param name="Y">Workgroups along y.</param>
    /// <param name="Z">Workgroups along z.</param>
    public sealed record Dispatch(int X, int Y, int Z) : RecordedRhiCommand;

    /// <summary>A copy between two buffers.</summary>
    /// <param name="Source">The source's label.</param>
    /// <param name="Destination">The destination's label.</param>
    /// <param name="SizeInBytes">How much was copied.</param>
    public sealed record CopyBufferToBuffer(
        string Source,
        string Destination,
        ulong SizeInBytes) : RecordedRhiCommand;

    /// <summary>A copy out of a texture.</summary>
    /// <param name="Source">The texture's label.</param>
    /// <param name="Destination">The buffer's label.</param>
    public sealed record CopyTextureToBuffer(string Source, string Destination) : RecordedRhiCommand;

    /// <summary>A copy from one texture into another.</summary>
    /// <param name="Source">The texture copied from.</param>
    /// <param name="Destination">The texture copied into.</param>
    public sealed record CopyTextureToTexture(string Source, string Destination) : RecordedRhiCommand;

    /// <summary>A copy into a texture.</summary>
    /// <param name="Source">The buffer's label.</param>
    /// <param name="Destination">The texture's label.</param>
    public sealed record CopyBufferToTexture(string Source, string Destination) : RecordedRhiCommand;

    /// <summary>A host write into a buffer.</summary>
    /// <param name="Label">The buffer's label.</param>
    /// <param name="Offset">Where it went.</param>
    /// <param name="SizeInBytes">How much.</param>
    public sealed record WriteBuffer(string Label, ulong Offset, int SizeInBytes) : RecordedRhiCommand;

    /// <summary>A host write into a texture.</summary>
    /// <param name="Label">The texture's label.</param>
    /// <param name="SizeInBytes">How much.</param>
    public sealed record WriteTexture(string Label, int SizeInBytes) : RecordedRhiCommand;

    /// <summary>Recorded work was submitted.</summary>
    /// <param name="Labels">The names of what was submitted, in order.</param>
    public sealed record Submit(IReadOnlyList<string> Labels) : RecordedRhiCommand;

    /// <summary>A frame was acquired.</summary>
    /// <param name="Index">Which image.</param>
    /// <param name="FrameInFlight">Which set of per-frame resources it may write.</param>
    public sealed record BeginFrame(int Index, int FrameInFlight) : RecordedRhiCommand;

    /// <summary>A frame was presented.</summary>
    /// <param name="Index">Which image.</param>
    public sealed record EndFrame(int Index) : RecordedRhiCommand;
}
