using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>The human as its weapon sees it: the parent the <c>Weapon</c> task asks, and the fields a shot touches.</summary>
public abstract partial class Igi2Human : IIgi2WeaponOwner
{
    /// <summary>Gets or sets the game's tick count, <c>Flow_ptFlow-&gt;nTicks</c>; the session advances it once a tick.</summary>
    public int Ticks { get; set; }

    /// <summary>Gets or sets the human's team, <c>eTeam</c>.</summary>
    public int Team { get; set; }

    /// <summary>Gets or sets a value indicating whether the human is seen from behind, <c>Human_s.isThirdPerson</c>.</summary>
    public bool IsThirdPerson { get; set; }

    /// <summary>Gets or sets the colour a shot's flash lights the human with, <c>Human_s.tFlashEffect</c>.</summary>
    public Vector3f FlashEffect { get; set; }

    /// <summary>Gets or sets the ticks the human stays visible whatever the light, <c>Human_s.nIgnoreLightEffectOnVisibilityTimer</c>.</summary>
    public int IgnoreLightEffectOnVisibilityTimer { get; set; }

    /// <summary>Gets the shots the human has fired that count towards accuracy, <c>tMissionScoreData</c> shots (<c>Human+0x1EC</c>).</summary>
    public int MissionShots { get; private set; }

    /// <inheritdoc/>
    public virtual bool IsPlayer => false;

    /// <inheritdoc/>
    public virtual int GrenadeHeldTime => 0;

    /// <inheritdoc/>
    public bool IsZoomMode => View.IsZoomMode;

    /// <inheritdoc/>
    public Igi2HumanView? ZoomView => View;

    /// <inheritdoc/>
    public abstract bool HasFireFrame { get; }

    /// <inheritdoc/>
    public abstract Igi2FireFrame GetFireFrame(bool addInaccuracy);

    /// <inheritdoc/>
    public abstract int WeaponObjectCount();

    /// <inheritdoc/>
    public void Flash(Vector3f flash) => FlashEffect = flash;

    /// <inheritdoc/>
    public void IgnoreLightOnVisibility(int ticks) => IgnoreLightEffectOnVisibilityTimer = ticks;

    /// <inheritdoc/>
    public void CountShots(int shots) => MissionShots += shots;

    /// <summary>Gets the humans of another side the human has killed, <c>tMissionScoreData.nKills</c>.</summary>
    public int MissionKills { get; private set; }

    /// <summary>Gets the hits the human's shots have scored, <c>tMissionScoreData.nHits</c>.</summary>
    public int MissionHits { get; private set; }

    /// <summary>
    /// Counts a hit the human scored, <c>Human_HitByYouHandler</c>: on anything but a human of its own side.
    /// </summary>
    /// <param name="target">What was hit.</param>
    public void HitByYou(object target)
    {
        if (target is not Igi2Human human || human.Team != Team)
        {
            MissionHits++;
        }
    }

    /// <summary>Counts a kill the human made, <c>Human_KilledByYouHandler</c>: of a human of another side.</summary>
    /// <param name="target">Who died.</param>
    public void KilledByYou(object target)
    {
        if (target is Igi2Human human && human.Team != Team)
        {
            MissionKills++;
        }
    }
}
