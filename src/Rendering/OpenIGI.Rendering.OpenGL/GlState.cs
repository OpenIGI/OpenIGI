using OpenIGI.Rendering.Rhi;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>What is bound in the context, so a call that would change nothing is not made.</summary>
/// <remarks>
/// <para>
/// OpenGL is a state machine whose every redundant call still costs a driver's validation, and a frame here binds
/// the same handful of textures and ranges thousands of times. The cache holds what the executor last set; anything
/// that changes context state behind its back -- a resource being created on the edit unit, a copy binding a buffer --
/// either uses a binding point the cache does not track or says what it disturbed.
/// </para>
/// <para>
/// Fixed-function state is compared as one <see cref="GlRasterState"/> per pipeline, with the parts a render pass
/// overrides (depth writes on a read-only depth attachment, masks during a clear) forced back when they apply.
/// </para>
/// </remarks>
internal sealed class GlState(GL gl, int textureUnits, int imageUnits, int bufferUnits)
{
    private readonly (uint Name, GLEnum Target)[] _textures = new (uint, GLEnum)[textureUnits];
    private readonly uint[] _samplers = new uint[textureUnits];
    private readonly (uint Name, long Offset, long Size)[] _uniforms = new (uint, long, long)[bufferUnits];
    private readonly (uint Name, long Offset, long Size)[] _storage = new (uint, long, long)[bufferUnits];
    private readonly (uint Name, GLEnum Access, GLEnum Format, bool Layered)[] _images = new (uint, GLEnum, GLEnum, bool)[imageUnits];
    private readonly (uint Name, long Offset, int Stride)[] _vertexBuffers = NoVertexBuffers();
    private uint _elementBuffer = uint.MaxValue;
    private uint _program;
    private uint _vertexArray = uint.MaxValue;
    private GlRasterState? _raster;
    private bool _depthReadOnly;
    private int _activeUnit = -1;

    /// <summary>Gets the vertex array bound now.</summary>
    public uint VertexArray => _vertexArray;

    /// <summary>Uses a program.</summary>

    public void UseProgram(uint program)
    {
        if (program != _program)
        {
            _program = program;
            gl.UseProgram(program);
        }
    }

    /// <summary>Binds a vertex array, and says whether it changed.</summary>
    public bool BindVertexArray(uint vertexArray)
    {
        if (vertexArray == _vertexArray)
        {
            return false;
        }

        _vertexArray = vertexArray;
        ForgetVertexBuffers();
        gl.BindVertexArray(vertexArray);
        return true;
    }

    /// <summary>Forgets the bound vertex array, after a pipeline was made and left none bound.</summary>
    public void ForgetVertexArray()
    {
        _vertexArray = uint.MaxValue;
        ForgetVertexBuffers();
    }

    /// <summary>Binds a vertex buffer to a slot of the bound vertex array.</summary>
    /// <remarks>
    /// A mesh drawn in several parts, or many times over, names the same buffers before every draw, and a driver
    /// that is told again goes through its whole vertex set-up again; only a change reaches it.
    /// </remarks>
    public void BindVertexBuffer(int slot, uint buffer, long offset, int stride)
    {
        if ((uint)slot < (uint)_vertexBuffers.Length)
        {
            if (_vertexBuffers[slot] == (buffer, offset, stride))
            {
                return;
            }

            _vertexBuffers[slot] = (buffer, offset, stride);
        }

        gl.BindVertexBuffer((uint)slot, buffer, (nint)offset, (uint)stride);
    }

    /// <summary>Binds the element buffer of the bound vertex array.</summary>
    public void BindElementBuffer(uint buffer)
    {
        if (buffer != _elementBuffer)
        {
            _elementBuffer = buffer;
            gl.BindBuffer(GLEnum.ElementArrayBuffer, buffer);
        }
    }

    /// <summary>Binds a texture to a unit.</summary>
    public void BindTexture(int unit, GLEnum target, uint name)
    {
        if ((uint)unit >= (uint)_textures.Length)
        {
            return;
        }

        if (_textures[unit] == (name, target))
        {
            return;
        }


        // A unit holds one texture per target; the one this replaces is unbound so a stale texture of another
        // target cannot make the unit ambiguous to a driver that checks.
        Active(unit);

        if (_textures[unit].Name != 0 && _textures[unit].Target != target)
        {
            gl.BindTexture(_textures[unit].Target, 0);
        }

        gl.BindTexture(target, name);
        _textures[unit] = (name, target);
    }

    /// <summary>Binds a sampler object to a unit; nought for none.</summary>
    public void BindSampler(int unit, uint sampler)
    {
        if ((uint)unit < (uint)_samplers.Length && _samplers[unit] != sampler)
        {
            _samplers[unit] = sampler;
            gl.BindSampler((uint)unit, sampler);
        }
    }

    /// <summary>Binds a uniform buffer range.</summary>
    public void BindUniform(int unit, uint buffer, long offset, long size) =>
        BindRange(_uniforms, GLEnum.UniformBuffer, unit, buffer, offset, size);

    /// <summary>Binds a storage buffer range.</summary>
    public void BindStorage(int unit, uint buffer, long offset, long size) =>
        BindRange(_storage, GLEnum.ShaderStorageBuffer, unit, buffer, offset, size);

    /// <summary>Binds a storage image.</summary>
    public void BindImage(int unit, GlTextureView view, RhiStorageAccess access)
    {
        if ((uint)unit >= (uint)_images.Length)
        {
            return;
        }

        GLEnum glAccess = GlTranslation.ImageAccess(access);
        (uint, GLEnum, GLEnum, bool) wanted = (view.Name, glAccess, view.Gl.Internal, view.Layered);
        if (_images[unit] == wanted)
        {
            return;
        }

        _images[unit] = wanted;
        gl.BindImageTexture((uint)unit, view.Name, 0, view.Layered, 0, glAccess, view.Gl.Internal);
    }

    /// <summary>Forgets a texture bound anywhere, which a deleted name must not be remembered as.</summary>
    public void ForgetTexture(uint name)
    {
        for (int i = 0; i < _textures.Length; i++)
        {
            if (_textures[i].Name == name)
            {
                _textures[i] = default;
            }
        }

        for (int i = 0; i < _images.Length; i++)
        {
            if (_images[i].Name == name)
            {
                _images[i] = default;
            }
        }
    }

    /// <summary>Forgets a buffer bound to any range, as it is deleted: OpenGL may give its name to the next one.</summary>
    public void ForgetBuffer(uint name)
    {
        for (int i = 0; i < _uniforms.Length; i++)
        {
            if (_uniforms[i].Name == name)
            {
                _uniforms[i] = default;
            }

            if (_storage[i].Name == name)
            {
                _storage[i] = default;
            }
        }

        // Deleting a buffer unbinds it from the bound vertex array, and its name may be the next buffer's.
        for (int i = 0; i < _vertexBuffers.Length; i++)
        {
            if (_vertexBuffers[i].Name == name)
            {
                _vertexBuffers[i] = (uint.MaxValue, 0, 0);
            }
        }

        if (_elementBuffer == name)
        {
            _elementBuffer = uint.MaxValue;
        }
    }

    /// <summary>Forgets a sampler bound to any unit, as it is deleted.</summary>
    public void ForgetSampler(uint name)
    {
        for (int i = 0; i < _samplers.Length; i++)
        {
            if (_samplers[i] == name)
            {
                _samplers[i] = 0;
            }
        }
    }

    /// <summary>Forgets a program in use, as it is deleted.</summary>
    public void ForgetProgram(uint name)
    {
        if (_program == name)
        {
            _program = uint.MaxValue;
        }
    }

    /// <summary>Forgets which unit is active, after something else selected one.</summary>
    public void ForgetActiveUnit() => _activeUnit = -1;

    /// <summary>Applies a pipeline's fixed-function state.</summary>
    /// <param name="state">The state.</param>
    /// <param name="depthReadOnly">Whether the pass's depth attachment is read only, which overrides depth writes.</param>
    public void ApplyRaster(GlRasterState state, bool depthReadOnly)
    {
        if (ReferenceEquals(state, _raster) && depthReadOnly == _depthReadOnly)
        {
            return;
        }

        GlRasterState? was = _raster;
        _raster = state;
        _depthReadOnly = depthReadOnly;

        if (was is null || was.Cull != state.Cull)
        {
            if (state.Cull == RhiCullMode.None)
            {
                gl.Disable(EnableCap.CullFace);
            }
            else
            {
                gl.Enable(EnableCap.CullFace);
                gl.CullFace(state.Cull == RhiCullMode.Front ? GLEnum.Front : GLEnum.Back);
            }
        }

        if (was is null || was.FrontFace != state.FrontFace)
        {
            gl.FrontFace(state.FrontFace == RhiFrontFace.Clockwise ? GLEnum.CW : GLEnum.Ccw);
        }

        if (was is null || was.PrimitiveRestart != state.PrimitiveRestart)
        {
            Toggle(EnableCap.PrimitiveRestartFixedIndex, state.PrimitiveRestart);
        }

        if (was is null || was.DepthTest != state.DepthTest || was.DepthCompare != state.DepthCompare)
        {
            Toggle(EnableCap.DepthTest, state.DepthTest);
            gl.DepthFunc(state.DepthCompare);
        }

        gl.DepthMask(state.DepthWrite && !depthReadOnly);

        bool bias = state.DepthBiasConstant != 0f || state.DepthBiasSlope != 0f;
        Toggle(EnableCap.PolygonOffsetFill, bias);
        if (bias)
        {
            gl.PolygonOffset(state.DepthBiasSlope, state.DepthBiasConstant);
        }

        for (int i = 0; i < state.Targets.Length; i++)
        {
            RhiColorTargetState target = state.Targets[i];
            uint index = (uint)i;

            if (target.Blend is { } blend)
            {
                gl.Enable(EnableCap.Blend, index);
                gl.BlendFuncSeparate(
                    index,
                    GlTranslation.BlendFactor(blend.Color.Source),
                    GlTranslation.BlendFactor(blend.Color.Destination),
                    GlTranslation.BlendFactor(blend.Alpha.Source),
                    GlTranslation.BlendFactor(blend.Alpha.Destination));
                gl.BlendEquationSeparate(
                    index,
                    GlTranslation.BlendOperation(blend.Color.Operation),
                    GlTranslation.BlendOperation(blend.Alpha.Operation));
            }
            else
            {
                gl.Disable(EnableCap.Blend, index);
            }

            gl.ColorMask(
                index,
                (target.Write & RhiColorWrite.Red) != 0,
                (target.Write & RhiColorWrite.Green) != 0,
                (target.Write & RhiColorWrite.Blue) != 0,
                (target.Write & RhiColorWrite.Alpha) != 0);
        }
    }

    /// <summary>Opens every mask a clear writes through, and forgets the pipeline state that closed them.</summary>
    public void OpenMasksForClear()
    {
        gl.ColorMask(true, true, true, true);
        gl.DepthMask(true);
        _raster = null;
    }

    /// <summary>Forgets everything, after the context's state was changed wholesale.</summary>
    public void ForgetAll()
    {
        Array.Clear(_textures);
        Array.Clear(_samplers);
        Array.Clear(_uniforms);
        Array.Clear(_storage);
        Array.Clear(_images);
        _program = 0;
        _vertexArray = uint.MaxValue;
        ForgetVertexBuffers();
        _raster = null;
        _activeUnit = -1;
        gl.UseProgram(0);
    }

    private void BindRange((uint Name, long Offset, long Size)[] cache, GLEnum target, int unit, uint buffer, long offset, long size)
    {
        if ((uint)unit >= (uint)cache.Length || cache[unit] == (buffer, offset, size))
        {
            return;
        }

        cache[unit] = (buffer, offset, size);
        gl.BindBufferRange(target, (uint)unit, buffer, (nint)offset, (nuint)size);
    }

    // Vertex and element buffer bindings belong to the vertex array, so another array starts from nothing known.
    private void ForgetVertexBuffers()
    {
        Array.Fill(_vertexBuffers, (uint.MaxValue, 0L, 0));
        _elementBuffer = uint.MaxValue;
    }

    private static (uint Name, long Offset, int Stride)[] NoVertexBuffers()
    {
        var slots = new (uint, long, int)[16];
        Array.Fill(slots, (uint.MaxValue, 0L, 0));
        return slots;
    }

    private void Active(int unit)
    {
        if (unit != _activeUnit)
        {
            _activeUnit = unit;
            gl.ActiveTexture(GLEnum.Texture0 + unit);
        }
    }

    private void Toggle(EnableCap cap, bool on)
    {
        if (on)
        {
            gl.Enable(cap);
        }
        else
        {
            gl.Disable(cap);
        }
    }
}
