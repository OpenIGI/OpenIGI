using Silk.NET.OpenGL;

namespace OpenIGI.Rendering.OpenGL;

/// <summary>
/// What the context that was actually created can do.
/// </summary>
/// <remarks>
/// <para>
/// The desktop asks for the newest context the driver will give and falls back through 4.6, 4.3 and
/// 3.3. That range is deliberate rather than incidental: <b>4.3 is where compute shaders, shader
/// storage buffers and image load/store arrive</b>, which is what lets the remaster track's fog,
/// ambient occlusion and probe lighting run as compute over a volume instead of as fragment passes
/// over a full-screen quad. <b>3.3 is the floor</b>, and it is what the web and mobile backends will
/// target — the game runs there too, with the cheaper implementation of each effect.
/// </para>
/// <para>
/// Read from the context rather than from the version the window asked for, because a driver may hand
/// back something other than what it was asked for and the shaders have to match what exists.
/// </para>
/// </remarks>
internal readonly record struct GLFeatureLevel(int Major, int Minor)
{
    /// <summary>The lowest context this backend runs on.</summary>
    public static GLFeatureLevel Minimum { get; } = new(3, 3);

    /// <summary>Gets a value indicating whether compute shaders are available.</summary>
    public bool SupportsCompute => AtLeast(4, 3);

    /// <summary>Gets a value indicating whether a shader can write to a texture through an image unit.</summary>
    /// <remarks>Arrives with compute, and is what a froxel volume is filled through.</remarks>
    public bool SupportsImageLoadStore => AtLeast(4, 2);

    /// <summary>Gets a value indicating whether shader storage buffers are available.</summary>
    public bool SupportsShaderStorageBuffers => AtLeast(4, 3);

    /// <summary>Gets a value indicating whether a shader can share values across a wavefront directly.</summary>
    /// <remarks>
    /// <para>
    /// The tracing and culling stages lean on these heavily — summing across a wave, compacting a list
    /// by prefix count, broadcasting one lane's answer to the rest. Every one of them has a fallback
    /// through shared memory, so this decides how fast those passes are rather than whether they run.
    /// </para>
    /// <para>
    /// Reported from the version rather than probed for the extension string. <c>GL_KHR_shader_subgroup</c>
    /// is exposed by every current desktop driver at 4.6, and a context that reports 4.6 without it is
    /// one this project has no way to test against — so the version is the honest signal and the shared
    /// memory path is what an older context gets.
    /// </para>
    /// </remarks>
    public bool SupportsSubgroups => AtLeast(4, 6);

    /// <summary>Gets a value indicating whether objects can be modified without being bound first.</summary>
    /// <remarks>Not a feature the player can see; it removes a great deal of bind-and-restore from the backend.</remarks>
    public bool SupportsDirectStateAccess => AtLeast(4, 5);

    /// <summary>Gets a value indicating whether the depth range can be set to <c>[0, 1]</c>.</summary>
    /// <remarks>
    /// With a reversed-Z projection this is what buys back the depth precision a 4 km far plane
    /// otherwise throws away — the thing that makes distant terrain stop z-fighting.
    /// </remarks>
    public bool SupportsClipControl => AtLeast(4, 5);

    /// <summary>Gets the GLSL version directive shaders for this context should declare.</summary>
    /// <remarks>
    /// A 3.3-era shader compiles unchanged under a newer context, so only sources that actually need a
    /// newer feature declare a newer version. This is what those use.
    /// </remarks>
    public string GlslVersion => this switch
    {
        { Major: >= 4, Minor: >= 6 } or { Major: > 4 } => "#version 460 core",
        { Major: 4, Minor: >= 3 } => "#version 430 core",
        _ => "#version 330 core",
    };

    /// <summary>Reads the feature level out of the current context.</summary>
    /// <param name="gl">The GL binding.</param>
    /// <returns>The detected level, never below <see cref="Minimum"/>.</returns>
    public static GLFeatureLevel Detect(GL gl)
    {
        ArgumentNullException.ThrowIfNull(gl);

        // GL_MAJOR_VERSION and GL_MINOR_VERSION exist from 3.0; a driver that refuses them is older
        // than anything this backend supports, so the floor is the honest answer.
        try
        {
            gl.GetInteger(GetPName.MajorVersion, out int major);
            gl.GetInteger(GetPName.MinorVersion, out int minor);

            GLFeatureLevel detected = new(major, minor);
            return detected.AtLeast(Minimum.Major, Minimum.Minor) ? detected : Minimum;
        }
        catch (InvalidOperationException)
        {
            return Minimum;
        }
    }

    /// <summary>Determines whether the context is at least a given version.</summary>
    /// <param name="major">The major version to test against.</param>
    /// <param name="minor">The minor version to test against.</param>
    /// <returns><see langword="true"/> when the context is that version or newer.</returns>
    public bool AtLeast(int major, int minor) => Major > major || (Major == major && Minor >= minor);

    /// <summary>Returns the version as <c>major.minor</c>.</summary>
    /// <returns>The version text.</returns>
    public override string ToString() => $"{Major}.{Minor}";
}
