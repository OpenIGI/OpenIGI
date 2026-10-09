using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>What a human does to the thing it activates, <c>eHumanActivate</c> as a target's <c>GetActivate</c> handler gives it.</summary>
public enum Igi2ActivateKind
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>A switch is pressed (<c>Switch_GetActivateHandler</c>, <c>igi2 0x4DADC0</c>).</summary>
    Switch = 1,

    /// <summary>A door is opened or shut (<c>Door_GetActivateHandler</c>, <c>0x537790</c>).</summary>
    Door = 2,

    /// <summary>A wire is slid down (<c>Wire_GetActivateHandler</c>, <c>0x514CA0</c>).</summary>
    Wire = 7,

    /// <summary>A timed action at a code pad or a padlock, <c>GenericTBA_GetActivateHandler</c> (<c>igi2 0x4C2940</c>).</summary>
    GenericTba = 10,
}

/// <summary>Something a human may activate: it says whether a human's view finds it, and what activating it does.</summary>
public interface IIgi2Activatable
{
    /// <summary>Gets the centre of its sphere in the world's stationary tree.</summary>
    Vector3d Centre { get; }

    /// <summary>Gets the radius of that sphere.</summary>
    float Radius { get; }

    /// <summary>Gets what activating it does, its <c>GetActivate</c> handler.</summary>
    Igi2ActivateKind Kind { get; }

    /// <summary>Offers itself to a human's view, its <c>GameFunctions_eEvaluateActivateQTaskEvent</c> handler.</summary>
    /// <param name="context">The view and the best found so far.</param>
    void EvaluateActivate(Igi2EvaluateActivate context);

    /// <summary>
    /// A tick of being activated, <c>GameFunctions_eActivateQTaskEvent</c>, sent by <c>Human_UpdateActivate</c>
    /// (<c>igi2 0x500D10</c>) to what a human is activating before it counts the tick. A door's and a switch's handlers
    /// (<c>Door_ActivateHandler</c> <c>0x5377A0</c>, <c>Switch_ActivateHandler</c> <c>0x4DADD0</c>) do nothing.
    /// </summary>
    /// <param name="activateTime">The ticks the activation has run, before this one.</param>
    void Activate(int activateTime)
    {
    }
}

/// <summary>An activatable that opens and closes, as a door does, and may be locked.</summary>
public interface IIgi2Door : IIgi2Activatable
{
    /// <summary>Gets a value indicating whether it is locked, <c>Door_IsLocked</c> (<c>igi2 0x5383F0</c>).</summary>
    bool IsLocked { get; }

    /// <summary>Gets a value indicating whether its lock can be picked now, <c>Door_IsPickable</c> (<c>igi2 0x538400</c>).</summary>
    bool IsPickable { get; }

    /// <summary>Gets or sets a value indicating whether its lock has been picked, which unlocks it for good.</summary>
    bool IsPicked { get; set; }

    /// <summary>Gets its state: 0 open, 2 told to open, 1 shut, 3 closing (<c>Door_GetDoorStatus</c>, <c>igi2 0x538460</c>).</summary>
    int Status { get; }

    /// <summary>Gets the seconds picking its lock takes.</summary>
    float PickLockTime { get; }

    /// <summary>Gets the sound picking its lock makes.</summary>
    string ActivateSound { get; }

    /// <summary>Gets its model's first magic vertex where it stands now, the spot a picker kneels at.</summary>
    Vector3d MagicVertex { get; }

    /// <summary>Gets which way it faces now, row by row.</summary>
    IReadOnlyList<float> CurrentOrientation { get; }

    /// <summary>Tells it to open if it is shut or closing, or to close: <c>Door_ToggleOpenDoorEx</c> (<c>igi2 0x5384D0</c>).</summary>
    /// <param name="from">Where the one opening it stands, which a door that opens both ways swings away from.</param>
    void ToggleOpen(Vector3d from);
}

/// <summary>An activatable that a press acts on at once, as a switch is.</summary>
public interface IIgi2Pressable : IIgi2Activatable
{
    /// <summary>Is pressed, <c>Switch_Pressed</c> (<c>igi2 0x4DB3E0</c>).</summary>
    /// <param name="clientId">Who pressed it: nought for the single player.</param>
    void Press(int clientId);
}

/// <summary>
/// A human's look for something to activate, <c>EvaluateActivateContext_t</c>: where its view is and which way it faces, and
/// the best target found so far.
/// </summary>
/// <remarks>
/// <c>Human_UpdateActivate</c> (<c>igi2 0x500D10</c>) fills it and offers it to everything in the stationary tree within the
/// human's sphere; each target that would accept the human resolves itself against it
/// (<see cref="Resolve"/>). The best is the one nearest the line of sight, within a quarter turn for the player and a half
/// turn for anyone else, and nearer than 8192 units.
/// </remarks>
public sealed class Igi2EvaluateActivate
{
    /// <summary>The widest turn off the player's line of sight it finds anything at, <c>0x3F490FDB</c>.</summary>
    public const float PlayerReach = 0.78539819f;

    /// <summary>The widest turn off anyone else's, <c>0x40490FDB</c>.</summary>
    public const float OtherReach = 3.1415927f;

    /// <summary>How far it finds anything, <c>vBestLength</c>.</summary>
    public const double Reach = 8192.0;

    /// <summary>Initializes a new instance of the <see cref="Igi2EvaluateActivate"/> class.</summary>
    /// <param name="viewPosition">Where the human's view is, <c>tViewPos</c>.</param>
    /// <param name="viewOrientation">Which way it faces, nine floats row by row, <c>tViewOrientation</c>.</param>
    /// <param name="isPlayer">Whether the human is the player.</param>
    public Igi2EvaluateActivate(Vector3d viewPosition, IReadOnlyList<float> viewOrientation, bool isPlayer)
    {
        ArgumentNullException.ThrowIfNull(viewOrientation);
        ViewPosition = viewPosition;
        ViewOrientation = viewOrientation;
        BestAlpha = isPlayer ? PlayerReach : OtherReach;
        BestGamma = BestAlpha;
    }

    /// <summary>Gets where the view is.</summary>
    public Vector3d ViewPosition { get; }

    /// <summary>Gets which way the view faces.</summary>
    public IReadOnlyList<float> ViewOrientation { get; }

    /// <summary>Gets the best target's pitch off the line of sight, <c>vBestAlpha</c>; a resolve sets it to nought.</summary>
    public float BestAlpha { get; private set; }

    /// <summary>Gets the best target's turn off the line of sight, <c>vBestGamma</c>.</summary>
    public float BestGamma { get; private set; }

    /// <summary>Gets the best target found, <c>ptBestQTask</c>.</summary>
    public IIgi2Activatable? Best { get; private set; }

    /// <summary>
    /// Takes a target if it is the best yet, <c>GameFunctions_ResolveEvaluateActivate</c> (<c>igi2 0x48D0E0</c>): nearer than
    /// <see cref="Reach"/>, in front of the view, and no further round from its line than the best so far.
    /// </summary>
    /// <param name="position">Where the target is.</param>
    /// <param name="target">The target.</param>
    /// <returns>Whether it was taken.</returns>
    public bool Resolve(Vector3d position, IIgi2Activatable target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Fpu dx = (Fpu)position.X - ViewPosition.X;
        Fpu dy = (Fpu)position.Y - ViewPosition.Y;
        Fpu dz = (Fpu)position.Z - ViewPosition.Z;
        if (Fpu.Sqrt(((dy * dy) + (dz * dz)) + (dx * dx)).Value >= Reach)
        {
            return false;
        }

        IReadOnlyList<float> m = ViewOrientation;
        double x = ((((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx)).Value;
        double y = ((((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx)).Value;
        if (y <= 0.0)
        {
            return false;
        }

        float gamma = Math.Abs(Igi2Pose.CalcGamma(x, y));
        if (gamma > BestGamma)
        {
            return false;
        }

        BestGamma = gamma;
        Best = target;
        BestAlpha = 0f;
        return true;
    }
}
