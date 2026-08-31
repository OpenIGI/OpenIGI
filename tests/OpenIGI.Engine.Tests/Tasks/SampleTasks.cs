using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>Records the order lifecycle hooks fire in, and what the tree looks like when they do.</summary>
internal sealed class ProbeTask : GameTask
{
    public List<string>? Log { get; set; }

    public string Tag { get; set; } = string.Empty;

    public GameTask? ParentAtCreate { get; private set; }

    public GameTask? ParentAtDestroy { get; private set; }

    protected override void OnCreate() => ParentAtCreate = Parent;

    protected override void OnPreDestroy() => Log?.Add($"pre:{Tag}");

    protected override void OnDestroy()
    {
        ParentAtDestroy = Parent;
        Log?.Add($"dtor:{Tag}");
    }
}

/// <summary>Logs every visit and can mutate the tree from inside its own update.</summary>
internal sealed class WalkerTask : GameTask
{
    public string Tag { get; set; } = string.Empty;

    public List<string>? Visited { get; set; }

    public List<string>? Rendered { get; set; }

    public Action? OnUpdated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this task passes the tick on to its children.
    /// </summary>
    /// <remarks>
    /// Forwarding is opt-in per task type — the tick reaches the root's children and no further on its
    /// own — so a walker that stops forwarding freezes its whole subtree, exactly as <c>Static</c> does.
    /// </remarks>
    public bool ForwardsToChildren { get; set; } = true;

    protected override void OnUpdate()
    {
        Visited?.Add(Tag);
        OnUpdated?.Invoke();

        if (ForwardsToChildren)
        {
            ForwardToChildren(TaskMessage.Update);
        }
    }

    protected override void OnRender()
    {
        Rendered?.Add(Tag);

        if (ForwardsToChildren)
        {
            ForwardToChildren(TaskMessage.Render);
        }
    }
}

/// <summary>A behaviourless grouping task, as retail level data's <c>Container</c> is.</summary>
internal sealed class LifecycleProbeTask : GameTask
{
    public int ParsedCount { get; private set; }

    public int PostLoadCount { get; private set; }

    public int ChildCountAtParsed { get; private set; } = -1;

    protected override void OnParsed()
    {
        ParsedCount++;
        ChildCountAtParsed = ChildCount;
    }

    protected override void OnPostLoad() => PostLoadCount++;
}

/// <summary>
/// The retail <c>HeightMap</c> type, declared exactly as <c>location0/level1</c>'s
/// <c>Task_DeclareParameters</c> block spells it.
/// </summary>
/// <remarks>
/// <code>
/// Task_DeclareParameters("HeightMap", "Position","ObjectPos", "Level","Int32", "Size","Int32",
///                        "isEdit","bool8", "Bitmap ID","Int32", "NORMALSMOOTH","Int32", "BLURSMOOTH","Int32");
/// </code>
/// Nine script arguments, because <c>ObjectPos</c> consumes three.
/// </remarks>
internal sealed partial class HeightMapTask : GameTask
{
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position;

    [ScriptParam(ScriptParamType.Int32, "Level")]
    public int Level;

    [ScriptParam(ScriptParamType.Int32, "Size")]
    public int Size;

    [ScriptParam(ScriptParamType.Bool8, "isEdit")]
    public bool IsEdit;

    [ScriptParam(ScriptParamType.Int32, "Bitmap ID")]
    public int BitmapId;

    [ScriptParam(ScriptParamType.Int32, "NORMALSMOOTH")]
    public int NormalSmooth;

    [ScriptParam(ScriptParamType.Int32, "BLURSMOOTH")]
    public int BlurSmooth;
}

/// <summary>Exports a latched boolean and its <c>Last</c> shadow, as retail <c>Switch</c> does.</summary>
internal sealed class SwitchTask : GameTask
{
    public ScriptFlag Pressed { get; } = new();

    protected override void OnExportVars(ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        variables.BindFlag(this, "isPressed", Pressed);
    }

    protected override void OnUpdate() => Pressed.CommitTick();
}

/// <summary>Covers every value type the binder supports, to pin the wire arity of each.</summary>
internal sealed partial class EveryTypeTask : GameTask
{
    [ScriptParam(ScriptParamType.Int32)]
    public int Integer;

    [ScriptParam(ScriptParamType.Int16)]
    public short Small;

    [ScriptParam(ScriptParamType.Bool8)]
    public bool Flag;

    [ScriptParam(ScriptParamType.Real32)]
    public float Scalar;

    [ScriptParam(ScriptParamType.Real64)]
    public double Wide;

    [ScriptParam(ScriptParamType.Rgb)]
    public Vector3f Colour;

    [ScriptParam(ScriptParamType.ObjectPos)]
    public Vector3d Position;

    [ScriptParam(ScriptParamType.Real32x9)]
    public Vector3f Orientation;

    [ScriptParam(ScriptParamType.Quat32)]
    public Quaternion32 Rotation;

    [ScriptParam(ScriptParamType.String16)]
    public string Model = string.Empty;

    // Graph stores three positional values in script arguments.
    [ScriptParam(ScriptParamType.Graph)]
    public float[] Curve = [];

    [ScriptParam(ScriptParamType.GraphEnvelope)]
    public float[]? Envelope;

    [ScriptParam(ScriptParamType.Int32)]
    public int Trailer;
}

/// <summary>Exercises source-generated binding for the authored Graph wire payload.</summary>
internal sealed partial class GraphValueTask : GameTask
{
    [ScriptParam(ScriptParamType.Graph)]
    public float[] Curve = [];
}
