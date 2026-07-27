using System.Diagnostics.CodeAnalysis;

namespace OpenIGI.Core.Diagnostics;

/// <summary>
/// Signals a condition the original engine treated as unrecoverable.
/// </summary>
/// <remarks>
/// <para>
/// This models <c>Sys_FatalError</c> (<c>0x4AF7B0</c>), which formats a message, hands it to the
/// display and terminate hooks, and <em>never returns</em> — its callers busy-spin afterwards. The
/// port keeps that contract: where the original was fatal, the port throws and lets the host decide how
/// to report it. Silently recovering instead would let corrupt data flow into game logic and diverge
/// from the original's observable behaviour.
/// </para>
/// <para>
/// This is not the exception for expected, recoverable failures (a missing optional resource, a bad
/// user-supplied path); use ordinary exception types for those.
/// </para>
/// </remarks>
public sealed class EngineFatalException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="EngineFatalException"/> class.</summary>
    public EngineFatalException()
        : base("Fatal engine error.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EngineFatalException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    public EngineFatalException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EngineFatalException"/> class.</summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The underlying cause.</param>
    public EngineFatalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Throws an <see cref="EngineFatalException"/>. This method never returns.</summary>
    /// <param name="message">The message describing the failure.</param>
    /// <exception cref="EngineFatalException">Always.</exception>
    /// <remarks>
    /// Call sites read like the original's <c>Sys_FatalError(...)</c> and the compiler knows control
    /// does not continue past them.
    /// </remarks>
    [DoesNotReturn]
    public static void Throw(string message) => throw new EngineFatalException(message);

    /// <summary>Throws an <see cref="EngineFatalException"/> when <paramref name="condition"/> is <see langword="false"/>.</summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <param name="message">The message describing the failure.</param>
    /// <exception cref="EngineFatalException"><paramref name="condition"/> is <see langword="false"/>.</exception>
    public static void ThrowIfNot([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new EngineFatalException(message);
        }
    }
}
