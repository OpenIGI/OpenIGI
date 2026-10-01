namespace OpenIGI.Platform;

/// <summary>Carries the new client size of a resized window.</summary>
public sealed class WindowResizedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="WindowResizedEventArgs"/> class.</summary>
    /// <param name="width">The new client width in pixels.</param>
    /// <param name="height">The new client height in pixels.</param>
    public WindowResizedEventArgs(int width, int height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Gets the new client width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the new client height in pixels.</summary>
    public int Height { get; }
}
