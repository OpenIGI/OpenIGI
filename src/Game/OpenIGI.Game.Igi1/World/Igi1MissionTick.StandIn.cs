using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.World;

/// <content>The mission's world stepped on a tick another game's player stands in for the mission's own.</content>
public sealed partial class Igi1MissionTick
{
    /// <summary>
    /// What the mission's own player's tick would have stepped of the world, while another game's player stands in for it: the
    /// effects -- a shot camera's fireball, a barrel's -- the barrels' fuses, the alarm speakers and the security cameras'
    /// beeps and whirs, and the level's ambience where the stand-in listens from.
    /// </summary>
    /// <param name="standIn">The player standing in.</param>
    /// <remarks>
    /// <see cref="ResolvePlayerTick"/> steps these for the mission's own player among its own sounds and rounds; the stand-in's
    /// sounds, effects and rounds are its own game's, so only the world's half is stepped here, once a tick the stand-in ran.
    /// Without it a camera that has seen the stand-in glows red with no warning beep first, and anything that explodes stays
    /// on its first frame.
    /// </remarks>
    private void ResolveStandInTick(IHumanBodyState standIn)
    {
        if (!mission.PlayerTicked)
        {
            if (mission.IsPlayerDead)
            {
                host.StepWorldEffects();
            }

            return;
        }

        host.StepWorldEffects();
        blasts.TickDestructibles();
        audio.SoundAlarms();
        audio.TickAmbience(standIn.Eyes);
    }
}
