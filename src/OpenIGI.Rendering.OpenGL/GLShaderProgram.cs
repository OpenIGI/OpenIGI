using OpenIGI.Core.Numerics;
using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>A compiled and linked GLSL program, with cached uniform locations.</summary>
internal sealed class GLShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniforms = new(StringComparer.Ordinal);

    private GLShaderProgram(GL gl, uint handle)
    {
        _gl = gl;
        Handle = handle;
    }

    /// <summary>Gets the GL program name.</summary>
    public uint Handle { get; }

    /// <summary>Compiles and links a vertex and fragment shader pair.</summary>
    /// <param name="gl">The GL binding.</param>
    /// <param name="vertexSource">The vertex shader source.</param>
    /// <param name="fragmentSource">The fragment shader source.</param>
    /// <returns>The linked program.</returns>
    /// <exception cref="GraphicsDeviceException">A shader failed to compile, or the program failed to link.</exception>
    public static GLShaderProgram Create(GL gl, string vertexSource, string fragmentSource)
    {
        uint vertex = Compile(gl, ShaderType.VertexShader, vertexSource);
        uint fragment;
        try
        {
            fragment = Compile(gl, ShaderType.FragmentShader, fragmentSource);
        }
        catch
        {
            gl.DeleteShader(vertex);
            throw;
        }

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        gl.LinkProgram(program);

        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        gl.DetachShader(program, vertex);
        gl.DetachShader(program, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        if (linked == 0)
        {
            string log = gl.GetProgramInfoLog(program);
            gl.DeleteProgram(program);
            throw new GraphicsDeviceException($"Shader program failed to link: {log}");
        }

        return new GLShaderProgram(gl, program);
    }

    /// <summary>Makes this program current.</summary>
    public void Use() => _gl.UseProgram(Handle);

    /// <summary>Resolves a uniform's location so a caller can set it without naming it again.</summary>
    /// <param name="name">The uniform name.</param>
    /// <returns>The location, or -1 when the linker optimized the uniform away.</returns>
    /// <remarks>
    /// The per-name overloads below go through a string-keyed dictionary on every call, which is
    /// invisible at a few sets per frame and is not at several per draw. A caller on a hot path resolves
    /// its locations once, at link time, and uses the by-location overloads from then on.
    /// </remarks>
    public int LocationOf(string name) => GetLocation(name);

    /// <summary>Sets a <c>vec2</c> uniform.</summary>
    /// <param name="location">The uniform location, from <see cref="LocationOf"/>.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    public void SetVector2(int location, float x, float y) => _gl.Uniform2(location, x, y);

    /// <summary>Sets a <c>vec2</c> uniform.</summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    public void SetVector2(string name, float x, float y) => _gl.Uniform2(GetLocation(name), x, y);

    /// <summary>Sets a <c>vec3</c> uniform.</summary>
    /// <param name="location">The uniform location, from <see cref="LocationOf"/>.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    /// <remarks>
    /// The size has to match the declaration: <c>glUniform4f</c> against a <c>vec3</c> is an invalid
    /// operation and silently leaves the uniform at zero, which shows up as a scene lit by nothing.
    /// </remarks>
    public void SetVector3(int location, float x, float y, float z) => _gl.Uniform3(location, x, y, z);

    /// <summary>Sets a <c>vec4</c> uniform.</summary>
    /// <param name="location">The uniform location, from <see cref="LocationOf"/>.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    /// <param name="w">The fourth component.</param>
    public void SetVector4(int location, float x, float y, float z, float w) =>
        _gl.Uniform4(location, x, y, z, w);

    /// <summary>Sets a <c>vec4</c> uniform.</summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    /// <param name="w">The fourth component.</param>
    public void SetVector4(string name, float x, float y, float z, float w) =>
        _gl.Uniform4(GetLocation(name), x, y, z, w);

    /// <summary>Sets an <c>int</c> uniform.</summary>
    /// <param name="location">The uniform location, from <see cref="LocationOf"/>.</param>
    /// <param name="value">The value.</param>
    public void SetInt(int location, int value) => _gl.Uniform1(location, value);

    /// <summary>Sets an <c>int</c> uniform.</summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value.</param>
    public void SetInt(string name, int value) => _gl.Uniform1(GetLocation(name), value);

    /// <summary>Sets a <c>float</c> uniform.</summary>
    /// <param name="location">The uniform location, from <see cref="LocationOf"/>.</param>
    /// <param name="value">The value.</param>
    public void SetFloat(int location, float value) => _gl.Uniform1(location, value);

    /// <summary>Sets a <c>float</c> uniform.</summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value.</param>
    public void SetFloat(string name, float value) => _gl.Uniform1(GetLocation(name), value);

    /// <summary>Sets a <c>mat4</c> uniform.</summary>
    /// <param name="location">The uniform location, from <see cref="LocationOf"/>.</param>
    /// <param name="value">The matrix, written out column-major as GL expects.</param>
    public unsafe void SetMatrix4(int location, in Matrix4f value)
    {
        Span<float> elements = stackalloc float[Matrix4f.ComponentCount];
        value.CopyColumnMajorTo(elements);
        fixed (float* data = elements)
        {
            _gl.UniformMatrix4(location, 1, false, data);
        }
    }

    /// <summary>Sets a <c>mat4</c> uniform.</summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The matrix, written out column-major as GL expects.</param>
    public void SetMatrix4(string name, in Matrix4f value) => SetMatrix4(GetLocation(name), value);

    /// <summary>Sets one element of a <c>mat4</c> array uniform.</summary>
    /// <param name="baseLocation">The location of element zero.</param>
    /// <param name="index">The element to set.</param>
    /// <param name="value">The matrix.</param>
    /// <remarks>
    /// A GLSL array's elements occupy consecutive locations, so element <c>i</c> is the base plus
    /// <c>i</c>. Uploading the whole array in one call would need the elements contiguous in memory,
    /// which they are not here.
    /// </remarks>
    public void SetMatrix4(int baseLocation, int index, in Matrix4f value) =>
        SetMatrix4(baseLocation + index, value);

    /// <summary>Sets a <c>vec4</c> array uniform in one call.</summary>
    /// <param name="location">The location of element zero.</param>
    /// <param name="values">The elements, four floats each and tightly packed.</param>
    /// <remarks>
    /// One <c>glUniform4fv</c> for the whole array, which the local-light list wants: unlike the matrix
    /// array above the elements are already contiguous, so there is no reason to pay a call per element.
    /// </remarks>
    public unsafe void SetVector4Array(int location, ReadOnlySpan<float> values)
    {
        fixed (float* data = values)
        {
            _gl.Uniform4(location, (uint)(values.Length / 4), data);
        }
    }

    /// <summary>Sets a <c>mat3</c> uniform.</summary>
    /// <param name="location">The uniform location, from <see cref="LocationOf"/>.</param>
    /// <param name="value">The matrix, in the engine's row-major layout.</param>
    /// <remarks>
    /// GL wants column-major, and the engine stores rows, so the upload transposes. Getting this
    /// backwards applies the inverse rotation, which for an orthonormal matrix is silently plausible
    /// rather than obviously broken.
    /// </remarks>
    public unsafe void SetMatrix3(int location, in Matrix3f value)
    {
        Span<float> columnMajor =
        [
            value.M11, value.M21, value.M31,
            value.M12, value.M22, value.M32,
            value.M13, value.M23, value.M33,
        ];

        fixed (float* data = columnMajor)
        {
            _gl.UniformMatrix3(location, 1, false, data);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _gl.DeleteProgram(Handle);

    private static uint Compile(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled != 0)
        {
            return shader;
        }

        string log = gl.GetShaderInfoLog(shader);
        gl.DeleteShader(shader);
        throw new GraphicsDeviceException($"{type} failed to compile: {log}");
    }

    private int GetLocation(string name)
    {
        if (_uniforms.TryGetValue(name, out int location))
        {
            return location;
        }

        location = _gl.GetUniformLocation(Handle, name);
        _uniforms[name] = location;
        return location;
    }
}
