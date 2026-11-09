namespace OpenIGI.Presentation.Hud;

/// <summary>One action on the controls panel, and the keys that do it as the player has them bound.</summary>
/// <param name="Keys">The keys, each a cap of its own: <c>W</c>, <c>A</c>, <c>S</c>, <c>D</c>; or one cap for a run, <c>1–9</c>.</param>
/// <param name="Action">What they do, as the panel says it: <c>Drop weapon</c>.</param>
public sealed record ControlsLine(IReadOnlyList<string> Keys, string Action);

/// <summary>A column of the controls panel: its heading and its actions.</summary>
/// <param name="Title">The heading, as <c>MATCH</c>.</param>
/// <param name="Lines">The actions, in the order they are read.</param>
public sealed record ControlsSection(string Title, IReadOnlyList<ControlsLine> Lines);

/// <summary>
/// The controls panel a multiplayer match shows while its key is held: what every key does, as the player has the keys bound,
/// so nobody has to know a shooter's keys to find the shop, the drop or the team menu.
/// </summary>
/// <param name="Sections">The columns.</param>
/// <param name="HoldKey">The key held to keep it up, named on the screen's hint and the panel's foot.</param>
/// <param name="Mode">The match's mode, as its title: <c>TEAM DEATHMATCH</c>; empty for none.</param>
public sealed record ControlsHelp(IReadOnlyList<ControlsSection> Sections, string HoldKey, string Mode);
