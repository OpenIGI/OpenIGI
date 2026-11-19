namespace OpenIGI.Game.Player.Aim;

/// <summary>What modern aim does to the first-person rig in one drawn frame.</summary>
/// <param name="Profile">The aim profile of the weapon in hand, or <see langword="null"/> when it cannot be aimed.</param>
/// <param name="Amount">How far the sights' move is applied, nought (hip) to one, eased.</param>
/// <param name="Reference">The sights' move at the weapon's still stand pose (<see cref="SightAlignment.Reference"/>).</param>
/// <param name="Motion">The frame's motion laid over it (<see cref="ViewModelMotion"/>).</param>
public readonly record struct RigAim(AdsProfile? Profile, float Amount, RigOffset Reference, RigOffset Motion)
{
    /// <summary>Gets the original's rig: nothing moved.</summary>
    public static RigAim None { get; } = new(null, 0f, RigOffset.Identity, RigOffset.Identity);

    /// <summary>Gets a value indicating whether the rig is moved at all.</summary>
    public bool MovesRig => (Profile is not null && Amount > 0f) || Motion != RigOffset.Identity;
}
