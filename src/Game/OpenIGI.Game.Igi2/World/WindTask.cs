using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The level's wind — I.G.I. 2's <c>Wind</c>, which drives the vertex morph that makes foliage move.
/// </summary>
/// <remarks>
/// <para>
/// A single oscillator, not a field: one constant strength plus a main sine and a clamped gust sine,
/// blowing in one direction for the whole level. A <see cref="ForestTask"/> says how many of its model's
/// levels of detail respond to it, and <c>Phase shift between morph channels</c> is what stops every
/// tree in a stand swaying in unison.
/// </para>
/// <para>
/// 17 of the 24 shipped levels declare it, so wind is per level and optional. It is one of the seven
/// task types the sequel adds outright — I.G.I. 1 has no counterpart, because it has no foliage that
/// moves.
/// </para>
/// </remarks>
public sealed partial class WindTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Wind";

    /// <summary>Gets or sets how fast the steady component oscillates.</summary>
    [ScriptParam(ScriptParamType.Real32, "Main Frequency")]
    public float MainFrequency { get; set; }

    /// <summary>Gets or sets how fast the gust component oscillates.</summary>
    [ScriptParam(ScriptParamType.Real32, "Gust Frequency")]
    public float GustFrequency { get; set; }

    /// <summary>Gets or sets how far the steady component swings.</summary>
    [ScriptParam(ScriptParamType.Real32, "Main Amplitude")]
    public float MainAmplitude { get; set; }

    /// <summary>Gets or sets how far the gust component swings.</summary>
    [ScriptParam(ScriptParamType.Real32, "Gust Amplitude")]
    public float GustAmplitude { get; set; }

    /// <summary>Gets or sets the constant part of the wind, before either oscillator.</summary>
    [ScriptParam(ScriptParamType.Real32, "Constant Strength")]
    public float ConstantStrength { get; set; }

    /// <summary>Gets or sets the floor the gust is clamped to.</summary>
    [ScriptParam(ScriptParamType.Real32, "Gust Lower Clamp")]
    public float GustLowerClamp { get; set; }

    /// <summary>Gets or sets the ceiling the gust is clamped to.</summary>
    /// <remarks>
    /// Clamping a sine at both ends is what turns a swell into a gust: it flattens the peaks and troughs
    /// so the wind sits at one strength, rises quickly and sits again.
    /// </remarks>
    [ScriptParam(ScriptParamType.Real32, "Gust Upper Clamp")]
    public float GustUpperClamp { get; set; }

    /// <summary>Gets or sets how far apart the model's morph channels are driven.</summary>
    [ScriptParam(ScriptParamType.Real32, "Phase shift between morph channels")]
    public float MorphChannelPhaseShift { get; set; }

    /// <summary>Gets or sets which way the wind blows.</summary>
    [ScriptParam(ScriptParamType.Angle, "Wind direction")]
    public float Direction { get; set; }

    /// <summary>Gets or sets the editor's preview toggle.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Preview")]
    public bool Preview { get; set; }
}
