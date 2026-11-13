using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>Framebuffer objects, one per set of attachments a pass has drawn into, made once and kept.</summary>
/// <remarks>
/// A render pass names views; OpenGL draws into a framebuffer object those views are attached to. Making one per
/// pass would be a validation per pass, so they are kept by what is attached, and forgotten when an attached view is
/// released -- a framebuffer naming a deleted texture is incomplete, and a new texture may be given the same name.
/// </remarks>
internal sealed class GlFramebuffers(GL gl)
{
    private readonly Dictionary<Key, uint> _framebuffers = [];
    private readonly Dictionary<GlTextureView, List<Key>> _byView = [];

    /// <summary>Gets the framebuffer a pass draws into, made the first time those attachments are asked for.</summary>
    /// <param name="colors">The colour attachments, in order.</param>
    /// <param name="depth">The depth attachment, or null.</param>
    /// <returns>The framebuffer object, bound to the draw target.</returns>
    public uint Bind(IReadOnlyList<GlTextureView> colors, GlTextureView? depth)
    {
        Key key = new([.. colors], depth);
        if (!_framebuffers.TryGetValue(key, out uint framebuffer))
        {
            framebuffer = Make(colors, depth);
            _framebuffers[key] = framebuffer;
            foreach (GlTextureView view in key.Views())
            {
                if (!_byView.TryGetValue(view, out List<Key>? keys))
                {
                    _byView[view] = keys = [];
                }

                keys.Add(key);
            }
        }
        else
        {
            gl.BindFramebuffer(GLEnum.DrawFramebuffer, framebuffer);
        }

        return framebuffer;
    }

    /// <summary>Gets a framebuffer with one colour or depth attachment, for a copy or resolve to read or write.</summary>
    /// <param name="view">The view.</param>
    /// <returns>The framebuffer object; nothing is left bound.</returns>
    public uint Single(GlTextureView view)
    {
        GlTextureView[] colors = view.Format.IsDepth() ? [] : [view];
        GlTextureView? depth = view.Format.IsDepth() ? view : null;
        uint framebuffer = Bind(colors, depth);
        gl.BindFramebuffer(GLEnum.DrawFramebuffer, 0);
        return framebuffer;
    }

    /// <summary>Deletes every framebuffer a view is attached to, as the view goes.</summary>
    /// <param name="view">The view being released.</param>
    public void Forget(GlTextureView view)
    {
        if (!_byView.Remove(view, out List<Key>? keys))
        {
            return;
        }

        foreach (Key key in keys)
        {
            if (_framebuffers.Remove(key, out uint framebuffer))
            {
                gl.DeleteFramebuffer(framebuffer);
            }
        }
    }

    /// <summary>Attaches a view: its first level, and its first layer where it has layers.</summary>
    internal static void Attach(GL gl, GLEnum target, GLEnum attachment, GlTextureView view)
    {
        if (view.Target is GLEnum.Texture2D or GLEnum.Texture2DMultisample)
        {
            gl.FramebufferTexture(target, attachment, view.Name, 0);
        }
        else
        {
            gl.FramebufferTextureLayer(target, attachment, view.Name, 0, 0);
        }
    }

    private uint Make(IReadOnlyList<GlTextureView> colors, GlTextureView? depth)
    {
        uint framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(GLEnum.DrawFramebuffer, framebuffer);

        GLEnum[] drawBuffers = new GLEnum[colors.Count];
        for (int i = 0; i < colors.Count; i++)
        {
            GLEnum attachment = GLEnum.ColorAttachment0 + i;
            Attach(gl, GLEnum.DrawFramebuffer, attachment, colors[i]);
            drawBuffers[i] = attachment;
        }

        if (depth is not null)
        {
            Attach(
                gl,
                GLEnum.DrawFramebuffer,
                depth.Format.HasStencil() ? GLEnum.DepthStencilAttachment : GLEnum.DepthAttachment,
                depth);
        }

        if (drawBuffers.Length == 0)
        {
            gl.DrawBuffer(GLEnum.None);
        }
        else
        {
            gl.DrawBuffers(drawBuffers);
        }

        GLEnum status = gl.CheckFramebufferStatus(GLEnum.DrawFramebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            gl.BindFramebuffer(GLEnum.DrawFramebuffer, 0);
            gl.DeleteFramebuffer(framebuffer);
            throw new InvalidOperationException(
                $"The attachments {string.Join(", ", colors.Select(c => $"'{c.Label}' ({c.Format})"))}"
                + (depth is null ? string.Empty : $" with depth '{depth.Label}' ({depth.Format})")
                + $" do not make a complete framebuffer: {status}.");
        }

        return framebuffer;
    }

    /// <summary>A set of attachments, compared by the views themselves.</summary>
    private sealed class Key(GlTextureView[] colors, GlTextureView? depth) : IEquatable<Key>
    {
        private readonly GlTextureView[] _colors = colors;
        private readonly GlTextureView? _depth = depth;

        public IEnumerable<GlTextureView> Views() => _depth is null ? _colors : _colors.Append(_depth);

        public bool Equals(Key? other) =>
            other is not null
            && ReferenceEquals(_depth, other._depth)
            && _colors.Length == other._colors.Length
            && _colors.AsSpan().SequenceEqual(other._colors, ReferenceEqualityComparer.Instance);

        public override bool Equals(object? obj) => Equals(obj as Key);

        public override int GetHashCode()
        {
            HashCode hash = default;
            hash.Add(_depth is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_depth));
            foreach (GlTextureView color in _colors)
            {
                hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(color));
            }

            return hash.ToHashCode();
        }
    }
}
