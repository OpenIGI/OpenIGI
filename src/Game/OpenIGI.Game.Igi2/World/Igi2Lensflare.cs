using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One quad of a lens flare's sprite on the screen, in pixels, with the texture coordinates 0.01 to 0.99 across it.</summary>
/// <param name="Sprite">The sprite, <c>lens0N.tex</c>, nought to nine.</param>
/// <param name="X0">The quad's first corner across.</param>
/// <param name="Y0">The quad's first corner down.</param>
/// <param name="X1">The quad's opposite corner across, which may lie left of the first.</param>
/// <param name="Y1">The quad's opposite corner down, which may lie above the first.</param>
/// <param name="Colour">Its colour, the item's own times the flare's intensity.</param>
public readonly record struct Igi2LensflareQuad(int Sprite, float X0, float Y0, float X1, float Y1, Vector3f Colour);

/// <summary>
/// A lens flare, I.G.I. 2's <c>Lensflare</c> (lensflaretask.c): sprites strung along the line from where the sun stands on the screen
/// through the screen's centre, fading in and out as the sun is seen or hidden.
/// </summary>
/// <remarks>
/// <para>
/// <c>LensflareQTask_GetDirlightDirectionAndColor</c>: its direction and colour are its <c>Dirlight</c>'s, the one its id names, or its
/// parent without one, as the light stands.
/// </para>
/// <para>
/// <c>LensflareQTask_EnterHandler</c>, each frame, unless lens flares are off: the sun is put far along its direction from the camera and
/// projected; nearer than ten thousand, or off the display, it is not seen, otherwise as much of it is seen as a line of sight from the
/// camera towards it lets through (<c>Lensflaretask_CalcIntensity</c>). The intensity eases to that, four fifths kept a frame; from a
/// hundredth up its items are drawn, each at its position along the line to the sun's mirror through the display's centre, in whole
/// pixels.
/// </para>
/// </remarks>
public sealed partial class Igi2LensflareTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Lensflare";

    /// <summary>How far along its direction the sun is put, in world units.</summary>
    public const double SunDistance = 409600000.0;

    /// <summary>The nearest the projected sun may be and be seen.</summary>
    public const float NearestSeen = 10000f;

    /// <summary>The least intensity its items are drawn at.</summary>
    public const float LeastDrawn = 0.009999999776482582f;

    /// <summary>The level ids a <c>Dirlight QTask ID</c> may name.</summary>
    private const int LevelIds = 0x1000;

    /// <summary>Gets or sets its direction, the sun's as last read.</summary>
    [ScriptParam(ScriptParamType.Real32x3, "Direction")]
    public Vector3f Direction { get; set; }

    /// <summary>Gets or sets its colour, the sun's front colour as last read.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Color")]
    public Vector3f Colour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the level id of the <c>Dirlight</c> it follows, or minus one for its parent.</summary>
    [ScriptParam(ScriptParamType.Int32, "Dirlight QTask ID")]
    public int DirlightId { get; set; } = -1;

    /// <summary>Gets its intensity, eased frame by frame.</summary>
    public float Intensity { get; private set; }

    /// <summary>Gets or sets a value indicating whether lens flares are drawn, <c>LensflareQTask_IsLensflaresEnabled</c>.</summary>
    public static bool IsEnabled { get; set; } = true;

    /// <summary>Reads its <c>Dirlight</c>'s direction and colour, <c>LensflareQTask_GetDirlightDirectionAndColor</c>.</summary>
    public void FollowDirlight()
    {
        GameTask? light = DirlightId == -1 ? Parent : (uint)DirlightId < LevelIds ? Tree.FromId(DirlightId) : null;
        if (light is Igi2DirlightTask sun)
        {
            Direction = sun.Direction;
            Colour = sun.FrontColour;
        }
    }

    /// <summary>
    /// Takes a frame, <c>LensflareQTask_EnterHandler</c>: where the sun projects, whether it is seen, the intensity eased, and the
    /// items' quads.
    /// </summary>
    /// <param name="screen">Where the sun projects on the screen, in pixels.</param>
    /// <param name="depth">How far it projects.</param>
    /// <param name="displayMin">The display's top left corner.</param>
    /// <param name="displayMax">The display's bottom right corner.</param>
    /// <param name="seen">How much of the sun a line of sight towards it lets through, asked only when it is on the display.</param>
    /// <returns>The quads to draw, none when lens flares are off or it is too faint.</returns>
    public IReadOnlyList<Igi2LensflareQuad> Frame(
        System.Numerics.Vector2 screen,
        float depth,
        System.Numerics.Vector2 displayMin,
        System.Numerics.Vector2 displayMax,
        Func<float> seen)
    {
        ArgumentNullException.ThrowIfNull(seen);
        if (!IsEnabled)
        {
            return [];
        }

        FollowDirlight();
        if (!Children.Any())
        {
            return [];
        }

        float visible = depth < NearestSeen
            || screen.X < displayMin.X || screen.X >= displayMax.X
            || screen.Y < displayMin.Y || screen.Y >= displayMax.Y
            ? 0f
            : seen();
        Fpu centreX = ((Fpu)displayMax.X + displayMin.X) * 0.5, centreY = ((Fpu)displayMax.Y + displayMin.Y) * 0.5;
        float mirrorX = (centreX - screen.X + centreX).F, mirrorY = (centreY - screen.Y + centreY).F;
        Intensity = (((Fpu)Intensity * 0.800000011920929) + ((Fpu)visible * 0.20000000298023224)).F;
        if (Intensity < LeastDrawn)
        {
            return [];
        }

        List<Igi2LensflareQuad> quads = [];
        foreach (Igi2LensflareItemTask item in Children.OfType<Igi2LensflareItemTask>())
        {
            float x = Fpu.ToInt32((((Fpu)mirrorX - screen.X) * item.Position) + screen.X);
            float y = Fpu.ToInt32((((Fpu)mirrorY - screen.Y) * item.Position) + screen.Y);
            item.Draw(Intensity, x, y, quads);
        }

        return quads;
    }
}

/// <summary>
/// One sprite of a lens flare, I.G.I. 2's <c>LensflareItem</c> (lensflaretask.c): which of the ten <c>lens0N.tex</c>, its colour and
/// size, and how far along the flare's line it sits, nought at the sun and one at its mirror.
/// </summary>
/// <remarks>
/// <c>Lensflaretask_DrawLensflareItem</c>: a quad 0.99 of its size from its place right and down; mirrored, three more, left, up, and
/// both, making a sprite centred there.
/// </remarks>
public sealed partial class Igi2LensflareItemTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "LensflareItem";

    /// <summary>The sprites there are, <c>LOCAL:common/textures/lens00.tex</c> to <c>lens09.tex</c>.</summary>
    public const int Sprites = 10;

    /// <summary>The prefix of the sprites' files, which <c>Game</c> loads at start-up.</summary>
    public const string SpritePrefix = "LOCAL:common/textures/lens0";

    /// <summary>Gets or sets its colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Color")]
    public Vector3f Colour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets how far along the flare's line it sits.</summary>
    [ScriptParam(ScriptParamType.RangeReal32, "Position")]
    public float Position { get; set; }

    /// <summary>Gets or sets its size, in pixels.</summary>
    [ScriptParam(ScriptParamType.Real32, "Size")]
    public float Size { get; set; } = 64f;

    /// <summary>Gets or sets its sprite, held to nought to nine as the level loads.</summary>
    [ScriptParam(ScriptParamType.Int32, "SpriteIndex")]
    public int SpriteIndex { get; set; }

    /// <summary>Gets or sets a value indicating whether it is drawn mirrored about its place, four quads to a sprite.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Mirror?")]
    public bool Mirror { get; set; } = true;

    /// <summary>
    /// Gets its sprite, <see cref="SpriteIndex"/> held to nought to nine, <c>LensflareItemQTask_UpdateInternalDataHandler</c>.
    /// </summary>
    public int Sprite => Math.Clamp(SpriteIndex, 0, Sprites - 1);

    /// <summary>Gets a sprite's file.</summary>
    /// <param name="sprite">The sprite.</param>
    /// <returns>Its file.</returns>
    public static string SpriteFile(int sprite) => FormattableString.Invariant($"{SpritePrefix}{sprite}.tex");

    /// <summary>Adds its quads at a place, <c>Lensflaretask_DrawLensflareItem</c>.</summary>
    /// <param name="intensity">The flare's intensity.</param>
    /// <param name="x">Its place across, in whole pixels.</param>
    /// <param name="y">Its place down, in whole pixels.</param>
    /// <param name="into">Where the quads go.</param>
    public void Draw(float intensity, float x, float y, List<Igi2LensflareQuad> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        float size = ((Fpu)Size * 0.9900000095367432).F;
        Vector3f colour = new(((Fpu)intensity * Colour.X).F, ((Fpu)intensity * Colour.Y).F, ((Fpu)intensity * Colour.Z).F);
        into.Add(new Igi2LensflareQuad(Sprite, x, y, ((Fpu)size + x).F, ((Fpu)size + y).F, colour));
        if (!Mirror)
        {
            return;
        }

        into.Add(new Igi2LensflareQuad(Sprite, x, y, ((Fpu)x - size).F, ((Fpu)size + y).F, colour));
        into.Add(new Igi2LensflareQuad(Sprite, x, y, ((Fpu)size + x).F, ((Fpu)y - size).F, colour));
        into.Add(new Igi2LensflareQuad(Sprite, x, y, ((Fpu)x - size).F, ((Fpu)y - size).F, colour));
    }
}
