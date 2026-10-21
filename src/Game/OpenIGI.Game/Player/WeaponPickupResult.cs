namespace OpenIGI.Game.Player;

/// <summary>What acquiring a world weapon changed in the player's inventory.</summary>
/// <param name="AddedWeapon">Whether the pickup created a new carried gun.</param>
/// <param name="AddedReserveRounds">The rounds added to an already-owned weapon's reserve.</param>
public readonly record struct WeaponPickupResult(bool AddedWeapon, int AddedReserveRounds);
