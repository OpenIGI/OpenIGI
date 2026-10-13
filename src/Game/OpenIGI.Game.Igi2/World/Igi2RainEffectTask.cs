using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A level's rain or snow, I.G.I. 2's <c>RainEffect</c> (raineffect.c): falling drops (<c>drop.tga</c>) or flakes (<c>snow.tex</c>),
/// traced against the world from a height above the view to one below, of a colour and an alpha, as many layers as its density, while its
/// active expression holds.
/// </summary>
/// <remarks>
/// <para>
/// <c>RainEffect_UpdateInternalDataHandler</c>, outside the main menu: the weather is made of its kind, its traces' top and bottom in
/// metres, its alpha and colour, its density held to between one and thirty-two; changing its kind remakes it.
/// </para>
/// <para>
/// <c>RainEffect_RunHandler</c>: its active expression, chopped to a byte, where it has one, says whether it falls (it falls without
/// one), and its particles move on (<c>Rain_UpdateParticles</c>). The renderer draws it from these parameters.
/// </para>
/// </remarks>
public sealed partial class Igi2RainEffectTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "RainEffect";

    /// <summary>The texture rain falls as, <c>RainEffect_ptRainTexture</c>.</summary>
    public const string RainTexture = "drop.tga";

    /// <summary>The texture snow falls as, <c>RainEffect_ptSnowTexture</c>.</summary>
    public const string SnowTexture = "snow.tex";

    /// <summary>The most layers it falls in.</summary>
    public const int MaximumDensity = 32;

    private Igi2LevelExpression? _active;

    /// <summary>Gets or sets a value indicating whether it is rain, not snow.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Is Rain")]
    public bool IsRain { get; set; } = true;

    /// <summary>Gets or sets how far above the view its traces start, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Traceline start")]
    public float TracelineStart { get; set; } = 50f;

    /// <summary>Gets or sets how far below the view its traces end, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Traceline end")]
    public float TracelineEnd { get; set; } = 20f;

    /// <summary>Gets or sets its colour.</summary>
    [ScriptParam(ScriptParamType.Rgb, "Rain Colour")]
    public Vector3f Colour { get; set; } = new(1f, 1f, 1f);

    /// <summary>Gets or sets its alpha.</summary>
    [ScriptParam(ScriptParamType.Real32, "Rain Alpha")]
    public float Alpha { get; set; } = 0.3f;

    /// <summary>Gets or sets how many layers it falls in.</summary>
    [ScriptParam(ScriptParamType.Int32, "Density")]
    public int Density { get; set; } = 1;

    /// <summary>Gets or sets the expression that says whether it falls.</summary>
    [ScriptParam(ScriptParamType.VarString, "Is Active")]
    public string ActiveExpression { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether it falls, as its active expression last said.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>Gets the layers it falls in, its density held to between one and <see cref="MaximumDensity"/>.</summary>
    public int Layers => Math.Clamp(Density, 1, MaximumDensity);

    /// <summary>Gets the texture it falls as.</summary>
    public string Texture => IsRain ? RainTexture : SnowTexture;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _active ??= new Igi2LevelExpression(ActiveExpression, Name, level.Natives);
        IsActive = _active.IsEmpty || (byte)Igi2LevelExpression.Truncate(level.Evaluate(_active)) != 0;
    }
}
