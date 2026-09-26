namespace OpenIGI.Rendering;

/// <summary>How large the two blocks the deferred shading reads are.</summary>
/// <remarks>
/// Backend-neutral because both backends allocate them and neither decides them: they are the sizes of
/// the constant buffers the shading module declares, and a buffer smaller than what the shader reads is
/// undefined data rather than a failure anything reports.
/// </remarks>
public static class DeferredLightingBlocks
{
    /// <summary>The size in bytes of the lighting pass's GI parameter block.</summary>
    /// <remarks>
    /// The probe field's grid and how it is read, the depth unprojection, the sun and the weather:
    /// thirty-six vectors, which is well inside the sixteen kilobytes every implementation guarantees a
    /// uniform buffer, so no device has to be asked.
    /// </remarks>
    public const int GiBytes = 576;

    /// <summary>The size in bytes of the lighting pass's local-light block.</summary>
    /// <remarks>
    /// Two <c>vec4</c>s of counts and control, then four per light: position and reach, colour and
    /// strength, the direction it faces, and the rectangle it emits from. Sized for <see cref="DeferredLocalLight.Maximum"/>, which
    /// is what the shader's arrays are declared at.
    /// </remarks>
    public const int LampBytes = 32 + (DeferredLocalLight.Maximum * 64);
}
