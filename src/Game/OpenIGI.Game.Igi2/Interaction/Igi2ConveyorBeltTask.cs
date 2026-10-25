using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A conveyor belt, I.G.I. 2's <c>ConveyorBelt</c>: a placed object whose faces of the <c>ConveyorBelt</c> material carry what
/// stands on them along its own X and Y.
/// </summary>
/// <remarks>
/// <para>
/// A human standing on it asks it each move what pushes it (<c>ConveyorBelt_PushObjectHandler</c>, <c>igi2 0x53A7D0</c>): where its
/// footing's material is the belt's, the push is the belt's speeds, metres a second made units a tick, turned by the belt's frame;
/// anywhere else on it nothing. Its speeds are set by level data and by <c>ConveyorBelt_SetSpeed</c> (<c>0x53A5F0</c>), each
/// held within twenty metres a second either way.
/// </para>
/// <para>
/// It runs as any placed object does, its children only (<c>ConveyorBelt_RunHandler</c>, <c>0x53A790</c>), and is still placed
/// by the world scene, which reads its parameters by name (<see cref="IIgi2PlacedTask"/>).
/// </para>
/// </remarks>
public sealed partial class Igi2ConveyorBeltTask : Igi2Task, IIgi2PlacedTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ConveyorBelt";

    /// <summary>The native a script sets a belt's speeds through, <c>ConveyorBelt_SetSpeed(id, x, y)</c>.</summary>
    public const string SetSpeedNative = "ConveyorBelt_SetSpeed";

    /// <summary>The name of the material a belt's moving faces are of.</summary>
    public const string MaterialName = "ConveyorBelt";

    /// <summary>The most a belt runs either way, in metres a second.</summary>
    public const double MaxSpeed = 20.0;

    // Metres a second made units a tick, 4096 / 30: the float 0x43088889.
    private const float UnitsPerTick = 136.53334f;

    private float[] _frame = EngineEuler.Rotation(0f, 0f, 0f);

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way it faces, as level data's three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the queue it is drawn in.</summary>
    [ScriptParam(ScriptParamType.Int32, "RenderGroup")]
    public int RenderGroup { get; set; }

    /// <summary>Gets or sets a value indicating whether shadows do not fall on it.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Protect from shadows")]
    public bool ProtectFromShadows { get; set; }

    /// <summary>Gets or sets a value indicating whether it is drawn whole through a portal.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Excluded from clipping against portals")]
    public bool ExcludedFromPortalClipping { get; set; }

    /// <summary>Gets or sets the speed it carries along its own X, in metres a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "X Speed")]
    public float XSpeed { get; set; }

    /// <summary>Gets or sets the speed it carries along its own Y, in metres a second.</summary>
    [ScriptParam(ScriptParamType.Real32, "Y Speed")]
    public float YSpeed { get; set; }

    /// <summary>Registers <see cref="SetSpeedNative"/>, which finds its belt by level id among the running level's tasks.</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="functions">The natives script text may call, whose level the belt is found in.</param>
    public static void Register(NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentNullException.ThrowIfNull(functions);
        functions.Register(natives, SetSpeedNative, call =>
        {
            if (call.Count >= 3)
            {
                int id = call.GetInt32(0);
                if (id is >= 0 and < TaskTree.IdCapacity && functions.Level?.FromId(id) is Igi2ConveyorBeltTask belt)
                {
                    belt.XSpeed = Limit(call, 1);
                    belt.YSpeed = Limit(call, 2);
                }
            }

            return QvmValue.Zero;
        }, 3);
    }

    /// <summary>
    /// What it pushes a human standing on it with this move, <c>ConveyorBelt_PushObjectHandler</c> (<c>igi2 0x53A7D0</c>).
    /// </summary>
    /// <param name="materialName">The name of the material the human's footing is of, or <see langword="null"/> for none.</param>
    /// <returns>The push, in units a tick; nothing where the footing is not the belt's material.</returns>
    public Vector3f Push(string? materialName)
    {
        if (!string.Equals(materialName, MaterialName, StringComparison.Ordinal))
        {
            return default;
        }

        float x = ((Fpu)XSpeed * UnitsPerTick).F;
        float y = ((Fpu)YSpeed * UnitsPerTick).F;
        float[] m = _frame;
        return new Vector3f(
            ((((Fpu)m[1] * y) + ((Fpu)m[0] * x)) + ((Fpu)m[2] * 0.0)).F,
            ((((Fpu)m[4] * y) + ((Fpu)m[3] * x)) + ((Fpu)m[5] * 0.0)).F,
            ((((Fpu)m[7] * y) + ((Fpu)m[6] * x)) + ((Fpu)m[8] * 0.0)).F);
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        level.RunChildren(this);
    }

    /// <inheritdoc/>
    public bool TryGetValue(string name, out object? value)
    {
        value = name switch
        {
            "Position" => Position,
            "Orientation" => Orientation,
            "Model" => Model,
            "RenderGroup" => RenderGroup,
            "Protect from shadows" => ProtectFromShadows,
            "Excluded from clipping against portals" => ExcludedFromPortalClipping,
            "X Speed" => XSpeed,
            "Y Speed" => YSpeed,
            _ => null,
        };
        return value is not null;
    }

    /// <inheritdoc/>
    protected override void OnParsed() => _frame = EngineEuler.Rotation(Orientation.X, Orientation.Y, Orientation.Z);

    // Below the least the least, above the most the most, else the value as given, each evaluated afresh as the original does.
    private static float Limit(Igi2ScriptCall call, int index)
    {
        if (call.GetReal(index) < -MaxSpeed)
        {
            return (float)-MaxSpeed;
        }

        return call.GetReal(index) > MaxSpeed ? (float)MaxSpeed : new Fpu(call.GetReal(index)).F;
    }
}
