using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// Runs a shader over every pixel of the current render target.
/// </summary>
/// <remarks>
/// <para>
/// The workhorse of everything the remaster track adds: tone mapping, bloom, ambient occlusion,
/// reflections, fog and temporal filtering are all one shader run over the screen.
/// </para>
/// <para>
/// There is no vertex buffer. The vertex shader builds a triangle from <c>gl_VertexID</c>, so this
/// only has to bind an empty vertex array — core profiles refuse to draw without one — and issue a
/// three-vertex draw. Depth testing and blending are turned off for the duration and put back
/// afterwards, because a full-screen pass writes every pixel unconditionally by definition.
/// </para>
/// </remarks>
internal sealed class GLFullScreenPass : IDisposable
{
    private readonly GL _gl;
    private uint _emptyVertexArray;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="GLFullScreenPass"/> class.</summary>
    /// <param name="gl">The GL binding.</param>
    public GLFullScreenPass(GL gl)
    {
        _gl = gl;
        _emptyVertexArray = gl.GenVertexArray();
    }

    /// <summary>Draws the full-screen triangle with whatever program is current.</summary>
    /// <remarks>The caller binds the program, its uniforms and its textures; this only issues the draw.</remarks>
    public void Draw()
    {
        _gl.BindVertexArray(_emptyVertexArray);

        bool depthWasEnabled = _gl.IsEnabled(EnableCap.DepthTest);
        bool blendWasEnabled = _gl.IsEnabled(EnableCap.Blend);
        bool cullWasEnabled = _gl.IsEnabled(EnableCap.CullFace);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        if (depthWasEnabled)
        {
            _gl.Enable(EnableCap.DepthTest);
        }

        if (blendWasEnabled)
        {
            _gl.Enable(EnableCap.Blend);
        }

        if (cullWasEnabled)
        {
            _gl.Enable(EnableCap.CullFace);
        }

        _gl.BindVertexArray(0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_emptyVertexArray != 0)
        {
            _gl.DeleteVertexArray(_emptyVertexArray);
            _emptyVertexArray = 0;
        }
    }
}
