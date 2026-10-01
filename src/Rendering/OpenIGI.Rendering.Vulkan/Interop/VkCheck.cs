using OpenIGI.Rendering;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.Interop;

/// <summary>
/// Turns a failing <see cref="Result"/> into the seam's own <see cref="GraphicsDeviceException"/>.
/// </summary>
/// <remarks>
/// Every Vulkan entry point reports through a <see cref="Result"/> rather than by throwing, and a
/// backend that ignores them fails later and elsewhere — a swapchain that was never created shows up
/// as a null handle three calls on. Checking at the call site is what keeps the fault attached to the
/// operation that caused it, which is the whole reason the message names the operation.
/// </remarks>
internal static class VkCheck
{
    /// <summary>Throws when <paramref name="result"/> is not <see cref="Result.Success"/>.</summary>
    /// <param name="result">The result returned by a Vulkan call.</param>
    /// <param name="operation">A short description of what was attempted, for the message.</param>
    /// <exception cref="GraphicsDeviceException"><paramref name="result"/> reports a failure.</exception>
    public static void Ok(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new GraphicsDeviceException($"{operation} failed: {result}.");
        }
    }

    /// <summary>
    /// Throws when <paramref name="result"/> is neither <see cref="Result.Success"/> nor one of the
    /// non-fatal outcomes a caller has chosen to handle itself.
    /// </summary>
    /// <param name="result">The result returned by a Vulkan call.</param>
    /// <param name="operation">A short description of what was attempted, for the message.</param>
    /// <param name="tolerated">Results that are acceptable and must not throw.</param>
    /// <returns><paramref name="result"/>, so the caller can branch on the tolerated outcomes.</returns>
    /// <remarks>
    /// The swapchain path needs this: <see cref="Result.SuboptimalKhr"/> and
    /// <see cref="Result.ErrorOutOfDateKhr"/> are how a resize or a monitor change is reported, and they
    /// are handled by rebuilding the swapchain rather than by failing the frame.
    /// </remarks>
    public static Result Allow(Result result, string operation, params ReadOnlySpan<Result> tolerated)
    {
        if (result == Result.Success)
        {
            return result;
        }

        foreach (Result allowed in tolerated)
        {
            if (result == allowed)
            {
                return result;
            }
        }

        throw new GraphicsDeviceException($"{operation} failed: {result}.");
    }
}
