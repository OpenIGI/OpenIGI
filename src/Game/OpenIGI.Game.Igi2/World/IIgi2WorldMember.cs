namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A task that hears it is taken out of the world, <c>DynCubeObj_eDynCubeObjRemoveQTaskEvent</c>: as a conditional container whose
/// condition fails takes its children out (<c>ConditionalContainer_RemoveChildren</c>), each is told, its own children first
/// through it.
/// </summary>
public interface IIgi2WorldMember
{
    /// <summary>It is taken out of the world.</summary>
    void RemoveFromWorld();
}
