using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// I.G.I. 2's sky: a graded dome with scrolling layers over it.
/// </summary>
/// <remarks>
/// <para>
/// Declared by all 24 shipped levels. The dome is five colours — a top, two middles and two bottoms —
/// interpolated down its height by <c>SkyDome_InterpolateVertexColour</c> (<c>igi2ed 0x5719B0</c>) into
/// a vertex buffer built once (<c>SkyDome_InitialiseVertexBuffer</c>, <c>0x5717E0</c>). The clouds are
/// separate: each <see cref="FlatSkyLayerTask"/> child is one scrolling texture drawn over the dome.
/// </para>
/// <para>
/// <see cref="FogColour"/> is not only the sky's. The host clears the frame to it so the horizon meets
/// backing of the same colour rather than black, which is the same arrangement Project I.G.I. uses.
/// </para>
/// </remarks>
public sealed partial class FlatSkyTask : Igi2Task
{
    /// <summary>The type name a level declares this under.</summary>
    public const string TypeName = "FlatSky";

    /// <summary>Gets or sets how much the sky is fogged.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fog Amount")]
    public float FogAmount { get; set; }

    /// <summary>Gets or sets the height the flat layers sit at.</summary>
    [ScriptParam(ScriptParamType.Real32, "Z Pos")]
    public float LayerHeight { get; set; }

    /// <summary>Gets or sets how far away the sky is drawn.</summary>
    [ScriptParam(ScriptParamType.Real32, "Distance")]
    public float Distance { get; set; }

    /// <summary>Gets or sets the fog colour, which is also what the frame clears to.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Fog Color")]
    public Vector3f FogColour { get; set; }

    /// <summary>Gets or sets whether the dome's bands step rather than blend.</summary>
    [ScriptParam(ScriptParamType.Bool8, "SkyDome Snap Colours")]
    public bool SnapColours { get; set; }

    /// <summary>Gets or sets the angle the dome's gradient is measured down.</summary>
    [ScriptParam(ScriptParamType.Degrees, "SkyDome Angle")]
    public float DomeAngle { get; set; }

    /// <summary>Gets or sets the colour at the top of the dome.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Top Colour")]
    public Vector3f TopColour { get; set; }

    /// <summary>Gets or sets the first of the dome's two middle colours.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Middle Colour 1")]
    public Vector3f MiddleColour1 { get; set; }

    /// <summary>Gets or sets the second of the dome's two middle colours.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Middle Colour 2")]
    public Vector3f MiddleColour2 { get; set; }

    /// <summary>Gets or sets the first of the dome's two bottom colours.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Bottom Colour 1")]
    public Vector3f BottomColour1 { get; set; }

    /// <summary>Gets or sets the second of the dome's two bottom colours.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Bottom Colour 2")]
    public Vector3f BottomColour2 { get; set; }

    /// <inheritdoc/>
    public override string ToString() => $"FlatSky, distance {Distance}";
}

/// <summary>One scrolling cloud layer over the sky dome.</summary>
/// <remarks>
/// <para>
/// <b>The texture name is rebased, not used as written.</b> <c>FlatSkyLayer_FixFilename</c>
/// (<c>igi2ed 0x653890</c>) strips whatever directory the level authored and re-prefixes the bare file
/// name with <c>MISSION:textures/</c> — keeping the extension, which is why a sky layer resolves to a
/// loose <c>.tga</c> beside the level rather than to anything in an archive.
/// </para>
/// <para>
/// So a level may author any path it likes and the engine will look in exactly one place. That is worth
/// reproducing rather than trusting the authored string: the two agree on the shipped data and would
/// diverge the moment a mod authored a path.
/// </para>
/// </remarks>
public sealed partial class FlatSkyLayerTask : Igi2Task
{
    /// <summary>The type name a level declares this under.</summary>
    public const string TypeName = "FlatSkyLayer";

    /// <summary>The directory every sky texture is looked up in, whatever the level wrote.</summary>
    public const string TextureDirectory = "MISSION:textures/";

    /// <summary>Gets or sets the texture as the level authored it, before rebasing.</summary>
    [ScriptParam(ScriptParamType.String256, "Texture File Name")]
    public string TextureFileName { get; set; } = string.Empty;

    /// <summary>Gets or sets how large the layer's texture is drawn.</summary>
    [ScriptParam(ScriptParamType.Real32, "Scale")]
    public float Scale { get; set; }

    /// <summary>Gets or sets how fast the layer scrolls along X.</summary>
    [ScriptParam(ScriptParamType.Real32, "X Speed")]
    public float SpeedX { get; set; }

    /// <summary>Gets or sets how fast the layer scrolls along Y.</summary>
    [ScriptParam(ScriptParamType.Real32, "Y Speed")]
    public float SpeedY { get; set; }

    /// <summary>Gets or sets how transparent the layer is.</summary>
    [ScriptParam(ScriptParamType.Real32, "Alpha")]
    public float Alpha { get; set; }

    /// <summary>Gets or sets the colour the layer is tinted with.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Color")]
    public Vector3f Colour { get; set; }

    /// <summary>Gets the resource name the layer's texture is actually read from.</summary>
    /// <remarks>The rebasing <c>FlatSkyLayer_FixFilename</c> performs, applied to whatever was authored.</remarks>
    public string TextureResource => TextureDirectory + BareName(TextureFileName);

    /// <inheritdoc/>
    public override string ToString() => $"FlatSkyLayer '{BareName(TextureFileName)}'";

    private static string BareName(string name)
    {
        int slash = name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }
}

/// <summary>One keyframe of a sky that changes colour over the mission.</summary>
/// <remarks>
/// Declared by exactly one shipped level. The five colours are the dome's, and <see cref="Time"/> is
/// when they apply — so a level can take its sky from dawn to dusk without scripting it.
/// </remarks>
public sealed partial class FlatSkyKeyframeTask : Igi2Task
{
    /// <summary>The type name a level declares this under.</summary>
    public const string TypeName = "FlatSkyKeyframe";

    /// <summary>Gets or sets the colour at the top of the dome.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Top Colour")]
    public Vector3f TopColour { get; set; }

    /// <summary>Gets or sets the first of the dome's two middle colours.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Middle Colour 1")]
    public Vector3f MiddleColour1 { get; set; }

    /// <summary>Gets or sets the second of the dome's two middle colours.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Middle Colour 2")]
    public Vector3f MiddleColour2 { get; set; }

    /// <summary>Gets or sets the first of the dome's two bottom colours.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Bottom Colour 1")]
    public Vector3f BottomColour1 { get; set; }

    /// <summary>Gets or sets the second of the dome's two bottom colours.</summary>
    [ScriptParam(ScriptParamType.Rgb, "SkyDome Bottom Colour 2")]
    public Vector3f BottomColour2 { get; set; }

    /// <summary>Gets or sets when this keyframe applies.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time")]
    public float Time { get; set; }

    /// <inheritdoc/>
    public override string ToString() => $"FlatSkyKeyframe at {Time}";
}
