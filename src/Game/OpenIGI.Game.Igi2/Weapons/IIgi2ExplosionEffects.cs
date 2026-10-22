using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>What an explosion shows and sounds, behind which none of its logic depends.</summary>
public interface IIgi2ExplosionEffects
{
    /// <summary>The explosion burns a mark on what it struck (<c>BulletHole_eCreateBulletHoleTaskEvent</c>).</summary>
    /// <param name="ground">What it struck.</param>
    /// <param name="radius">The mark's size.</param>
    void BurnMark(Igi2Intersection ground, float radius);

    /// <summary>The explosion sounds (<c>SoundSys_NewSoundFn</c> placed at the blast).</summary>
    /// <param name="sound">The sound.</param>
    /// <param name="position">Where.</param>
    void Sound(string sound, Vector3d position);

    /// <summary>The explosion raises an AI event (<c>HumanAI_SendEvent</c>, <c>igi2 0x460A90</c>).</summary>
    /// <param name="aiEvent">The event.</param>
    /// <param name="flags">Its flags.</param>
    /// <param name="position">Where.</param>
    /// <param name="range">How far it carries.</param>
    /// <param name="team">The team it concerns: an explosion's, 1; a grenade's, every team.</param>
    void AiEvent(int aiEvent, int flags, Vector3d position, float range, int team = 1);
}
