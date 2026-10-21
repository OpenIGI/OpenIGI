using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Soldiers;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>The idle sweep of a mind's view, <c>tViewConeRotationInfo</c>.</summary>
public sealed class Igi2AiViewSweep
{
    /// <summary>Gets or sets how far the view is tilted, radians.</summary>
    public float Alpha { get; set; }

    /// <summary>Gets or sets where the tilt is heading.</summary>
    public float AlphaFactor { get; set; }

    /// <summary>Gets or sets how far the view is turned, radians.</summary>
    public float Gamma { get; set; }

    /// <summary>Gets or sets where the turn is heading.</summary>
    public float GammaFactor { get; set; }

    /// <summary>Gets or sets which of the four sweep steps it is on.</summary>
    public int Index { get; set; }

    /// <summary>Gets or sets the ticks it rests before sweeping again.</summary>
    public int Timer { get; set; }
}

/// <content>A mind's state between ticks: nodes, timers, lines to say (<c>tCommon</c>, <c>tCombat</c> and the scalars).</content>
public sealed partial class Igi2HumanAi
{
    private const float OneDegree = 0.01745329238474369f;
    private const float SweepTilt = 0.1745329350233078f;
    private const float SweepTurn = 0.7853981852531433f;
    private static readonly float[] SweepAlphaSign = [0f, -1f, 0f, 1f];
    private static readonly float[] SweepGammaSign = [1f, 0f, -1f, 0f];

    private readonly List<string> _soundQueue = [];

    /// <summary>Gets the idle sweep of its view.</summary>
    public Igi2AiViewSweep ViewConeRotation { get; } = new();

    /// <summary>Gets or sets the node it was on before, <c>nPrevNode</c>.</summary>
    public int PreviousNode { get; set; } = -1;

    /// <summary>Gets or sets the node it is on, <c>nCurrentNode</c>.</summary>
    public int CurrentNode { get; set; } = -1;

    /// <summary>Gets or sets the node it is going to, <c>nDestinationNode</c>.</summary>
    public int DestinationNode { get; set; } = -1;

    /// <summary>Gets or sets the node its way starts from, <c>nCurrentStartNode</c>.</summary>
    public int CurrentStartNode { get; set; } = -1;

    /// <summary>Gets or sets the node it returns to, <c>nOriginalNode</c>.</summary>
    public int OriginalNode { get; set; } = -1;

    /// <summary>Gets or sets the way it faced at the start, <c>tOriginalViewDirection</c>.</summary>
    public Vector3f OriginalViewDirection { get; set; }

    /// <summary>Gets or sets the ticks detection ignores how lit a target is, <c>nIgnoreVisibilityFactorTimer</c>.</summary>
    public int IgnoreVisibilityFactorTimer { get; set; }

    /// <summary>Gets or sets the ticks before it may roll, <c>nRollDisallowedTimer</c>.</summary>
    public int RollDisallowedTimer { get; set; }

    /// <summary>Gets or sets a value indicating whether it has played its detection line, <c>isDetectionSoundPlayed</c>.</summary>
    public bool IsDetectionSoundPlayed { get; set; }

    /// <summary>Gets or sets a value indicating whether its stand action has started, <c>isStandActionStarted</c>.</summary>
    public bool IsStandActionStarted { get; set; }

    /// <summary>Gets or sets the door it is using, <c>ptDoorQTaskRef</c>, or <see langword="null"/>.</summary>
    public object? Door { get; set; }

    /// <summary>Gets or sets a value indicating whether it triggered its door, <c>isDoorTriggered</c>.</summary>
    public bool IsDoorTriggered { get; set; }

    /// <summary>Gets or sets where it aims, <c>tAimPosition</c>.</summary>
    public Vector3d AimPosition { get; set; }

    /// <summary>Gets or sets where its target is, <c>tTargetPosition</c>: where its shots go.</summary>
    public Vector3d TargetPosition { get; set; }

    /// <summary>Gets or sets where its target was last, <c>tCombat.tLastTargetPos</c>.</summary>
    public Vector3d LastTargetPosition { get; set; }

    /// <summary>Gets or sets a value indicating whether it carries a grenade, <c>tCombat.isGrenade</c>.</summary>
    public bool HasGrenade { get; set; }

    /// <summary>Gets or sets a value indicating whether its squad lets it fire, <c>tCombat.isAllowedToFire</c>.</summary>
    public bool IsAllowedToFire { get; set; }

    /// <summary>Gets or sets the ticks of a burst's pause, <c>tCombat.iFireDelayTimer</c>.</summary>
    public int FireDelayTimer { get; set; }

    /// <summary>Gets or sets the shots left in a burst, <c>tCombat.nShotsBeforeDelay</c>.</summary>
    public int ShotsBeforeDelay { get; set; }

    /// <summary>Gets or sets its grenade's weapon id, <c>tCombat.iGrenadeWeaponID</c>.</summary>
    public int GrenadeWeaponTypeId { get; set; } = -1;

    /// <summary>Gets or sets the indoor sector it stands in, or <see langword="null"/> outdoors (<c>ptCurrentSectorQTask</c>).</summary>
    public object? CurrentSector { get; set; }

    /// <summary>Gets or sets a value indicating whether it stands between two sectors, <c>isBetweenSectors</c>.</summary>
    public bool IsBetweenSectors { get; set; }

    /// <summary>Gets or sets its legal nodes: the two it may stand between (<c>tLegalNodesData</c>).</summary>
    public (int Node1, int Node2, bool IsUsed) LegalNodes { get; set; } = (-1, -1, false);

    /// <summary>Gets the lines waiting to be said, <c>azSoundQueue</c>, at most five.</summary>
    public IReadOnlyList<string> SoundQueue => _soundQueue;

    /// <summary>
    /// The idle sweep (<c>HumanAI_UpdateIdleViewConeRotation</c>): resting, or not idle, the view relaxes a degree a tick toward
    /// straight; otherwise it sweeps a four-step pattern a degree a tick, ten degrees up and down and forty-five across, resting
    /// 150 ticks after the last step.
    /// </summary>
    public void UpdateIdleViewConeRotation()
    {
        Igi2AiViewSweep sweep = ViewConeRotation;
        bool resting = false;
        if (sweep.Timer != 0)
        {
            resting = true;
            sweep.Timer--;
        }

        if (!IsUseIdleViewConeRotation() || resting)
        {
            sweep.Alpha = (Step(-(Fpu)sweep.Alpha) + sweep.Alpha).F;
            Fpu back = -(Fpu)sweep.Gamma;
            sweep.Gamma = back.Value >= -OneDegree
                ? (Step(back) + sweep.Gamma).F
                : ((Fpu)sweep.Gamma + -OneDegree).F;
            return;
        }

        int index = sweep.Index;
        bool turned = false;
        sweep.Alpha = (Step((Fpu)sweep.AlphaFactor - sweep.Alpha) + sweep.Alpha).F;
        Fpu alphaFactor = ((Fpu)SweepAlphaSign[index] * OneDegree) + sweep.AlphaFactor;
        sweep.AlphaFactor = alphaFactor.F;
        if (alphaFactor.Value > SweepTilt)
        {
            sweep.AlphaFactor = SweepTilt;
            turned = true;
        }
        else if (alphaFactor.Value < -SweepTilt)
        {
            sweep.AlphaFactor = -SweepTilt;
            turned = true;
        }

        sweep.Gamma = (Step((Fpu)sweep.GammaFactor - sweep.Gamma) + sweep.Gamma).F;
        Fpu gammaFactor = ((Fpu)SweepGammaSign[index] * OneDegree) + sweep.GammaFactor;
        sweep.GammaFactor = gammaFactor.F;
        if (gammaFactor.Value > SweepTurn)
        {
            sweep.GammaFactor = SweepTurn;
        }
        else if (gammaFactor.Value < -SweepTurn)
        {
            sweep.GammaFactor = -SweepTurn;
        }
        else if (!turned)
        {
            return;
        }

        if (index >= 3)
        {
            sweep.Timer = 150;
            sweep.Index = 0;
        }
        else
        {
            sweep.Index = index + 1;
        }

        static Fpu Step(Fpu toward) => toward.Value >= -OneDegree ? (toward.Value > OneDegree ? OneDegree : toward) : -OneDegree;
    }

    /// <summary>
    /// Whether the view sweeps (<c>HumanAI_IsUseIdleViewConeRotation</c>): patrolling, idle, with nothing to do or with its squad
    /// patrolling, with no target in sight, and not seated on a gun or in a vehicle.
    /// </summary>
    /// <returns><see langword="true"/> when it sweeps.</returns>
    public bool IsUseIdleViewConeRotation()
    {
        Igi2AiActionItem? top = Actions.Top;
        bool idle = top is null || top.Init == Igi2AiContent.PatrolInit || top.Init == Igi2AiContent.IdleInit || Squad?.State == 6;
        return idle && GetTarget(visibleOnly: true) is null && ((uint)Soldier.Signal & 0x1000000u) == 0;
    }

    /// <summary>
    /// Finds the indoor sector it stands in from its node, or from both its legal nodes (between two sectors when they differ)
    /// (<c>HumanAI_FindCurrentSectorFromNodes</c>).
    /// </summary>
    public void FindCurrentSectorFromNodes()
    {
        if (Graph is not { } graph)
        {
            return;
        }

        if (LegalNodes.IsUsed)
        {
            object? first = graph.NodeAt(LegalNodes.Node1)?.Sector;
            object? second = graph.NodeAt(LegalNodes.Node2)?.Sector;
            if (!ReferenceEquals(first, second))
            {
                CurrentSector = null;
                IsBetweenSectors = true;
                return;
            }

            CurrentSector = first;
        }
        else
        {
            CurrentSector = graph.NodeAt(CurrentNode)?.Sector;
        }

        IsBetweenSectors = false;
    }

    /// <inheritdoc/>
    void IIgi2SoldierMind.Say(Igi2HumanSoldier soldier, string sound) => Say(soldier, sound, queueable: false);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>HumanAI_PlaySoundAtPosition</c>: nothing from a dying body; while a line it said is timing out a queueable line
    /// waits its turn (at most five wait); otherwise the line plays at its eyes and times out in sixty ticks.
    /// </remarks>
    /// <returns>The line playing, or <see langword="null"/>.</returns>
    public IIgi2SoundInstance? Say(Igi2HumanSoldier soldier, string sound, bool queueable)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        // A body that has begun to die says nothing (HumanSoldier+0xBE8, set by RunStateDie).
        if (soldier.LifeState != 0)
        {
            return null;
        }

        if (SoundTimer != 0)
        {
            if (queueable && _soundQueue.Count < 5)
            {
                _soundQueue.Add(sound);
            }

            return null;
        }

        if (soldier.Surroundings.PlaySoundAt(sound, View.Position) is { } playing)
        {
            SoundTimer = 60;
            return playing;
        }

        return null;
    }

    /// <summary>Says the oldest waiting line once the last has timed out (<c>HumanAI_MaintainSoundQueue</c>).</summary>
    public void MaintainSoundQueue()
    {
        if (_soundQueue.Count == 0 || SoundTimer != 0)
        {
            return;
        }

        string line = _soundQueue[0];
        _soundQueue.RemoveAt(0);
        Say(Soldier, line, queueable: false);
    }

    /// <summary>Finds whether it carries a live grenade, and which (<c>HumanAI_FindWeapons</c>).</summary>
    public void FindWeapons()
    {
        if (Soldier.Weapon.Inventory?.FindWeapon("WEAPON_ID_GRENADE") is { IsActive: true } grenade)
        {
            HasGrenade = true;
            GrenadeWeaponTypeId = grenade.WeaponType.Id;
        }
    }

    /// <summary>Gets the grenade it carries, as the soldier's mind interface asks it.</summary>
    public string? GrenadeWeaponId => HasGrenade ? "WEAPON_ID_GRENADE" : null;

    /// <summary>Gets where it would throw its grenade.</summary>
    public Vector3d? GrenadeTargetPosition => HasGrenade ? LastTargetPosition : null;

    /// <summary>Gets where its target is.</summary>
    Vector3d? IIgi2SoldierMind.TargetPosition => GetTargetPosition(out _);

    /// <summary>Gets where it fires from: its eyes, <c>tViewPosition</c>.</summary>
    public Vector3d FirePosition => View.ViewPosition;

    /// <summary>Gets the orientation it looks with.</summary>
    public IReadOnlyList<float> FireOrientation => View.ViewOrientation;

    /// <summary>Gets the point its shots go at.</summary>
    public Vector3d AimTarget => TargetPosition;

    /// <summary>Gets the view sweep's turn, as the soldier's head follows it.</summary>
    public float HeadTurn => ViewConeRotation.Gamma;

    /// <summary>Lets go of the door it was using.</summary>
    public void ReleaseDoor()
    {
        if (IsDoorTriggered)
        {
            Door = null;
            IsDoorTriggered = false;
        }
    }

    /// <summary>Lets go of the things it was activating (<c>HumanAI_ClearActivationEntry</c> on both).</summary>
    public void ClearActivationEntries()
    {
        World.Activations.Release(this);
    }
}
