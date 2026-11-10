using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.World;

/// <content>
/// Where mods take part in the tick: what they run before the player's update and at the tick's end, and the prompts they
/// offer. With no mod adding to <see cref="MissionWorld.BeforePlayerUpdate"/>, <see cref="MissionWorld.PromptOffers"/> or
/// <see cref="MissionWorld.TickEnded"/>, none of this changes anything. <b>Not retail.</b>
/// </content>
public sealed partial class Igi1MissionTick
{
    private const string ModLogCategory = "Mods";

    // Each is the mod's own: one that throws is logged, and the tick and the others go on. By index, so one may add or remove one.
    private void RunModsBeforePlayer(PlayerCamera player)
    {
        for (int i = 0; i < mission.BeforePlayerUpdate.Count; i++)
        {
            Contained.Run(host.Logger, ModLogCategory, "A mod's before-player update", mission.BeforePlayerUpdate[i], static run => run());
        }

        OfferModPrompt(player);
    }

    private void RunModsAtTickEnd()
    {
        for (int i = 0; i < mission.TickEnded.Count; i++)
        {
            Contained.Run(host.Logger, ModLogCategory, "A mod's tick end", mission.TickEnded[i], static run => run());
        }
    }

    // The first prompt a mod offers, where the game shows none of its own; the use key going down acts on it before the
    // player's update reads the same key, which then finds nothing of the game's to use.
    private void OfferModPrompt(PlayerCamera player)
    {
        if (FindModPrompt(mission, player, host.Logger) is { } offer)
        {
            ActOnModPrompt(offer, player, PlayerInputSampler.SampleFrame(host.ViewInput, player.InputProfile), host.Logger);
        }
    }

    /// <summary>Offers the first prompt a mod has for the player, where the game has none of its own, as <see cref="MissionWorld.OfferedPrompt"/>.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="player">The player.</param>
    /// <param name="logger">Where a prompt source that throws is reported.</param>
    /// <returns>The prompt offered, or <see langword="null"/> for none.</returns>
    internal static PromptOffer? FindModPrompt(MissionWorld mission, PlayerCamera player, IOpenIgiLogger logger)
    {
        if (mission.PromptOffers.Count == 0
            || player is not { IsOnLadder: false, IsInWorldInteraction: false, ControlTaken: false }
            || player.InteractionPrompt(1f) is not null)
        {
            return null;
        }

        for (int i = 0; i < mission.PromptOffers.Count; i++)
        {
            if (Contained.Run(logger, ModLogCategory, "A mod's prompt source", mission.PromptOffers[i], static source => source(), out PromptOffer? offer)
                && offer is not null)
            {
                mission.OfferedPrompt = offer;
                return offer;
            }
        }

        return null;
    }

    /// <summary>Acts on a mod's prompt when the use key goes down in <paramref name="frame"/>, against the frame the player's last update ran on.</summary>
    /// <param name="offer">The prompt offered.</param>
    /// <param name="player">The player, whose last frame the key is held against: the edge his own update is about to see.</param>
    /// <param name="frame">This tick's controls.</param>
    /// <param name="logger">Where a prompt that throws is reported.</param>
    internal static void ActOnModPrompt(PromptOffer offer, PlayerCamera player, in PlayerInputFrame frame, IOpenIgiLogger logger)
    {
        if (frame.IsSet(PlayerInputSlot.Activate) && !player.LastFrame.IsSet(PlayerInputSlot.Activate))
        {
            Contained.Run(logger, ModLogCategory, "A mod's prompt", offer, static taken => taken.Activate());
        }
    }
}
