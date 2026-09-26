namespace OpenIGI.Game.Player;

/// <summary>
/// What clip each of a soldier's thirty-eight locomotion states plays — the table at <c>0x53ED10</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>HumanSoldier</c>'s constructor (<c>0x45CC50</c>) builds a state machine at <c>Human+748</c> over
/// the pointer table <c>off_53EC10</c> and installs <b>this</b> table at <c>Human+824</c> — the same
/// pair of fields the player has, with the player's own tables at <c>0x538870</c>. Every state entry
/// function then calls <c>0x489E70(human, stateId, 0.15f)</c>, which indexes it.
/// </para>
/// <para>
/// <b>Animation id 0 means "this state names its own clip"</b> (<see cref="HumanBodyStateTable.SelfDriven"/>),
/// and it is what the three death states carry (<see cref="SoldierDeath"/>).
/// </para>
/// <para>
/// <b>These ids are a soldier's, not the player's.</b> The same number in the player's animation set is
/// a different clip: playing the weapon table's ids on a soldier's rig made guards fall flat on the
/// floor, get up and do it again. A body clip for a soldier comes from here.
/// </para>
/// </remarks>
public static class HumanSoldierStates
{
    /// <summary>Gets the soldier's <c>{animation, mode}</c> table, transcribed from <c>0x53ED10</c>.</summary>
    public static HumanBodyStateTable Table { get; } = new(
        (2, 1), (34, 1), (3, 1), (57, 1), (10, 0), (11, 0), (56, 1), (14, 0), (58, 0), (70, 0),
        (54, 0), (53, 0), (55, 0), (7, 0), (19, 0), (12, 0), (13, 0), (48, 1), (52, 1), (18, 0),
        (33, 0), (32, 0), (9, 1), (15, 0), (8, 0), (20, 0), (16, 0), (17, 0), (21, 0), (2, 1),
        (0, 0), (26, 0), (0, 0), (0, 0), (0, 0), (0, 0), (31, 1), (2, 0));

    /// <summary>The blend time a soldier's state entry passes, in seconds.</summary>
    /// <remarks>
    /// <b>Zero, measured.</b> Forty-four state lookups against this table in a live retail mission all
    /// passed <c>0.000</c>; the player's table <c>0x538870</c> is looked up with <c>0.200</c> and
    /// sometimes <c>0.000</c>. <c>player.md</c> §16 records <c>0.15f</c> for the player, read statically
    /// — whatever that literal is, it is not what reaches this argument, and a soldier blends nothing.
    /// </remarks>
    public const float BlendSeconds = 0f;

    /// <summary>The state a soldier fires from — <b>and the only state it can fire from</b>.</summary>
    /// <remarks>
    /// <para>
    /// Measured first, then read. A live capture showed a fighting guard sitting here on every tick its
    /// burst counter was draining; <c>0x45B060</c>, the state's per-tick, then said why. It is the only
    /// one of the four combat states that touches the weapon, and what it does is
    /// <c>if (*(float *)(human + 456) != 0) human[+1233] = 1</c> — <c>Human+456</c> is <b>input-frame
    /// slot 6</b> (the newest frame lives at <c>+432</c>) and <c>Human+1233</c> is what the weapon tick
    /// hands the gun. So the chain that starts with the AI writing <c>HumanAI+15500</c> ends here, and
    /// a soldier holding the Fire slot up in any other state fires nothing at all.
    /// </para>
    /// <para>
    /// <b>A firing soldier is crouched.</b> State 6 is the only one of the four that sets
    /// <c>Human+829</c>. It is not the AI's kneel — that is state 19.
    /// </para>
    /// </remarks>
    public const int FiringState = 6;

    /// <summary>The standing grenade-throw state, animation 58.</summary>
    /// <remarks>
    /// Measured in <c>mission1-parity.log</c>: <c>0x48DBF0</c> wrote logical channel 8 to input slot 29
    /// while the body was in state 0; the signal builder immediately selected state 8 and animation 58.
    /// The state held after the child callback completed and returned to standing only when the clip ended.
    /// </remarks>
    public const int GrenadeThrowState = 8;

    /// <summary>The standing close-combat state, animation 70.</summary>
    /// <remarks>
    /// Measured in the same retail capture: <c>0x48DBB0</c> wrote logical channel 9 to input slot 30,
    /// selecting state 9 and animation 70. Like the grenade state, it owns the body until its clip ends.
    /// </remarks>
    public const int CloseCombatState = 9;

    /// <summary>The state a soldier's body is in once its burst is spent.</summary>
    /// <remarks>
    /// <para>
    /// The combat cycle is <c>0 → 12 → 10 → 6</c>, and <b>12 is the only way back to 0</b> — its
    /// per-tick <c>0x45B820</c> leaves for state 0 and nothing else. So a guard between bursts is here,
    /// not standing. The live capture agreed: state 12 on the tick the burst counter reached zero, and
    /// state 0 only once the target was out of sight.
    /// </para>
    /// <para>
    /// Reading a spent burst as standing put a tick of the idle clip between every two bursts, and
    /// because bursts are back to back that was a visible flicker.
    /// </para>
    /// </remarks>
    public const int ReloadState = 12;

    /// <summary>The state a soldier stands in.</summary>
    public const int StandState = 0;

    /// <summary>The standing roll-left state, animation 10.</summary>
    /// <remarks>
    /// <c>0x48D830</c> writes raw input slot 2, whose <c>0x20</c> signal selects this state in
    /// <c>0x457A00</c>. Its per-tick, <c>0x45B900</c>, holds until the clip ends and then enters
    /// <see cref="KneelingState"/>.
    /// </remarks>
    public const int RollLeftState = 4;

    /// <summary>The standing roll-right state, animation 11.</summary>
    /// <remarks>The raw-slot-3/<c>0x40</c> twin of <see cref="RollLeftState"/>.</remarks>
    public const int RollRightState = 5;

    /// <summary>The displayed standing reload state, animation 14.</summary>
    /// <remarks>Raw input slot 8/signal <c>0x800</c> selects it; direct weapon reloads do not.</remarks>
    public const int DisplayReloadState = 7;

    /// <summary>The ready state between bursts, animation 54 — <c>0x45B6E0</c>.</summary>
    public const int ReadyState = 10;

    /// <summary>The weapon-raise state, animation 53 — <c>0x458810</c>.</summary>
    public const int RackState = 11;

    /// <summary>The standing-to-kneeling transition, animation 7.</summary>
    public const int CrouchDownState = 13;

    /// <summary>The transition from standing to prone, animation 12.</summary>
    /// <remarks>
    /// Logical combat channel 2 resolves to input slot 22 and signal <c>0x100000</c>. State 15 holds
    /// that transition until its clip completes, then enters <see cref="ProneState"/>.
    /// </remarks>
    public const int LieDownState = 15;

    /// <summary>The transition from prone back to standing, animation 13.</summary>
    /// <remarks><c>0x45BF90</c> selects it as soon as the lie-down signal is released.</remarks>
    public const int GetUpState = 16;

    /// <summary>The stable kneeling state, animation 18.</summary>
    /// <remarks>Both standing rolls finish here before returning to stand when no kneel input is held.</remarks>
    public const int KneelingState = 19;

    /// <summary>The kneeling roll-left state, animation 33.</summary>
    public const int KneelRollLeftState = 20;

    /// <summary>The kneeling roll-right state, animation 32.</summary>
    public const int KneelRollRightState = 21;

    /// <summary>The kneeling firing state, animation 9.</summary>
    public const int KneelFiringState = 22;

    /// <summary>The kneeling displayed-reload state, animation 15.</summary>
    public const int KneelReloadState = 23;

    /// <summary>The kneeling weapon-ready state, animation 8.</summary>
    public const int KneelReadyState = 24;

    /// <summary>The kneeling-to-prone transition, animation 20.</summary>
    public const int KneelToProneState = 25;

    /// <summary>The stable prone state, animation 16.</summary>
    /// <remarks><c>0x45BF90</c> holds it while logical channel 2 remains asserted.</remarks>
    public const int ProneState = 26;

    /// <summary>The prone firing state, animation 17.</summary>
    /// <remarks>
    /// State 26 selects it when raw fire slot 6 contributes signal <c>0x200</c>. Its per-tick
    /// <c>0x45C0C0</c> is the prone state that actually forwards the trigger to the weapon.
    /// </remarks>
    public const int ProneFiringState = 27;

    /// <summary>The prone displayed-reload state, animation 21.</summary>
    public const int ProneReloadState = 28;

    /// <summary>The state a soldier <b>runs</b> in — what the move-and-fight arm's Follow puts it in.</summary>
    /// <remarks>
    /// <para>
    /// Measured by hooking <c>"Combat - Follow"</c> (<c>0x48C860</c>) in the running game: a chasing guard
    /// sits in locomotion state 17 (its diagonal twin is 18), at <b>4.0–5.0 m/s</b> — about three and a half
    /// times the 1.28 m/s patrol walk. Through the table (<c>0x53ED10</c>) that reads as <b>animation 48</b>
    /// (state 18 is 52), whose own root motion carries the run; the walk it was drawn with instead is why a
    /// chasing guard looked like it was strolling.
    /// </para>
    /// <para>
    /// <b>Which of 17/18 is straight-ahead versus diagonal is not yet pinned</b> — both were seen at the same
    /// speed — so 17 is taken as the forward run and the distinction is recorded as open.
    /// </para>
    /// </remarks>
    public const int RunState = 17;

    /// <summary>The tense forward-run state which forwards a held trigger to the weapon.</summary>
    /// <remarks>
    /// IGI 1 state 17's per-tick (<c>0x457FC0</c>) enters this state on signal <c>0x200</c>. Its own
    /// per-tick (<c>0x45AB00</c>) calls the weapon-forwarding helper while slot 6 is non-zero and returns
    /// to state 17 when the trigger is released. Animation 52 is therefore run-and-fire, not a diagonal
    /// locomotion variant.
    /// </remarks>
    public const int RunFiringState = 18;

    /// <summary>The state a soldier falls into when it is not on the ground.</summary>
    /// <remarks>Every combat state's per-tick leaves for this the moment flag <c>0x4</c> clears.</remarks>
    public const int AirborneState = 29;

    /// <summary>The state a soldier flinches into.</summary>
    /// <remarks>
    /// Entered on flag <c>0x10000</c> — which is exactly the bit <c>0x45D3C0</c> raises when the running
    /// damage accumulator passes the settings row's <c>Recoil threshold</c>. The damage handler and the
    /// state machine meet on that bit.
    /// </remarks>
    public const int FlinchState = 30;

    /// <summary>The state a soldier dies into.</summary>
    /// <remarks>
    /// Entered on flag <c>0x20000</c>, the other bit <c>0x45D3C0</c> raises, and its entry is
    /// <c>0x45C440</c> — the general death chooser <see cref="SoldierDeath"/> already read from the
    /// other direction.
    /// </remarks>
    public const int DeathState = 32;
}
