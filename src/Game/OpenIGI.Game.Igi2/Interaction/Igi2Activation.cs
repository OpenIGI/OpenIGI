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

    /// <summary>A cabinet is searched, <c>Cabinet_GetActivateHandler</c>.</summary>
    Cabinet = 3,

    /// <summary>A terminal is hacked, <c>Terminal_GetActivateHandler</c> (<c>igi2 0x4CB050</c>).</summary>
    Terminal = 4,

    /// <summary>A mounted gun is manned, which only a soldier does (<c>HumanSoldier_HandleActivation</c>, <c>igi2 0x4E3340</c>).</summary>
    StationaryGun = 5,

    /// <summary>A wire is slid down (<c>Wire_GetActivateHandler</c>, <c>0x514CA0</c>).</summary>
    Wire = 7,

    /// <summary>A ladder, climbed from its foot or its top, <c>Ladder_GetActivateHandler</c>.</summary>
    Ladder = 8,

    /// <summary>A fence, climbed over while it is dead, <c>Fence_GetActivateHandler</c>.</summary>
    Fence = 9,

    /// <summary>A timed action at a code pad or a padlock, <c>GenericTBA_GetActivateHandler</c> (<c>igi2 0x4C2940</c>).</summary>
    GenericTba = 10,

    /// <summary>A soldier killed silently from behind, <c>HumanSoldier_GetActivateHandler</c>.</summary>
    SilentKill = 13,
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

/// <summary>Something a human kneels at and works by holding the activate key for a while: a code pad, a padlock, a terminal.</summary>
public interface IIgi2WorkedActivatable : IIgi2Activatable
{
    /// <summary>Gets the seconds the key must be held.</summary>
    float ActivateTime { get; }

    /// <summary>Gets the sound the human makes at it while it works.</summary>
    string ActivateSound { get; }

    /// <summary>Gets where the human kneels from, its model's second magic vertex.</summary>
    Vector3d MagicVertex { get; }

    /// <summary>Gets its frame, row by row.</summary>
    IReadOnlyList<float> CurrentOrientation { get; }

    /// <summary>Finishes it, from the next tick on.</summary>
    void Finish();
}

/// <summary>Something a human climbs over, as a fence is: across its frame's Y, anywhere along its X.</summary>
public interface IIgi2Fence : IIgi2Activatable
{
    /// <summary>Gets its frame, row by row.</summary>
    IReadOnlyList<float> Frame { get; }

    /// <summary>Gets its pivot, in world units.</summary>
    Vector3d Position { get; }
}

/// <summary>Something a human hangs from and goes along, as a wire is: down its frame's Y, between its two magic vertices.</summary>
public interface IIgi2Wire : IIgi2Activatable
{
    /// <summary>Gets its frame, row by row.</summary>
    IReadOnlyList<float> Frame { get; }

    /// <summary>Gets a value indicating whether it is ridden down rather than climbed, <c>Wire_IsSliding</c>.</summary>
    bool IsSliding { get; }

    /// <summary>A magic vertex in its own frame: 0 at its start, 1 at its end.</summary>
    /// <param name="index">The vertex.</param>
    /// <returns>The vertex.</returns>
    Vector3f LocalMagicVertex(int index);

    /// <summary>A point in its frame.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The point in its frame.</returns>
    (double X, double Y, double Z) Local(Vector3d point);

    /// <summary>A point of its frame in the world.</summary>
    /// <param name="local">The point in its frame.</param>
    /// <returns>The point.</returns>
    Vector3d World(Vector3d local);

    /// <summary>The human lets go of it.</summary>
    void Release();
}

/// <summary>Something a human searches, as a cabinet is: it stands before it, pulls its drawer out and pushes it back.</summary>
public interface IIgi2Cabinet : IIgi2Activatable
{
    /// <summary>Gets its frame, row by row.</summary>
    IReadOnlyList<float> Frame { get; }

    /// <summary>Gets where the human stands before it from, its model's first magic vertex.</summary>
    Vector3d SearchVertex { get; }

    /// <summary>Gets the seconds a search takes.</summary>
    float SearchTime { get; }

    /// <summary>The search clip's trigger pulls the drawer out.</summary>
    void OpenDrawer();

    /// <summary>The search clip's trigger pushes the drawer back.</summary>
    void CloseDrawer();

    /// <summary>A search cut short shuts the drawer at once.</summary>
    void SnapDrawerClosed();

    /// <summary>The search is done.</summary>
    void Searched();
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

    /// <summary>Gets or sets the human looking, <c>ptQTask</c>, which some targets note how they were found on.</summary>
    public object? Activator { get; init; }

    /// <summary>Takes a target outright, as a ladder does once its sight is clear: best, and dead ahead.</summary>
    /// <param name="target">The target.</param>
    public void Take(IIgi2Activatable target)
    {
        ArgumentNullException.ThrowIfNull(target);
        BestAlpha = 0f;
        BestGamma = 0f;
        Best = target;
    }

    /// <summary>Forgets the best target found so far, as a ladder whose sight is blocked does.</summary>
    public void Clear() => Best = null;

    /// <summary>
    /// Offers a target worked from a spot on its front, as a timed action and a terminal are: only a view ahead of the spot along
    /// its frame's Y, and less than a half width to either side of it, resolves it.
    /// </summary>
    /// <param name="at">The spot it is worked from.</param>
    /// <param name="frame">Its frame, row by row.</param>
    /// <param name="halfWidth">How far to either side of the spot the view may be.</param>
    /// <param name="target">The target.</param>
    /// <returns>Whether it was taken.</returns>
    /// <remarks>
    /// <c>GenericTBA_EvaluateActivateHandler</c> (<c>igi2 0x4C27B0</c>) and <c>Terminal_EvaluateActivateHandler</c>
    /// (<c>0x4CAF40</c>) turn the view into the frame alike, each row summed Z, then Y, then X.
    /// </remarks>
    public bool OfferFromFront(Vector3d at, IReadOnlyList<float> frame, double halfWidth, IIgi2Activatable target)
    {
        ArgumentNullException.ThrowIfNull(frame);
        IReadOnlyList<float> m = frame;
        Fpu dx = (Fpu)ViewPosition.X - at.X, dy = (Fpu)ViewPosition.Y - at.Y, dz = (Fpu)ViewPosition.Z - at.Z;
        Fpu across = (((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx);
        Fpu ahead = (((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx);
        return ahead.Value > 0.0 && Math.Abs(across.Value) < halfWidth && Resolve(at, target);
    }

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
