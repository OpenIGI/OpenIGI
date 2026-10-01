using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// One wave train on a body of water — I.G.I. 2's <c>WaterLayer</c>, a child of a <see cref="WaterTask"/>.
/// </summary>
/// <remarks>
/// <para>
/// A directional sine: a frequency, an amplitude, a speed and a heading. The surface is the sum of its
/// water's layers, which is how a sheet gets a swell and a chop at once without either being modelled —
/// <c>WaterLayer_t</c> carries a precomputed <c>tRotation</c> beside the angle, so the heading is
/// resolved once and the per-vertex work is a dot product and a sine.
/// </para>
/// <para>
/// Four parameters, and none of them are editor state. This is one of the few I.G.I. 2 task types that
/// is nothing but level data.
/// </para>
/// </remarks>
public sealed partial class WaterLayerTask : GameTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "WaterLayer";

    /// <summary>Gets or sets how many wave crests fit into a unit of distance — <c>vFrequency</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Frequency")]
    public float Frequency { get; set; }

    /// <summary>Gets or sets how far the surface rises and falls — <c>vAmplitude</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Amplitude")]
    public float Amplitude { get; set; }

    /// <summary>Gets or sets how fast the train travels — <c>vSpeed</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Speed")]
    public float Speed { get; set; }

    /// <summary>Gets or sets which way it travels — <c>vAngle</c>.</summary>
    [ScriptParam(ScriptParamType.Real32, "Angle")]
    public float Angle { get; set; }
}
