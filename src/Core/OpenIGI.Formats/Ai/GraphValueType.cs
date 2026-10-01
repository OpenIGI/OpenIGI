namespace OpenIGI.Formats.Ai;

/// <summary>
/// The eleven value kinds a GraphStream tag can carry. The code is stored twice for every field — once
/// as the field's declared type and once as the tag on the payload itself.
/// </summary>
/// <remarks>
/// <para>
/// The numbering is fixed by the skip routine at <c>0x4FA3B0</c>, which is the only place in the engine
/// that maps every code to a width without knowing what the field means. The reader dispatch at
/// <c>0x4F9CD0</c> agrees with it and adds the interpretation.
/// </para>
/// <para>
/// Codes come in pairs that share a width — <c>0/1</c>, <c>2/3</c>, <c>4/5</c> — which is how a signed
/// and an unsigned form of the same width are told apart. <see cref="Real32"/> shares its width with the
/// 32-bit integers, so <c>4</c>, <c>5</c> and <c>6</c> all skip four bytes.
/// </para>
/// </remarks>
public enum GraphValueType
{
    /// <summary>A single byte, read as a boolean or an unsigned octet.</summary>
    Bool8 = 0,

    /// <summary>A single byte, the signed form of <see cref="Bool8"/>.</summary>
    Int8 = 1,

    /// <summary>Two bytes, unsigned.</summary>
    UInt16 = 2,

    /// <summary>Two bytes, signed.</summary>
    Int16 = 3,

    /// <summary>Four bytes, unsigned.</summary>
    UInt32 = 4,

    /// <summary>Four bytes, signed — the type nine of the eleven graph fields declare.</summary>
    Int32 = 5,

    /// <summary>Four bytes, IEEE single precision.</summary>
    Real32 = 6,

    /// <summary>Eight bytes, IEEE double precision.</summary>
    Real64 = 7,

    /// <summary>Twenty-four bytes — three doubles, the engine's <c>Real64x3</c> world position.</summary>
    Real64x3 = 8,

    /// <summary>A single length byte then that many bytes, the terminating NUL included in the count.</summary>
    String8 = 9,

    /// <summary>A tagged element type, a tagged 32-bit length, then that many bytes.</summary>
    Blob = 10,
}
