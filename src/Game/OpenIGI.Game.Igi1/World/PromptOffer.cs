using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// A prompt a mod offers the player, through <see cref="MissionWorld.PromptOffers"/>: drawn as the game's own interaction
/// prompts are, and acted on when the use key goes down while it is the one shown. <b>Not retail.</b>
/// </summary>
/// <param name="Title">What the prompt says.</param>
/// <param name="AnchorUnits">Where in the world it is drawn, in engine units.</param>
/// <param name="Activate">What the use key does while it is shown, run on the tick the key goes down, before the player's update.</param>
public sealed record PromptOffer(string Title, Vector3d AnchorUnits, Action Activate);
