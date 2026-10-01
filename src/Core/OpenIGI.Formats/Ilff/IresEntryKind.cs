namespace OpenIGI.Formats.Ilff;

/// <summary>The kind of payload an <see cref="IresEntry"/> carries.</summary>
/// <remarks>
/// These mirror the chunk tags the original's archive walker (<c>0x4B60F0</c>) dispatches on, and the
/// resource types it passes to <c>Res_CreateEntry</c>.
/// </remarks>
public enum IresEntryKind
{
    /// <summary>A <c>BODY</c> chunk: the resource's file contents. Registered with resource type 1.</summary>
    Body = 0,

    /// <summary>A <c>PATH</c> chunk: a path node. Registered with resource type 4.</summary>
    Path = 1,

    /// <summary>
    /// A <c>CSTR</c> chunk: a NUL-terminated context string. The language archives use these as the
    /// payload itself — <c>NAME "OBJECTIVE"</c> followed by <c>CSTR "Objective"</c>.
    /// </summary>
    ContextString = 2,
}
