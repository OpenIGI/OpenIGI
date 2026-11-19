using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// An I.G.I. 2 level's switches, doors, terminals, timed actions and cabinets as another game's human works them by hand
/// (<see cref="WorldInteraction"/>), each keeping its own rules: the reverse of <see cref="Igi2HostInteraction"/>, which has an
/// I.G.I. 2 human work another game's.
/// </summary>
/// <remarks>
/// <para>
/// Each thing is offered by its own <see cref="IIgi2Activatable.EvaluateActivate"/>, asked from where the human looks from with the
/// view turned straight at it, so its own front and width decide and the human's own game decides the aim; and it is pressed,
/// opened, worked and finished through the calls I.G.I. 2's player makes (<c>HumanPlayer_InitActivate</c>, <c>igi2 0x4A06A0</c>,
/// and the states it enters).
/// </para>
/// <para>
/// The kinds a human climbs, rides or mans -- ladders, wires, fences, mounted guns -- are not among them: those are places a
/// body goes, which each game moves through in its own way.
/// </para>
/// </remarks>
public static class Igi2GuestInteractions
{
    // The sprites Project I.G.I. shows for each kind of thing, by its own names.
    private const string SwitchSprite = "action_pressbutton.spr";
    private const string DoorSprite = "a_door.spr";
    private const string CabinetSprite = "action_searchcab.spr";
    private const string TerminalSprite = "action_comput.spr";
    private const string ActionSprite = "a_c4.spr";

    private static readonly float[] Upright = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Makes an interaction for each thing of the level a human works by hand, in the order the level holds them.</summary>
    /// <param name="levelRoot">The level, once its systems have prepared its tasks: each thing's frame is read as it stands.</param>
    /// <returns>The interactions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<WorldInteraction> Of(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        List<WorldInteraction> made = [];
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (task is IIgi2Activatable target && For(task, target) is { } interaction)
            {
                made.Add(interaction);
            }
        }

        return made;
    }

    /// <summary>The level's ladders as Project I.G.I.'s humans climb them (<see cref="LadderPlacement"/>), in the order the level holds them.</summary>
    /// <param name="levelRoot">The level, once its systems have prepared its tasks.</param>
    /// <returns>The ladders: each from its foot to its head, with where it is got on at the top.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    /// <remarks>The reverse of <see cref="Igi2HostLadder"/>: the same four points and frame, read the other way.</remarks>
    public static IReadOnlyList<LadderPlacement> Ladders(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        List<LadderPlacement> ladders = [];
        foreach (IIgi2Ladder ladder in levelRoot.Descendants().OfType<IIgi2Ladder>())
        {
            IReadOnlyList<float> m = ladder.Frame;
            if (m.Count >= 9)
            {
                ladders.Add(new LadderPlacement(
                    ladder.AttachPosition,
                    ladder.MagicVertex(1),
                    ladder.MagicVertex(0),
                    ladder.MagicVertex(3),
                    new Matrix3f(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8])));
            }
        }

        return ladders;
    }

    // The interaction a thing is worked through, or none for a kind a human does not work by hand.
    private static WorldInteraction? For(GameTask task, IIgi2Activatable target)
    {
        (InteractionActivateType type, IReadOnlyList<float> frame, float seconds, string sprite)? shape = target switch
        {
            IIgi2Pressable => (InteractionActivateType.Switch, Upright, 0f, SwitchSprite),
            IIgi2Door door => (InteractionActivateType.Door, door.CurrentOrientation, door.PickLockTime, DoorSprite),
            IIgi2Cabinet cabinet => (InteractionActivateType.Cabinet, cabinet.Frame, cabinet.SearchTime, CabinetSprite),
            IIgi2WorkedActivatable { Kind: Igi2ActivateKind.Terminal } terminal =>
                (InteractionActivateType.Terminal, terminal.CurrentOrientation, terminal.ActivateTime, TerminalSprite),
            IIgi2WorkedActivatable { Kind: Igi2ActivateKind.GenericTba } action =>
                (InteractionActivateType.Generic, action.CurrentOrientation, action.ActivateTime, ActionSprite),
            _ => null,
        };
        if (shape is not var (type, frame, seconds, sprite) || frame.Count < 9)
        {
            return null;
        }

        Matrix3f orientation = new(frame[0], frame[1], frame[2], frame[3], frame[4], frame[5], frame[6], frame[7], frame[8]);
        Vector3d centre = target.Centre;
        return new WorldInteraction(
            task,
            type,
            centre,
            centre,
            orientation,
            Igi2Pose.Gamma(frame[0], frame[3]),
            Math.Max(1, (int)MathF.Round(seconds * 30f)),
            sprite,
            door: null,
            isPickable: false,
            stationaryGun: null,
            localBoundsMinimum: default,
            localBoundsMaximum: default,
            hasLocalBounds: false,
            markerPoint: centre,
            markerLocal: default)
        {
            Hosted = new Thing(task, target),
            TaskPosition = centre,
        };
    }

    // One thing of the level, as the interaction standing for it asks after it and tells it.
    private sealed class Thing(GameTask task, IIgi2Activatable target) : IHostedInteraction
    {
        /// <inheritdoc/>
        public Vector3d Anchor { get; private set; } = target.Centre;

        /// <inheritdoc/>
        public bool IsInWorld => ConditionalContainerGate.IsTaskActive(task);

        /// <inheritdoc/>
        public bool IsLocked => target is IIgi2Door { IsLocked: true };

        /// <inheritdoc/>
        public bool IsPickable => target is IIgi2Door { IsPickable: true };

        /// <inheritdoc/>
        /// <remarks>
        /// The view is turned level at the thing from the eye, and the reach is another human's, any angle: the thing's own gate is
        /// what is asked for here, not how well it is aimed at.
        /// </remarks>
        public bool Offer(Vector3d eye)
        {
            Vector3d centre = target.Centre;
            double dx = centre.X - eye.X;
            double dy = centre.Y - eye.Y;
            double flat = Math.Sqrt((dx * dx) + (dy * dy));
            float fx = flat > 0.0 ? (float)(dx / flat) : 0f;
            float fy = flat > 0.0 ? (float)(dy / flat) : 1f;

            // Its columns the right, forward and up axes, as the view's are.
            Igi2EvaluateActivate context = new(eye, [fy, fx, 0f, -fx, fy, 0f, 0f, 0f, 1f], isPlayer: false);
            target.EvaluateActivate(context);
            if (!ReferenceEquals(context.Best, target))
            {
                return false;
            }

            Anchor = context.BestPosition ?? centre;
            return true;
        }

        /// <inheritdoc/>
        public void Press() => (target as IIgi2Pressable)?.Press(0);

        /// <inheritdoc/>
        public void Toggle(Vector3d from) => (target as IIgi2Door)?.ToggleOpen(from);

        /// <inheritdoc/>
        /// <remarks>A cabinet's drawer comes out as the search begins (<c>HumanPlayer_RunStateActivateCabinet</c>'s first trigger).</remarks>
        public void Begin() => (target as IIgi2Cabinet)?.OpenDrawer();

        /// <inheritdoc/>
        public void Use(int ticks) => target.Activate(ticks);

        /// <inheritdoc/>
        public void Complete()
        {
            switch (target)
            {
                case IIgi2Door door:
                    // HumanPlayer_RunStateActivateDoor (igi2 0x4AD620): picked, then opened by whoever picked it.
                    door.IsPicked = true;
                    break;
                case IIgi2Cabinet cabinet:
                    cabinet.Searched();
                    cabinet.CloseDrawer();
                    break;
                case IIgi2WorkedActivatable worked:
                    worked.Finish();
                    break;
            }
        }

        /// <inheritdoc/>
        public void Cancel() => (target as IIgi2Cabinet)?.SnapDrawerClosed();
    }
}
