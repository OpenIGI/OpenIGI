using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A stop on a lift's way, I.G.I. 2's <c>Floor</c>: where the lift stands and which way it faces there. An editor object; it is
/// neither drawn nor struck.
/// </summary>
/// <remarks>
/// <c>Floor_UpdateInternalDataHandler</c> (<c>igi2 0x46E940</c>) has the lift it belongs to read its floors again, which is how a
/// lift, whose own data is read before its children are made, comes to stand at its first floor.
/// </remarks>
public sealed partial class Igi2FloorTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "Floor";

    /// <summary>Gets or sets where the lift stands at this floor.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way the lift faces at this floor, as level data's three angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets which way the lift faces at this floor, row by row.</summary>
    public float[] Frame { get; private set; } = EngineEuler.Rotation(0f, 0f, 0f);

    /// <inheritdoc/>
    protected override void OnParsed()
    {
        Frame = EngineEuler.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
        for (GameTask? up = Parent; up is not null; up = up.Parent)
        {
            if (up is Igi2LiftTask lift)
            {
                lift.UpdateInternalData();
                return;
            }
        }
    }
}
