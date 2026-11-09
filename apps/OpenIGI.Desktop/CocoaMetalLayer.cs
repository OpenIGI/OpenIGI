using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenIGI.Platform;

namespace OpenIGI.Desktop;

/// <summary>Backs a Cocoa window's view with a Metal layer, which is what a WebGPU surface on macOS is made from.</summary>
/// <remarks>
/// <para>
/// A Vulkan window on macOS gets its layer from the surface the windowing library creates; WebGPU makes its own surface, so
/// nothing has put a layer on the view and one has to be attached first. Talked to through the Objective-C runtime, so no
/// binding library is needed for four messages.
/// </para>
/// <para>
/// AppKit wants its views touched on the main thread, which is the thread the window and the renderer are created on.
/// </para>
/// </remarks>
[SupportedOSPlatform("macos")]
internal static partial class CocoaMetalLayer
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    // CAMetalLayer lives here; its class is not registered with the runtime until the framework is loaded.
    private const string QuartzCore = "/System/Library/Frameworks/QuartzCore.framework/QuartzCore";

    /// <summary>Attaches a new Metal layer to the window's content view, scaled to the window's backing store.</summary>
    /// <param name="window">The <c>NSWindow</c>.</param>
    /// <returns>The <c>CAMetalLayer</c>, owned by the view.</returns>
    /// <exception cref="PlatformException">The window has no view, or Metal layers are unavailable.</exception>
    public static nint Attach(nint window)
    {
        if (window == 0)
        {
            throw new PlatformException("The window has no NSWindow to attach a Metal layer to.");
        }

        if (!NativeLibrary.TryLoad(QuartzCore, out _))
        {
            throw new PlatformException("QuartzCore could not be loaded, so there is no Metal layer for WebGPU to present to.");
        }

        nint layerClass = GetClass("CAMetalLayer");
        nint view = Send(window, Selector("contentView"));
        if (layerClass == 0 || view == 0)
        {
            throw new PlatformException("The window has no view a Metal layer can be attached to.");
        }

        // The class's factory returns an autoreleased layer; the view retains it once it is set.
        nint layer = Send(layerClass, Selector("layer"));
        SendBool(view, Selector("setWantsLayer:"), 1);
        SendPointer(view, Selector("setLayer:"), layer);

        // Without this a Retina display is drawn at half its resolution and stretched.
        SendDouble(layer, Selector("setContentsScale:"), SendReturningDouble(window, Selector("backingScaleFactor")));
        return layer;
    }

    private static nint Selector(string name) => RegisterSelector(name);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(ObjectiveC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterSelector(string name);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial void SendPointer(nint receiver, nint selector, nint argument);

    // BOOL is a signed char on x86-64 and a bool on arm64; one byte is both.
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial void SendBool(nint receiver, nint selector, byte argument);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial void SendDouble(nint receiver, nint selector, double argument);

    // A double comes back in a floating-point register through plain objc_msgSend on both x86-64 and arm64.
    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    private static partial double SendReturningDouble(nint receiver, nint selector);
}
