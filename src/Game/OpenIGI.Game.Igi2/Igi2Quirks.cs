namespace OpenIGI.Game.Igi2;

/// <summary>
/// The special cases I.G.I. 2 decides by mission number, in one place a mod can change: the title publishes one per session,
/// and a mod's content module, which registers after the title's, edits it.
/// </summary>
public sealed class Igi2Quirks
{
    /// <summary>Gets or sets which missions show the map computer in its broken casing.</summary>
    /// <remarks>The original sets <c>ComputerMenu_isBroken</c> for mission 17 alone.</remarks>
    public Func<int, bool> HasBrokenComputer { get; set; } = static mission => mission == 17;
}
