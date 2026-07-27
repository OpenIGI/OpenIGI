namespace OpenIGI.Engine.Configuration;

/// <summary>The value type a <see cref="ConfigVariable"/> exposes.</summary>
public enum ConfigVariableKind
{
    /// <summary>A boolean, stored as a 4-byte integer in the original.</summary>
    Boolean = 0,

    /// <summary>A 32-bit signed integer.</summary>
    Integer = 1,

    /// <summary>A double-precision real.</summary>
    Real = 2,

    /// <summary>A string.</summary>
    String = 3,
}
