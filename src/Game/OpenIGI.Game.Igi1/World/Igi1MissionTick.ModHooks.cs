using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.World;

/// <content>
/// Where mods take part in the tick: what they run before the player's update and at the tick's end, and the prompts they
/// offer. With no mod adding to <see cref="MissionWorld.BeforePlayerUpdate"/>, <see cref="MissionWorld.PromptOffers"/> or
/// <see cref="MissionWorld.TickEnded"/>, none of this changes anything: it only follows the use key. <b>Not retail.</b>
/// </content>
public sealed partial class Igi1MissionTick
{
    // Whether the use key was down on the last tick a mod's prompt was looked for: a prompt acts on the key going down.
    private bool _promptUseWasDown;

    private void RunModsBeforePlayer(PlayerCamera player)
    {
        foreach (Action before in mission.BeforePlayerUpdate)
        {
            before();
        }

        OfferModPrompt(player);
    }

    private void RunModsAtTickEnd()
    {
        foreach (Action ended in mission.TickEnded)
        {
            ended();
        }
    }

    // The first prompt a mod offers, where the game shows none of its own; the use key going down acts on it before the
    // player's update reads the same key, which then finds nothing of the game's to use.
    private void OfferModPrompt(PlayerCamera player)
    {
        mission.OfferedPrompt = null;

        // The key is followed every tick, offers or none, so a key already held as a prompt appears doesn't act on it.
        bool down = PlayerInputSampler.SampleFrame(host.ViewInput, player.InputProfile)[(int)PlayerInputSlot.Activate] != 0f;
        bool pressed = down && !_promptUseWasDown;
        _promptUseWasDown = down;
        if (mission.PromptOffers.Count == 0)
        {
            return;
        }

        if (player is not { IsOnLadder: false, IsInWorldInteraction: false, ControlTaken: false } || player.InteractionPrompt(1f) is not null)
        {
            return;
        }

        foreach (Func<PromptOffer?> source in mission.PromptOffers)
        {
            if (source() is { } offer)
            {
                mission.OfferedPrompt = offer;
                if (pressed)
                {
                    offer.Activate();
                }

                return;
            }
        }
    }
}
