using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Computer;

/// <summary>What a mark on the computer's map is, <c>ComputerObject</c>'s three draws.</summary>
public enum Igi2ComputerMarkKind
{
    /// <summary>A marker about the object, <c>ComputerObject_DrawMarker</c>.</summary>
    Marker,

    /// <summary>A highlight's sprite on top of the object, <c>ComputerObject_DrawSprite</c>.</summary>
    Sprite,

    /// <summary>A highlight's text beside it, <c>ComputerObject_DrawText</c>.</summary>
    Text,
}

/// <summary>The shapes a marker takes, <c>ComputerObjectMarker_e</c>.</summary>
public enum Igi2ComputerMarker
{
    /// <summary>None.</summary>
    None = -1,

    /// <summary>A box of lines about the object.</summary>
    Box = 0,

    /// <summary>A triangle pointing the object's way.</summary>
    Triangle = 1,

    /// <summary>A larger triangle.</summary>
    LargeTriangle = 2,

    /// <summary>A diamond.</summary>
    Diamond = 3,
}

/// <summary>The colours a marker takes, <c>ComputerObjectMarkerColor_e</c> over <c>ComputerObject_atMarkerColor</c>.</summary>
public enum Igi2ComputerMarkerColour
{
    /// <summary>None.</summary>
    None = -1,

    /// <summary>Red.</summary>
    Red = 0,

    /// <summary>Yellow, the vehicles'.</summary>
    Yellow = 1,

    /// <summary>Green, the player's.</summary>
    Green = 2,

    /// <summary>Bright green.</summary>
    BrightGreen = 3,

    /// <summary>Blue.</summary>
    Blue = 4,

    /// <summary>Bright blue, which tints the highlights' sprites.</summary>
    BrightBlue = 5,
}

/// <summary>
/// One mark the map draws over the level, as the objects' <c>DrawComputerObject</c> handlers ask for it: where, and what.
/// </summary>
/// <param name="Kind">What is drawn.</param>
/// <param name="Position">Where, in the world; a sprite's and a text's already put on the object's top.</param>
/// <param name="Name">The sprite's name, or the text.</param>
/// <param name="Marker">The marker's shape.</param>
/// <param name="Colour">The marker's colour.</param>
/// <param name="Radius">The object's radius, which sizes the marker.</param>
/// <param name="Gamma">The object's heading, which turns the marker.</param>
public readonly record struct Igi2ComputerMapMark(
    Igi2ComputerMarkKind Kind,
    Vector3d Position,
    string Name = "",
    Igi2ComputerMarker Marker = Igi2ComputerMarker.None,
    Igi2ComputerMarkerColour Colour = Igi2ComputerMarkerColour.None,
    float Radius = 0f,
    float Gamma = 0f)
{
    /// <summary>The smallest radius a marker is drawn for, <c>3072</c>.</summary>
    public const float MinimumRadius = 3072f;

    /// <summary>A soldier's marker's radius, <c>3686.4</c>.</summary>
    public const float HumanRadius = 3686.39990234375f;

    /// <summary>The highlights' sprites by index, <c>ComputerMenu_tComputerMenuResource.azQSpriteHilight</c>.</summary>
    public static IReadOnlyList<string> HilightSprites { get; } =
    [
        "h_player", "h_guard", "h_camera", "h_computer", "h_cabinet", "h_mine",
        "h_0", "h_1", "h_2", "h_3", "h_4", "h_5", "h_6", "h_7", "h_8", "h_9",
        "h_sentrygun", "h_scamera1", "h_scamera2", "h_b",
    ];

    /// <summary>The marker colours, <c>ComputerObject_atMarkerColor</c>, 255 for full.</summary>
    public static IReadOnlyList<(float R, float G, float B)> Colours { get; } =
    [
        (255f, 0f, 0f), (255f, 255f, 0f), (0f, 200f, 0f), (64f, 255f, 64f), (0f, 0f, 255f), (102f, 153f, 255f),
    ];

    /// <summary>The box marker's outline, <c>0x6B7E54</c>: a square of side two about the object.</summary>
    public static IReadOnlyList<(float X, float Y)> BoxOutline { get; } = [(-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f), (-1f, -1f)];
}
