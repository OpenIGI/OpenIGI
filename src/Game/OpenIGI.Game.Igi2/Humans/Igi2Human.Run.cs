using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>The shared human's run handler.</content>
public abstract partial class Igi2Human
{
    /// <summary>What is left of a muzzle flash's light on the holder after a tick, <c>0x3F4CCCCD</c>.</summary>
    private const float FlashDecay = 0.80000001f;

    /// <summary>Gets or sets the ticks it has been active this mission, <c>tMissionScoreData.nTime</c>.</summary>
    public int MissionTime { get; set; }

    /// <summary>Gets or sets the save games loaded this mission, <c>tMissionScoreData.nGameLoads</c>.</summary>
    public int GameLoads { get; set; }

    private bool _inSmoke;
    private IIgi2SoundInstance? _cough;

    /// <summary>Marks a human inside smoke, <c>human.c Human_SmokeAreaHandler</c>.</summary>
    /// <param name="position">The smoke centre.</param>
    /// <param name="range">Its radius.</param>
    public void SmokeArea(Vector3d position, float range)
    {
        float x = ((Fpu)Position.X - position.X).F;
        float y = ((Fpu)Position.Y - position.Y).F;
        Fpu z = (Fpu)Position.Z - position.Z;
        if (Fpu.Sqrt((((Fpu)x * x) + ((Fpu)y * y)) + (z * z)).Value <= range)
        {
            _inSmoke = true;
        }
    }

    /// <summary>Starts a cough only when none is playing and clears the smoke flag, <c>human.c Human_MaintainCough</c>.</summary>
    protected void MaintainCough()
    {
        if (_inSmoke && _cough is not { IsAlive: true })
        {
            _cough = Surroundings.StartSound("cough");
        }

        _inSmoke = false;
    }

    /// <summary>
    /// The human's own part of its run handler, single-player: <c>Human_RunHandler</c> (<c>igi2 0x4FE3A0</c>) before it runs the
    /// body's children.
    /// </summary>
    /// <remarks>
    /// While a shot keeps the human visible regardless of the light (<see cref="IgnoreLightEffectOnVisibilityTimer"/>), the
    /// timer counts down and the flash it lit the human by fades to four fifths a tick; once it has run out the flash is gone.
    /// While it is active its mission clock counts the tick (<c>tMissionScoreData.nTime</c>), which a save game keeps as its time.
    /// Not ported: the blood puddle under a body that has come to rest.
    /// </remarks>
    protected void RunHuman()
    {
        if (IsActive)
        {
            MissionTime++;
        }

        if (IgnoreLightEffectOnVisibilityTimer != 0)
        {
            Vector3f flash = FlashEffect;
            float x = ((Fpu)flash.X * FlashDecay).F;
            IgnoreLightEffectOnVisibilityTimer--;
            FlashEffect = new Vector3f(x, ((Fpu)flash.Y * FlashDecay).F, ((Fpu)flash.Z * FlashDecay).F);
        }
        else
        {
            FlashEffect = default;
        }

        MaintainCough();
    }
}
