namespace OpenIGI.Rendering.Rhi;

/// <summary>What a specialisation constant holds.</summary>
/// <remarks>
/// Carried rather than inferred because the two backends want the value in different forms and only
/// one of them can work it out for itself. Vulkan takes four raw bytes and lets the shader's own
/// declaration say what they mean; WebGPU takes a number and converts it to the declared type, which
/// means the bits of a float and the bits of an integer are not interchangeable there. A constant that
/// did not say which it was would be a float arriving as a very large integer.
/// </remarks>
public enum RhiConstantKind
{
    /// <summary>A single-precision float.</summary>
    Float,

    /// <summary>A signed 32-bit integer.</summary>
    Int,

    /// <summary>An unsigned 32-bit integer.</summary>
    UInt,

    /// <summary>A boolean, which every target carries as nought or one.</summary>
    Bool,
}

/// <summary>
/// One value fixed when a pipeline is created rather than when its module was compiled.
/// </summary>
/// <remarks>
/// Vulkan calls these specialisation constants and WebGPU calls them pipeline-overridable constants.
/// They are the same idea: a value the compiler may fold, so a branch on one is gone by the time
/// anything runs. This renderer spends them on the things that have no push room left — the cut-out
/// threshold, the level's fog and sun, and which way the target's clip space runs.
/// </remarks>
public readonly record struct RhiConstant
{
    private RhiConstant(RhiConstantKind kind, uint bits, double number)
    {
        Kind = kind;
        Bits = bits;
        Number = number;
    }

    /// <summary>Gets what the value is.</summary>
    public RhiConstantKind Kind { get; }

    /// <summary>Gets the four bytes a Vulkan pipeline is specialised with.</summary>
    public uint Bits { get; }

    /// <summary>Gets the number a WebGPU pipeline is overridden with.</summary>
    public double Number { get; }

    /// <summary>A float constant.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The constant.</returns>
    public static RhiConstant Of(float value) =>
        new(RhiConstantKind.Float, BitConverter.SingleToUInt32Bits(value), value);

    /// <summary>A signed integer constant.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The constant.</returns>
    public static RhiConstant Of(int value) => new(RhiConstantKind.Int, unchecked((uint)value), value);

    /// <summary>An unsigned integer constant.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The constant.</returns>
    public static RhiConstant Of(uint value) => new(RhiConstantKind.UInt, value, value);

    /// <summary>A boolean constant.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The constant.</returns>
    public static RhiConstant Of(bool value) => new(RhiConstantKind.Bool, value ? 1u : 0u, value ? 1d : 0d);
}
