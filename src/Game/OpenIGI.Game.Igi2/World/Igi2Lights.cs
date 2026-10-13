using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The sun or moon, I.G.I. 2's <c>Dirlight</c> (dirlighttask.c): a directional light whose direction and front and back colours are
/// keyed through the day by its <c>DirlightKeyframe</c> children, drawn as a sprite of its texture where it stands in the sky.
/// </summary>
/// <remarks>
/// <para>
/// <c>DirlightQTask_UpdateInternalDataHandler</c> spans its keyframes' times; each frame the viewport's time of day sets it
/// (<c>DirlightQTask_DrawCallback</c>, <c>DirlightQTask_SetTime</c>), and a time it is not already at updates it.
/// </para>
/// <para>
/// <c>DirlightQTask_Update</c>: without keyframes it is dark, pointing straight up. With every keyframe at one time it is the first's.
/// Otherwise a keyframe at the time is it; else the latest before and the earliest after the time bracket it, and it is the one before
/// when there is none after, or the two are at one time, or the one after when there is none before. Between two it is interpolated:
/// its colours straight, its direction turned from the earlier's about the axis across the two by the angle between them, scaled by how
/// far between them the time is.
/// </para>
/// </remarks>
public sealed partial class Igi2DirlightTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Dirlight";

    private float _minTime = float.MaxValue;
    private float _maxTime = -float.MaxValue;
    private float _time = float.NaN;

    /// <summary>Gets or sets a value indicating whether it lights the terrain.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Affects terrain")]
    public bool AffectsTerrain { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether it lights objects.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Affects objects")]
    public bool AffectsObjects { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether it casts stencil shadows, where the display can.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Hard shadows")]
    public bool HardShadows { get; set; } = true;

    /// <summary>Gets or sets its radiosity intensity, for the editor's lighting bake.</summary>
    [ScriptParam(ScriptParamType.Real32, "Radiosity intensity")]
    public float RadiosityIntensity { get; set; }

    /// <summary>Gets or sets how dark its stencil shadows are.</summary>
    [ScriptParam(ScriptParamType.Real32, "Shadow intensity")]
    public float ShadowIntensity { get; set; } = 0.25f;

    /// <summary>Gets or sets its sprite's texture, under <c>LOCAL:common/textures/</c>.</summary>
    [ScriptParam(ScriptParamType.String32, "Sun/Moon Texture")]
    public string Texture { get; set; } = "sun.tex";

    /// <summary>Gets or sets its sprite's size, in pixels.</summary>
    [ScriptParam(ScriptParamType.Real32, "Sun/Moon Size")]
    public float Size { get; set; } = 32f;

    /// <summary>Gets or sets a value indicating whether the sprite takes the light's front colour.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Sun/Moon Colored")]
    public bool IsColoured { get; set; }

    /// <summary>Gets its front colour at its time, <c>tFrontColor</c>.</summary>
    public Vector3f FrontColour { get; private set; }

    /// <summary>Gets its back colour at its time, <c>tBackColor</c>.</summary>
    public Vector3f BackColour { get; private set; }

    /// <summary>Gets the unit vector towards it at its time, <c>tDirection</c>.</summary>
    public Vector3f Direction { get; private set; } = new(0f, 0f, 1f);

    /// <summary>Gets the time of day it was last set to, <c>vCurrentTime</c>.</summary>
    public float Time => _time;

    /// <summary>Spans its keyframes' times and sets it to the start of the day, <c>DirlightQTask_UpdateInternalDataHandler</c>.</summary>
    public void Prepare()
    {
        _minTime = float.MaxValue;
        _maxTime = -float.MaxValue;
        foreach (Igi2DirlightKeyframeTask keyframe in Keyframes)
        {
            keyframe.Prepare();
            _maxTime = Math.Max(_maxTime, keyframe.KeyTime);
            _minTime = Math.Min(_minTime, keyframe.KeyTime);
        }

        _time = float.NaN;
        SetTime(0f);
    }

    /// <summary>Sets it to a time of day, <c>DirlightQTask_SetTime</c>: a time it is not at updates it.</summary>
    /// <param name="time">The time, in hours.</param>
    public void SetTime(float time)
    {
        if (time != _time)
        {
            _time = time;
            Update();
        }
    }

    private IEnumerable<Igi2DirlightKeyframeTask> Keyframes => Children.OfType<Igi2DirlightKeyframeTask>();

    // DirlightQTask_Update.
    private void Update()
    {
        Igi2DirlightKeyframeTask? first = Keyframes.FirstOrDefault();
        if (first is null)
        {
            FrontColour = default;
            BackColour = default;
            Direction = new Vector3f(0f, 0f, 1f);
            return;
        }

        if (_maxTime == _minTime)
        {
            Take(first);
            return;
        }

        (Igi2DirlightKeyframeTask? before, Igi2DirlightKeyframeTask? after) = Igi2KeyframeSpan.Bracket(Keyframes, static k => k.KeyTime, _time);
        if (before is null)
        {
            Take(after!);
            return;
        }

        if (after is null || ReferenceEquals(before, after) || before.KeyTime == after.KeyTime)
        {
            Take(before);
            return;
        }

        Fpu t = ((Fpu)_time - before.KeyTime) / ((Fpu)after.KeyTime - before.KeyTime);
        FrontColour = Igi2KeyframeSpan.Lerp(before.FrontColour, after.FrontColour, t);
        BackColour = Igi2KeyframeSpan.Lerp(before.BackColour, after.BackColour, t);
        Vector3f from = before.Direction, to = after.Direction;
        Vector3f axis = new(
            (((Fpu)from.Y * to.Z) - ((Fpu)from.Z * to.Y)).F,
            (-(((Fpu)to.Z * from.X) - ((Fpu)to.X * from.Z))).F,
            (((Fpu)from.X * to.Y) - ((Fpu)from.Y * to.X)).F);
        float cosine = (((Fpu)to.Z * from.Z) + ((Fpu)to.X * from.X) + ((Fpu)from.Y * to.Y)).F;
        float angle = ((Fpu)Igi2Pose.Acos(cosine) * t).F;
        float[] m = Igi2KeyframeSpan.RotationAroundAxis(axis, angle);
        Direction = new Vector3f(
            (((Fpu)m[1] * from.Y) + ((Fpu)m[2] * from.Z) + ((Fpu)m[0] * from.X)).F,
            (((Fpu)m[4] * from.Y) + ((Fpu)m[5] * from.Z) + ((Fpu)m[3] * from.X)).F,
            (((Fpu)m[7] * from.Y) + ((Fpu)m[8] * from.Z) + ((Fpu)m[6] * from.X)).F);
    }

    private void Take(Igi2DirlightKeyframeTask keyframe)
    {
        FrontColour = keyframe.FrontColour;
        BackColour = keyframe.BackColour;
        Direction = keyframe.Direction;
    }
}

/// <summary>
/// One key of a <c>Dirlight</c>'s day, I.G.I. 2's <c>DirlightKeyframe</c> (dirlighttask.c): where the light stands, as an elevation and
/// a bearing, and its front and back colours, at an hour.
/// </summary>
/// <remarks>
/// <c>DirlightKeyframeQTask_UpdateInternalDataHandler</c>: its direction is <c>(1, 0, 0)</c> turned by
/// <c>Real32x9_Create(0, 2π − Beta, 2π − Gamma)</c> (see <see cref="Igi2FixedFunctionSun.DirectionFrom"/>), and its hour is kept as
/// the time it is keyed at.
/// </remarks>
public sealed partial class Igi2DirlightKeyframeTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "DirlightKeyframe";

    /// <summary>A full turn, as the original's single-precision constant.</summary>
    public const float FullTurn = 6.2831854820251465f;

    /// <summary>Gets or sets its elevation, in radians.</summary>
    [ScriptParam(ScriptParamType.Angle, "Beta")]
    public float Beta { get; set; }

    /// <summary>Gets or sets its bearing, in radians.</summary>
    [ScriptParam(ScriptParamType.Angle, "Gamma")]
    public float Gamma { get; set; }

    /// <summary>Gets or sets its front colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Front Color")]
    public Vector3f FrontColour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets its back colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Back Color")]
    public Vector3f BackColour { get; set; }

    /// <summary>Gets or sets its hour.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time")]
    public float Hour { get; set; }

    /// <summary>Gets the unit vector towards the light, as its two angles give it.</summary>
    public Vector3f Direction { get; private set; } = new(1f, 0f, 0f);

    /// <summary>Gets the time it is keyed at.</summary>
    public float KeyTime { get; private set; }

    /// <summary>Works out its direction and keys it at its hour.</summary>
    public void Prepare()
    {
        Fpu elevation = (Fpu)FullTurn - Beta;
        Fpu bearing = (Fpu)FullTurn - Gamma;
        Fpu cosElevation = Fpu.Cos(elevation.F);
        Direction = new Vector3f(
            (Fpu.Cos(bearing.F) * cosElevation).F,
            (Fpu.Sin(bearing.F) * cosElevation).F,
            (-Fpu.Sin(elevation.F)).F);
        KeyTime = Hour;
    }
}

/// <summary>One category's light, <c>GlobalLightCategory_t</c>: the terrain's, or an object category's.</summary>
/// <param name="Ambient">Its ambient colour.</param>
/// <param name="FogColour">Its fog's colour.</param>
/// <param name="FogDensity">Its fog's density, as authored.</param>
public readonly record struct Igi2LightCategory(Vector3f Ambient, Vector3f FogColour, float FogDensity)
{
    /// <summary>Interpolates two categories.</summary>
    /// <param name="from">The earlier.</param>
    /// <param name="to">The later.</param>
    /// <param name="t">How far from the earlier to the later.</param>
    /// <returns>The category between.</returns>
    public static Igi2LightCategory Lerp(Igi2LightCategory from, Igi2LightCategory to, Fpu t) => new(
        Igi2KeyframeSpan.Lerp(from.Ambient, to.Ambient, t),
        Igi2KeyframeSpan.Lerp(from.FogColour, to.FogColour, t),
        ((((Fpu)to.FogDensity - from.FogDensity) * t) + from.FogDensity).F);
}

/// <summary>
/// The level's ambient light, fog, sky and water colours, I.G.I. 2's <c>GlobalLight</c> (globallighttask.c), keyed through the day by
/// its <c>GlobalLightKeyframe</c> children: five categories, the terrain's and four of objects', each an ambient colour, a fog colour and
/// a fog density.
/// </summary>
/// <remarks>
/// <para>
/// <c>GlobalLightQTask_UpdateInternalDataHandler</c> spans its keyframes' times and sets it to the start of the day; each frame the
/// viewport's time of day sets it (<c>GlobalLightQTask_DrawCallback</c>), and the categories, fog densities times 0.0005, light the
/// terrain and the objects, and its sky colour the sky.
/// </para>
/// <para>
/// <c>GlobalLightQTask_Update</c> brackets the time as the <c>Dirlight</c> does, and between two keyframes interpolates every value
/// straight; without keyframes it is all black.
/// </para>
/// </remarks>
public sealed partial class Igi2GlobalLightTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "GlobalLight";

    /// <summary>The categories it keys: the terrain's, then four of objects'.</summary>
    public const int Categories = 5;

    /// <summary>What an authored fog density is multiplied by as it is handed to the renderer.</summary>
    public const float FogDensityScale = 0.0005000000237487257f;

    private readonly Igi2LightCategory[] _categories = new Igi2LightCategory[Categories];
    private float _minTime = float.MaxValue;
    private float _maxTime = -float.MaxValue;
    private float _time = float.NaN;

    /// <summary>Gets or sets its radiosity intensity, for the editor's lighting bake.</summary>
    [ScriptParam(ScriptParamType.Real32, "Radiosity intensity ")]
    public float RadiosityIntensity { get; set; } = 1000f;

    /// <summary>Gets or sets its radiosity gamma, for the bake.</summary>
    [ScriptParam(ScriptParamType.Real32, "Radiosity gamma")]
    public float RadiosityGamma { get; set; } = 0.7f;

    /// <summary>Gets or sets its radiosity fadeout, for the bake.</summary>
    [ScriptParam(ScriptParamType.Real32, "Radiosity fadeout")]
    public float RadiosityFadeout { get; set; } = 20f;

    /// <summary>Gets or sets how soft the bake takes the sun to be.</summary>
    [ScriptParam(ScriptParamType.Real32, "Radiosity dirlight softness")]
    public float RadiosityDirlightSoftness { get; set; } = 0.15f;

    /// <summary>Gets or sets a value indicating whether the bake shadows the terrain.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Radiosity terrain shadows")]
    public bool RadiosityTerrainShadows { get; set; }

    /// <summary>Gets or sets the ambient colour the textures are filtered with as they load.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Texture filter ambient colour")]
    public Vector3f TextureFilterAmbient { get; set; }

    /// <summary>Gets or sets the scale the textures are filtered with as they load.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Texture filter scale")]
    public Vector3f TextureFilterScale { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the gamma the textures are filtered with as they load.</summary>
    [ScriptParam(ScriptParamType.Real32, "Texture filter gamma")]
    public float TextureFilterGamma { get; set; } = 1f;

    /// <summary>Gets its categories at its time: the terrain's, then four of objects'.</summary>
    public IReadOnlyList<Igi2LightCategory> Category => _categories;

    /// <summary>Gets the sky's colour at its time.</summary>
    public Vector3f SkyColour { get; private set; }

    /// <summary>Gets the water's ambient colour at its time.</summary>
    public Vector3f WaterAmbient { get; private set; }

    /// <summary>Gets the water's colour at its time.</summary>
    public Vector3f WaterColour { get; private set; }

    /// <summary>Gets the time of day it was last set to, <c>vCurrentTime</c>.</summary>
    public float Time => _time;

    /// <summary>Spans its keyframes' times and sets it to the start of the day, <c>GlobalLightQTask_UpdateInternalDataHandler</c>.</summary>
    public void Prepare()
    {
        _minTime = float.MaxValue;
        _maxTime = -float.MaxValue;
        foreach (Igi2GlobalLightKeyframeTask keyframe in Keyframes)
        {
            keyframe.Prepare();
            _maxTime = Math.Max(_maxTime, keyframe.KeyTime);
            _minTime = Math.Min(_minTime, keyframe.KeyTime);
        }

        _time = float.NaN;
        SetTime(0f);
    }

    /// <summary>Sets it to a time of day, <c>GlobalLightQTask_SetTime</c>: a time it is not at updates it.</summary>
    /// <param name="time">The time, in hours.</param>
    public void SetTime(float time)
    {
        if (time != _time)
        {
            _time = time;
            Update();
        }
    }

    private IEnumerable<Igi2GlobalLightKeyframeTask> Keyframes => Children.OfType<Igi2GlobalLightKeyframeTask>();

    // GlobalLightQTask_Update.
    private void Update()
    {
        Igi2GlobalLightKeyframeTask? first = Keyframes.FirstOrDefault();
        if (first is null)
        {
            Array.Clear(_categories);
            SkyColour = default;
            WaterAmbient = default;
            WaterColour = default;
            return;
        }

        if (_maxTime == _minTime)
        {
            Take(first);
            return;
        }

        (Igi2GlobalLightKeyframeTask? before, Igi2GlobalLightKeyframeTask? after) = Igi2KeyframeSpan.Bracket(Keyframes, static k => k.KeyTime, _time);
        if (before is null)
        {
            Take(after!);
            return;
        }

        if (after is null || ReferenceEquals(before, after) || before.KeyTime == after.KeyTime)
        {
            Take(before);
            return;
        }

        Fpu t = ((Fpu)_time - before.KeyTime) / ((Fpu)after.KeyTime - before.KeyTime);
        for (int i = 0; i < Categories; i++)
        {
            _categories[i] = Igi2LightCategory.Lerp(before.Linked[i], after.Linked[i], t);
        }

        SkyColour = Igi2KeyframeSpan.Lerp(before.SkyColour, after.SkyColour, t);
        WaterAmbient = Igi2KeyframeSpan.Lerp(before.WaterAmbient, after.WaterAmbient, t);
        WaterColour = Igi2KeyframeSpan.Lerp(before.WaterColour, after.WaterColour, t);
    }

    private void Take(Igi2GlobalLightKeyframeTask keyframe)
    {
        for (int i = 0; i < Categories; i++)
        {
            _categories[i] = keyframe.Linked[i];
        }

        SkyColour = keyframe.SkyColour;
        WaterAmbient = keyframe.WaterAmbient;
        WaterColour = keyframe.WaterColour;
    }
}

/// <summary>
/// One key of a <c>GlobalLight</c>'s day, I.G.I. 2's <c>GlobalLightKeyframe</c> (globallighttask.c): five categories, the sky, the
/// water, at an hour; a category may take another's values, by its link.
/// </summary>
/// <remarks>
/// <c>GlobalLightKeyframeQTask_UpdateInternalDataHandler</c>: <c>Link All Sliders</c>, pressed, links each object category to the one
/// before it when the first is linked, and unlinks them all when it is not; then five times over, each category in turn linked to a
/// category (nought to four) takes its values, so that chains of links carry through.
/// </remarks>
public sealed partial class Igi2GlobalLightKeyframeTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "GlobalLightKeyframe";

    private readonly Igi2LightCategory[] _linked = new Igi2LightCategory[Igi2GlobalLightTask.Categories];

    /// <summary>Gets or sets a value indicating whether the editor's <c>Link All Sliders</c> was pressed.</summary>
    [ScriptParam(ScriptParamType.PushButton, "Link All Sliders")]
    public bool LinkAllSliders { get; set; }

    /// <summary>Gets or sets the terrain's ambient colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Ambient color terrain")]
    public Vector3f TerrainAmbient { get; set; }

    /// <summary>Gets or sets the terrain's fog colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Fog color terrain")]
    public Vector3f TerrainFogColour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the terrain's fog density.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fog density terrain")]
    public float TerrainFogDensity { get; set; }

    /// <summary>Gets or sets the category the terrain takes its values from, or minus one.</summary>
    [ScriptParam(ScriptParamType.Int32, "Link setting terrain")]
    public int TerrainLink { get; set; } = -1;

    /// <summary>Gets or sets the first object category's ambient colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Ambient color object category 1")]
    public Vector3f Category1Ambient { get; set; }

    /// <summary>Gets or sets the first object category's fog colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Fog color object category 1")]
    public Vector3f Category1FogColour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the first object category's fog density.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fog density object category 1")]
    public float Category1FogDensity { get; set; }

    /// <summary>Gets or sets the category the first object category takes its values from, or minus one.</summary>
    [ScriptParam(ScriptParamType.Int32, "Link setting object category 1")]
    public int Category1Link { get; set; } = -1;

    /// <summary>Gets or sets the second object category's ambient colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Ambient color object category 2")]
    public Vector3f Category2Ambient { get; set; }

    /// <summary>Gets or sets the second object category's fog colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Fog color object category 2")]
    public Vector3f Category2FogColour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the second object category's fog density.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fog density object category 2")]
    public float Category2FogDensity { get; set; }

    /// <summary>Gets or sets the category the second object category takes its values from, or minus one.</summary>
    [ScriptParam(ScriptParamType.Int32, "Link setting object category 2")]
    public int Category2Link { get; set; } = -1;

    /// <summary>Gets or sets the third object category's ambient colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Ambient color object category 3")]
    public Vector3f Category3Ambient { get; set; }

    /// <summary>Gets or sets the third object category's fog colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Fog color object category 3")]
    public Vector3f Category3FogColour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the third object category's fog density.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fog density object category 3")]
    public float Category3FogDensity { get; set; }

    /// <summary>Gets or sets the category the third object category takes its values from, or minus one.</summary>
    [ScriptParam(ScriptParamType.Int32, "Link setting object category 3")]
    public int Category3Link { get; set; } = -1;

    /// <summary>Gets or sets the fourth object category's ambient colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Ambient color object category 4")]
    public Vector3f Category4Ambient { get; set; }

    /// <summary>Gets or sets the fourth object category's fog colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Fog color object category 4")]
    public Vector3f Category4FogColour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the fourth object category's fog density.</summary>
    [ScriptParam(ScriptParamType.Real32, "Fog density object category 4")]
    public float Category4FogDensity { get; set; }

    /// <summary>Gets or sets the category the fourth object category takes its values from, or minus one.</summary>
    [ScriptParam(ScriptParamType.Int32, "Link setting object category 4")]
    public int Category4Link { get; set; } = -1;

    /// <summary>Gets or sets the sky's colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Sky color")]
    public Vector3f SkyColour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets the water's ambient colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Water ambient")]
    public Vector3f WaterAmbient { get; set; }

    /// <summary>Gets or sets the water's colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Water color")]
    public Vector3f WaterColour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets its hour.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time")]
    public float Hour { get; set; }

    /// <summary>Gets its categories, each as its link leaves it.</summary>
    public IReadOnlyList<Igi2LightCategory> Linked => _linked;

    /// <summary>Gets the time it is keyed at.</summary>
    public float KeyTime { get; private set; }

    /// <summary>Applies its links and keys it at its hour.</summary>
    public void Prepare()
    {
        if (LinkAllSliders)
        {
            if (Category1Link != 0)
            {
                (TerrainLink, Category1Link, Category2Link, Category3Link, Category4Link) = (-1, 0, 1, 2, 3);
            }
            else
            {
                (TerrainLink, Category1Link, Category2Link, Category3Link, Category4Link) = (-1, -1, -1, -1, -1);
            }

            LinkAllSliders = false;
        }

        KeyTime = Hour;
        _linked[0] = new Igi2LightCategory(TerrainAmbient, TerrainFogColour, TerrainFogDensity);
        _linked[1] = new Igi2LightCategory(Category1Ambient, Category1FogColour, Category1FogDensity);
        _linked[2] = new Igi2LightCategory(Category2Ambient, Category2FogColour, Category2FogDensity);
        _linked[3] = new Igi2LightCategory(Category3Ambient, Category3FogColour, Category3FogDensity);
        _linked[4] = new Igi2LightCategory(Category4Ambient, Category4FogColour, Category4FogDensity);
        int[] links = [TerrainLink, Category1Link, Category2Link, Category3Link, Category4Link];
        for (int pass = 0; pass < Igi2GlobalLightTask.Categories; pass++)
        {
            for (int i = 0; i < links.Length; i++)
            {
                if ((uint)links[i] < Igi2GlobalLightTask.Categories)
                {
                    _linked[i] = _linked[links[i]];
                }
            }
        }
    }
}

/// <summary>What a keyed light's update shares: bracketing a time between keyframes, and interpolating between two.</summary>
internal static class Igi2KeyframeSpan
{
    /// <summary>
    /// Finds the keyframes either side of a time: one at the time is both; otherwise the latest before it and the earliest after it,
    /// each the first found of its time.
    /// </summary>
    public static (T? Before, T? After) Bracket<T>(IEnumerable<T> keyframes, Func<T, float> timeOf, float time)
        where T : class
    {
        T? before = null, after = null;
        foreach (T keyframe in keyframes)
        {
            float at = timeOf(keyframe);
            if (at == time)
            {
                return (keyframe, keyframe);
            }

            if (at < time && (before is null || at > timeOf(before)))
            {
                before = keyframe;
            }

            if (at > time && (after is null || at < timeOf(after)))
            {
                after = keyframe;
            }
        }

        return (before, after);
    }

    /// <summary>Interpolates two colours straight.</summary>
    public static Vector3f Lerp(Vector3f from, Vector3f to, Fpu t) => new(
        ((((Fpu)to.X - from.X) * t) + from.X).F,
        ((((Fpu)to.Y - from.Y) * t) + from.Y).F,
        ((((Fpu)to.Z - from.Z) * t) + from.Z).F);

    /// <summary>
    /// The rotation by an angle about an axis taken as a unit, row by row, <c>Real32x9_CreateRotationAroundAxis</c>: built from the
    /// quaternion of half the angle.
    /// </summary>
    public static float[] RotationAroundAxis(Vector3f axis, float radians)
    {
        Fpu half = (Fpu)radians * 0.5;
        Fpu s = Fpu.Sin(half.F);
        Fpu x = s * axis.X, y = s * axis.Y, z = s * axis.Z;
        Fpu w = Fpu.Cos(half.F);
        Fpu xx = (x * x) + (x * x), xy = (y * x) + (y * x), xz = (z * x) + (z * x), xw = (x * w) + (x * w);
        Fpu yy = (y * y) + (y * y), yz = (z * y) + (z * y), yw = (y * w) + (y * w);
        Fpu zz = (z * z) + (z * z), zw = (w * z) + (w * z);
        Fpu oneXx = (Fpu)1.0 - xx;
        return
        [
            ((Fpu)1.0 - yy - zz).F, (xy - zw).F, (yw + xz).F,
            (zw + xy).F, (oneXx - zz).F, (yz - xw).F,
            (xz - yw).F, (yz + xw).F, (oneXx - yy).F,
        ];
    }
}
