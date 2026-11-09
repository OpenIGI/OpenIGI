namespace OpenIGI.Host;

/// <content>
/// A buy heard: each weapon or item the match hands the local player -- a buy, or a loadout taken -- plays the gun pickup's
/// sound (<c>weaponpickup_1</c>) in its ears, as a gun taken from the ground does; I.G.I. 2's buyer hears the same sound as
/// the shop's equipment is handed over.
/// </content>
public sealed partial class GameHost
{
    private const string BuySound = "weaponpickup_1";

    // How many the session had handed over when last looked, or none outside a session.
    private int? _buysHeard;

    // Once a frame.
    private void UpdateBuySounds()
    {
        if (TickObserver is not ISessionScoreboard board)
        {
            _buysHeard = null;
            return;
        }

        int handed = board.OwnHandedOver;
        if (_buysHeard is { } heard && handed > heard)
        {
            _audioOut.Sounds?.PlayDefinition(BuySound, priority: 180);
        }

        _buysHeard = handed;
    }
}
