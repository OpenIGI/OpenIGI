namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A task that hears it is taken out of the world or put back, <c>DynCubeObj_eDynCubeObjRemoveQTaskEvent</c> and
/// <c>DynCubeObj_eDynCubeObjRegisterQTaskEvent</c>: as a conditional container whose condition fails takes its children out
/// (<c>ConditionalContainer_RemoveChildren</c>) and puts them back once it holds again (<c>ConditionalContainer_RegisterChildren</c>).
/// </summary>
public interface IIgi2WorldMember
{
    /// <summary>It is taken out of the world.</summary>
    void RemoveFromWorld();

    /// <summary>It is put back in the world; nothing for a type whose register handler does nothing of its own.</summary>
    void PutInWorld()
    {
    }
}
