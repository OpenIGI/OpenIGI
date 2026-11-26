namespace OpenIGI.Game.Igi2.World;

/// <content>
/// What a save does to a figure: it keeps nothing of it (an <c>EditBoneObj</c> has no serialize list of its own), but once a save is
/// read, <c>EditBoneObj_DeSerializeDoneHandler</c> makes its clip controllers anew and plays its list again from its first clip.
/// </content>
public sealed partial class Igi2EditBoneObjTask : ISavedState
{
    /// <inheritdoc/>
    public bool IsSaved => false;

    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader)
    {
    }

    /// <inheritdoc/>
    public void LoadDone() => StartAnimation();
}
