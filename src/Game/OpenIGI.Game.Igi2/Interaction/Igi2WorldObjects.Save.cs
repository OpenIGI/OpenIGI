using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Interaction;

/// <content>Its save state, <c>EditRigidObj_ptSerializeParameterList</c>: a model object's, which is all it keeps.</content>
public sealed partial class Igi2EditRigidObjTask : ISavedState
{
    /// <inheritdoc/>
    /// <remarks>
    /// <c>EditRigidObj_SerializeHandler</c>: only one beneath a <c>Dynamic</c> task is written
    /// (<c>QTask_GetFirstParentDerivedOfType</c> with <c>Level_eDynamicQTaskType</c>); what stands under <c>Static</c> never moves.
    /// </remarks>
    public bool IsSaved
    {
        get
        {
            for (OpenIGI.Engine.Tasks.GameTask? up = Parent; up is not null; up = up.Parent)
            {
                if (up.Type.Name == OpenIGI.Engine.Tasks.EngineTaskTypes.Dynamic)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <inheritdoc/>
    public void WriteState(StateWriter writer) => Igi2ModelObjState.Write(writer, _position, _orientation);

    /// <inheritdoc/>
    /// <remarks>It is put where it stood, as a guide that rides it would put it.</remarks>
    public void ReadState(StateReader reader)
    {
        (OpenIGI.Core.Numerics.Vector3d position, float[] orientation, _) = Igi2ModelObjState.Read(reader);
        Ride(position, orientation, _movedTick);
    }
}

/// <content>Its save state, <c>MineField_ptSerializeParameterList</c>: the ticks before it may explode again.</content>
public sealed partial class Igi2MineFieldTask : ISavedState
{
    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(Pause);
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Pause = reader.Int();
    }
}
