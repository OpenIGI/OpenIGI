using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Presentation;

/// <summary>A line of chat as the HUD shows it.</summary>
/// <param name="Name">Who said it; empty for the server's own word.</param>
/// <param name="Text">What was said.</param>
/// <param name="Opacity">How much of it is left to see, fading at the end of its time; whole while the chat is open.</param>
/// <param name="Kind">Who it was said to.</param>
/// <param name="Enemy">Whether the speaker is on another side than this player's, for its name's colour.</param>
/// <param name="Dead">Whether the speaker was dead when it said it.</param>
public readonly record struct HudChatLine(string Name, string Text, float Opacity, ChatKind Kind = ChatKind.All, bool Enemy = false, bool Dead = false);

/// <summary>The line being typed, as the HUD shows it.</summary>
/// <param name="Text">What is typed so far.</param>
/// <param name="Caret">Where the caret stands in it, in characters.</param>
/// <param name="Team">Whether it goes to the team only.</param>
/// <param name="Scroll">How many lines of the history are scrolled back from the newest.</param>
/// <param name="CaretOn">Whether the blinking caret is lit this frame.</param>
/// <param name="Touch">Whether it is typed on a touch screen's keyboard, which shows no key hints.</param>
/// <param name="KeyboardInset">How much of the screen's bottom a touch screen's keyboard covers, in pixels.</param>
public sealed record HudChatEntry(string Text, int Caret, bool Team, int Scroll, bool CaretOn, bool Touch, float KeyboardInset);

/// <summary>A place a co-op player marked, as the HUD shows it.</summary>
/// <param name="AnchorMetres">Where, in metres.</param>
/// <param name="Name">Who marked it.</param>
/// <param name="Opacity">How much of it is left to see.</param>
public readonly record struct HudMark(Vector3d AnchorMetres, string Name, float Opacity);
