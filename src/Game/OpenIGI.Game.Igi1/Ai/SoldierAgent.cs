using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>One of the level's soldiers as the simulation keeps it: who it is, where it spawned, and its weapon's state.</summary>
/// <param name="view">The soldier's body and rig.</param>
/// <param name="position">Where the level placed it.</param>
/// <remarks>
/// What its renderer draws it from is the renderer's own; what the renderer saw of it on the last frame -- whether it
/// was drawn, and from which eye -- it hands back here, since retail's AI re-reads a soldier's view only when it was
/// drawn.
/// </remarks>
public sealed class SoldierAgent(SoldierView view, Vector3d position)
{
    /// <summary>Gets the soldier's body and rig.</summary>
    public SoldierView View { get; } = view;

    /// <summary>Whether the last frame's main view drew this soldier -- retail's cue to re-skin it.</summary>
    public bool InView { get; set; }

    /// <summary>The eye orientation from the last frame that drew this soldier, or none yet.</summary>
    public Matrix3f? EyeOrientation { get; set; }

    /// <summary>Gets where the level placed it.</summary>
    public Vector3d Position { get; } = position;

    /// <summary>Whether the authored origin is cinematic staging rather than a gameplay spawn.</summary>
    public bool IsCutsceneActor { get; init; }

    /// <summary>Whether an authored one-shot has moved this staged Human from its spawn.</summary>
    public bool HasCutsceneRootMotion { get; set; }

    /// <summary>Whether an authored WalkTo/RunTo route has handed placement to the Human mover.</summary>
    public bool HasCutsceneGameplayMovement { get; set; }

    /// <summary>The carried weapon table row restored after a temporary stationary-gun mount ends.</summary>
    public WeaponType? CarriedWeaponType { get; set; }

    /// <summary>Whether a round left this soldier's barrel on the current simulation tick.</summary>
    public bool FiredThisTick { get; set; }

    /// <summary>Monotonic round serial used to vary the retail gun-flame attachment.</summary>
    public int ShotSequence { get; set; }

    /// <summary>The weapon's table row, which the AI reads its clip size and burst length from.</summary>
    public WeaponType? WeaponType { get; set; }

    /// <summary>How many rounds are in the clip. The AI watches this change to count its burst.</summary>
    public int AmmoInClip { get; set; }

    /// <summary>Ticks left before the weapon's cadence lets another round out.</summary>
    public int FireCooldown { get; set; }

    /// <summary>Rounds fired since the trigger went down — the gun's <c>+320</c> that picks the spread.</summary>
    public int RoundsThisPull { get; set; }

    /// <summary>Ticks left of a reload.</summary>
    public int ReloadTicks { get; set; }
}
