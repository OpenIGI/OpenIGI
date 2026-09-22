using OpenIGI.Rendering.Pipeline.Draw2D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>A texture table with nothing behind it: every handle resolves, and none of them are real.</summary>
/// <remarks>
/// The passes only ever ask for something bindable and a key to cache it under, which is exactly what
/// this gives them. A handle that names nothing falls back to white, as the device's own table does.
/// </remarks>
public sealed class FakeTextureBindingSource : ITextureBindingSource
{
    private readonly Dictionary<uint, RhiTextureBinding> _bindings = [];
    private readonly IRhiSampler _sampler;
    private readonly RhiTextureBinding _white;

    /// <summary>Initialises a new instance of the <see cref="FakeTextureBindingSource"/> class.</summary>
    /// <param name="device">The device the textures are made on.</param>
    public FakeTextureBindingSource(IRhiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Device = device;
        _sampler = device.CreateSampler(new RhiSamplerDescriptor(Label: "linear"));
        _white = Make(0, "white");
    }

    /// <summary>Gets the device the textures are made on.</summary>
    public IRhiDevice Device { get; }

    /// <summary>Adds a texture that a handle of the same identifier will resolve to.</summary>
    /// <param name="id">The handle identifier.</param>
    /// <returns>The handle.</returns>
    public TextureHandle Add(uint id)
    {
        _bindings[id] = Make(id, $"texture-{id}");
        return new TextureHandle(id);
    }

    /// <inheritdoc/>
    public RhiTextureBinding Resolve(TextureHandle handle) =>
        _bindings.TryGetValue(handle.Id, out RhiTextureBinding binding) ? binding : _white;

    private RhiTextureBinding Make(uint id, string label)
    {
        IRhiTexture texture = Device.CreateTexture(new RhiTextureDescriptor(
            4,
            4,
            RhiTextureFormat.Rgba8Unorm,
            RhiTextureUsage.Sampled,
            Label: label));

        return new RhiTextureBinding(id, texture.View, _sampler);
    }
}
