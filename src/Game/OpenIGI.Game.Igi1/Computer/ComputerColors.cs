namespace OpenIGI.Game.Igi1.Computer;

/// <summary>The colours the computer draws in.</summary>
public static class ComputerColors
{
    /// <summary>The text colours, 0 to 128 a channel (<c>0x53F7D0</c>): red, green, pale green and grey.</summary>
    public static readonly (float R, float G, float B)[] Items =
    [
        (128f, 16f, 16f),
        (16f, 96f, 16f),
        (48f, 96f, 48f),
        (128f, 128f, 128f),
    ];

    /// <summary>The marker colours, 0 to 255 a channel (<c>0x53F608</c>): red, yellow, green, pale green and blue.</summary>
    public static readonly (float R, float G, float B)[] Markers =
    [
        (255f, 0f, 0f),
        (255f, 255f, 0f),
        (0f, 255f, 0f),
        (64f, 255f, 64f),
        (0f, 0f, 255f),
    ];

    /// <summary>The colour of the log's rule under each line, 0 to 128 a channel.</summary>
    public static readonly (float R, float G, float B) LogRule = (48f, 96f, 48f);
}
