using OpenIGI.Core.Graphics;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// The signed-distance-field text path: <see cref="ISdfTextDevice"/> on the OpenGL device.
/// </summary>
/// <remarks>
/// A partial of <see cref="GLGraphicsDevice"/> — like the compute path — so it sits in its own file
/// without disturbing the main device. The program is compiled lazily on first use, because a build that
/// never draws SDF text (the vanilla preset, a headless capture) should not pay to compile it; once
/// built it lives for the device's lifetime and is released in the device's <c>Dispose</c> through
/// <see cref="DisposeTextResources"/>.
/// </remarks>
public sealed partial class GLGraphicsDevice : ISdfTextDevice
{
    private GLShaderProgram? _sdfProgram;
    private int _sdfInverseScreen = -1;
    private int _sdfPxRange = -1;
    private int _sdfOutline = -1;
    private int _sdfGlow = -1;
    private int _sdfGlowWidth = -1;

    /// <inheritdoc/>
    public unsafe void DrawText2D(
        ReadOnlySpan<Vertex2D> vertices,
        ReadOnlySpan<ushort> indices,
        TextureHandle atlas,
        in SdfTextParams parameters,
        BlendMode blend)
    {
        ThrowIfDisposed();

        if (indices.Length % 3 != 0)
        {
            throw new ArgumentException($"An index buffer must be a multiple of three, got {indices.Length}.", nameof(indices));
        }

        if (vertices.IsEmpty || indices.IsEmpty)
        {
            return;
        }

        if (vertices.Length > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                $"A 16-bit index buffer addresses at most {ushort.MaxValue + 1} vertices, got {vertices.Length}.",
                nameof(vertices));
        }

        EnsureSdfProgram();
        GLShaderProgram program = _sdfProgram!;
        UseProgram(program);

        program.SetVector2(_sdfInverseScreen, 2.0f / _projectionWidth, 2.0f / _projectionHeight);
        program.SetFloat(_sdfPxRange, parameters.PxRange);
        program.SetVector4(_sdfOutline, Red(parameters.OutlineColor), Green(parameters.OutlineColor), Blue(parameters.OutlineColor), parameters.OutlineWidth);
        program.SetVector4(_sdfGlow, Red(parameters.GlowColor), Green(parameters.GlowColor), Blue(parameters.GlowColor), parameters.GlowIntensity);
        program.SetFloat(_sdfGlowWidth, parameters.GlowWidth);

        BindTexture(0, NameOf(atlas, nameof(atlas)));
        ApplyBlend(blend);

        BindVertexArray(_vertexArray);
        UploadStream(BufferTargetARB.ArrayBuffer, _vertexBuffer, vertices, Vertex2D.Size);
        UploadStream(BufferTargetARB.ElementArrayBuffer, _indexBuffer, indices, sizeof(ushort));

        _gl.DrawElements(PrimitiveType.Triangles, (uint)indices.Length, DrawElementsType.UnsignedShort, (void*)0);
    }

    /// <summary>Releases the SDF program. Called from the device's <c>Dispose</c>.</summary>
    private void DisposeTextResources()
    {
        _sdfProgram?.Dispose();
        _sdfProgram = null;
    }

    private void EnsureSdfProgram()
    {
        if (_sdfProgram is not null)
        {
            return;
        }

        _sdfProgram = GLShaderProgram.Create(_gl, Sprite2DShaders.Vertex, SdfTextShaders.Fragment);
        _sdfProgram.Use();
        _sdfProgram.SetInt("uTexture", 0);
        _sdfInverseScreen = _sdfProgram.LocationOf("uInverseScreen");
        _sdfPxRange = _sdfProgram.LocationOf("uPxRange");
        _sdfOutline = _sdfProgram.LocationOf("uOutline");
        _sdfGlow = _sdfProgram.LocationOf("uGlow");
        _sdfGlowWidth = _sdfProgram.LocationOf("uGlowWidth");
    }

    private static float Red(Bgra32 color) => color.R / 255f;

    private static float Green(Bgra32 color) => color.G / 255f;

    private static float Blue(Bgra32 color) => color.B / 255f;
}
