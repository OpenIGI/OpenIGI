namespace OpenIGI.Game.Igi2.Player;

/// <summary>The player's locomotion states, by their slot in <c>HumanPlayer_apfState</c>.</summary>
/// <remarks>
/// <para>
/// Slot numbers are the original's (<c>HumanPlayer_InitStateTable</c>, <c>igi2 0x49EE00</c>) because the
/// states transition by number and the animation table is indexed by it. Slots the table leaves empty are
/// not named.
/// </para>
/// </remarks>
public static class Igi2PlayerState
{
    /// <summary>The number of slots in the table.</summary>
    public const int Count = 109;

    /// <summary>Standing still.</summary>
    public const int Stand = 0;

    /// <summary>Turning on the spot.</summary>
    public const int StandTurn = 1;

    /// <summary>Running forward.</summary>
    public const int RunForward = 3;

    /// <summary>Running forward and right.</summary>
    public const int RunForwardRight = 4;

    /// <summary>Running forward and left.</summary>
    public const int RunForwardLeft = 5;

    /// <summary>Running backward.</summary>
    public const int RunBackward = 6;

    /// <summary>Running backward and right.</summary>
    public const int RunBackwardRight = 7;

    /// <summary>Running backward and left.</summary>
    public const int RunBackwardLeft = 8;

    /// <summary>Running right.</summary>
    public const int RunRight = 9;

    /// <summary>Running left.</summary>
    public const int RunLeft = 10;

    /// <summary>Walking forward.</summary>
    public const int WalkForward = 11;

    /// <summary>Walking forward and right.</summary>
    public const int WalkForwardRight = 12;

    /// <summary>Walking forward and left.</summary>
    public const int WalkForwardLeft = 13;

    /// <summary>Walking backward.</summary>
    public const int WalkBackward = 14;

    /// <summary>Walking backward and right.</summary>
    public const int WalkBackwardRight = 15;

    /// <summary>Walking backward and left.</summary>
    public const int WalkBackwardLeft = 16;

    /// <summary>Walking right.</summary>
    public const int WalkRight = 17;

    /// <summary>Walking left.</summary>
    public const int WalkLeft = 18;

    /// <summary>Swimming; slots 19 and 20 share its entry.</summary>
    public const int Swim = 21;

    /// <summary>Dying.</summary>
    public const int Die = 55;

    /// <summary>Crouching still.</summary>
    public const int Crouch = 23;

    /// <summary>Turning crouched.</summary>
    public const int CrouchTurn = 24;

    /// <summary>Walking forward crouched.</summary>
    public const int CrouchWalkForward = 26;

    /// <summary>Jumping.</summary>
    public const int Jump = 36;

    /// <summary>Falling.</summary>
    public const int Fall = 37;

    /// <summary>Flinching from a hit.</summary>
    public const int Hit = 54;

    /// <summary>Lying flat.</summary>
    public const int Flat = 75;

    /// <summary>Crawling.</summary>
    public const int FlatCrawl = 80;
}
