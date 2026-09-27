namespace OpenIGI.Game.Player;

/// <summary>What one tick of a human body machine reads.</summary>
/// <remarks>
/// <para>
/// Each <c>Human</c> class's signal handler (<c>0x45CCF0</c> for a soldier, <c>0x454BA0</c> for a female
/// soldier, <c>0x456CF0</c> for an RPG soldier) packs the machine's flag word from two sources: the
/// input frame at <c>Human+432</c>, which the AI or the player writes, and a handful of bits the engine
/// sets itself. The classes differ in which frame slots they pack and what those bits mean; this type
/// carries the raw material and leaves the packing to each class.
/// </para>
/// <para>
/// The frame slots an AI writes are the ones its type resolves its logical channels to
/// (<c>off_54151C</c>), so the same "take the firing pose" request lands on slot 27 for a guard and on
/// slot 23 for Anya — and each body reads it from where its own handler looks.
/// </para>
/// </remarks>
/// <param name="Frame">The input frame written this tick — <c>Human+432</c>.</param>
/// <param name="AnimationDone">Bit <c>0x1</c>: the current state's clip finished a cycle this tick.</param>
/// <param name="OnGround">Bit <c>0x4</c>: the body has ground under it.</param>
/// <param name="Flinch">Bit <c>0x10000</c>: a hit passed the class's flinch rule this tick.</param>
/// <param name="Dead">Bit <c>0x20000</c>: the damage that killed the body landed.</param>
/// <param name="MovingForward">
/// Bit <c>0x80</c>: forward locomotion. The port's AI moves its bodies directly rather than through
/// frame slot 4, so it reports the fact here.
/// </param>
/// <param name="Running">Bit <c>0x40000</c>: the run byte at <c>Human+3328</c>.</param>
/// <param name="HitZone">
/// The zone the hit that raised <paramref name="Flinch"/> struck — <c>Human+744</c>, which names the flinch
/// clip. <c>-1</c> when nothing was hit.
/// </param>
public readonly record struct HumanBodyInput(
    PlayerInputFrame Frame,
    bool AnimationDone,
    bool OnGround = true,
    bool Flinch = false,
    bool Dead = false,
    bool MovingForward = false,
    bool Running = false,
    int HitZone = -1)
{
    // Frame slots 2..9, packed as bits 0x20..0x1000 by every class's signal handler.
    private const int FirstActionSlot = 2;
    private const int LastActionSlot = 9;

    /// <summary>Whether a frame slot holds a non-zero value — the only test the signal handlers make.</summary>
    /// <param name="slot">The frame slot.</param>
    /// <returns><see langword="true"/> when the slot is non-zero.</returns>
    public bool IsSet(int slot) => Frame[slot] != 0f;

    /// <summary>
    /// Whether any movement or raw action is asked for — frame slots 2..9 (bits <c>0x20</c>..<c>0x1000</c>)
    /// or forward locomotion (<c>0x80</c>).
    /// </summary>
    /// <remarks>Every class's scripted-animation state ends on this; it is the <c>0x1FE0</c> in their exit masks.</remarks>
    public bool AsksForAction
    {
        get
        {
            if (MovingForward)
            {
                return true;
            }

            for (int slot = FirstActionSlot; slot <= LastActionSlot; slot++)
            {
                if (IsSet(slot))
                {
                    return true;
                }
            }

            return false;
        }
    }

}
