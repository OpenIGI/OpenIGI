namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// The badge I.G.I. 2's action menu shows for a thing to work (<c>ActionMenuScreen_DrawActionItemHandler</c> and each type's own):
/// a sprite of its status archive, by name. Whoever works the thing -- the level's own player or another game's standing in -- is
/// shown the same one.
/// </summary>
public static class Igi2ActionBadge
{
    /// <summary>The badge a locked door that can be picked shows.</summary>
    public const string PickLock = "action_picklock.spr";

    /// <summary>The badge a ladder shows.</summary>
    public const string Ladder = "a_ladder.spr";

    /// <summary>The badge for a thing.</summary>
    /// <param name="target">The thing.</param>
    /// <returns>The status archive sprite's name, or <see langword="null"/> where the thing has no badge.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static string? Of(IIgi2Activatable target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target switch
        {
            IIgi2Door { IsLocked: true, IsPickable: true } => PickLock,
            Igi2GenericTbaTask { Sprite.Length: > 0 } action => action.Sprite,
            { Kind: Igi2ActivateKind.Door } => "a_door.spr",
            { Kind: Igi2ActivateKind.Cabinet } => "action_searchcab.spr",
            { Kind: Igi2ActivateKind.Terminal } => "action_comput.spr",
            { Kind: Igi2ActivateKind.Switch } => "action_pressbutton.spr",
            { Kind: Igi2ActivateKind.GenericTba } => "a_codepad.spr",
            { Kind: Igi2ActivateKind.Ladder } => Ladder,
            { Kind: Igi2ActivateKind.Wire } => "a_wire.spr",
            { Kind: Igi2ActivateKind.Fence } => "a_fence.spr",
            { Kind: Igi2ActivateKind.StationaryGun } => "a_gun.spr",
            { Kind: Igi2ActivateKind.SilentKill } => "a_silent.spr",
            _ => null,
        };
    }
}
