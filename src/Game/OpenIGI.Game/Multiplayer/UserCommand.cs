namespace OpenIGI.Game.Multiplayer;

/// <summary>One tick of a player's input as it travels to the server: the tick it is for and the title's channel frame.</summary>
/// <remarks>
/// <para>
/// Both originals already reduce a tick of input to a frame of float channels before anything acts on it: I.G.I.'s
/// 40-slot <c>HumanPlayerInput</c> frame and I.G.I. 2's human channels. The frame is therefore the whole of what the
/// server needs to run a client's player exactly as the client ran it, and it is what is sent: Source's user command,
/// with the title's own channels in place of Source's buttons and angles.
/// </para>
/// <para>
/// The client keeps the commands it sent, so it can run them again on top of a correction from the server; the server
/// runs each once, on its tick.
/// </para>
/// </remarks>
/// <param name="Tick">The server tick it is to run on.</param>
/// <param name="Channels">The channel frame, the title's length; owned by the command.</param>
/// <param name="ViewTick">
/// The server tick the client was drawing the others at when it made the command, or <see cref="long.MinValue"/> when not
/// known: what a shot in it was aimed at, Source's <c>tick_count</c> less the client's interpolation.
/// </param>
public readonly record struct UserCommand(long Tick, float[] Channels, long ViewTick = long.MinValue);
