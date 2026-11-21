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
/// A wire and a fence are given the shape Project I.G.I.'s own have -- the cable's two ends and its origin, the fence's
/// model box -- since that game rides and climbs them by its own rule once it is on. Ladders are handed over apart
/// (<see cref="Ladders"/>); mounted guns are not offered.
/// </para>
/// </remarks>
public static class Igi2GuestInteractions
{
    private static readonly float[] Upright = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Makes an interaction for each thing of the level a human works by hand, in the order the level holds them.</summary>
    /// <param name="levelRoot">The level, once its systems have prepared its tasks: each thing's frame is read as it stands.</param>
    /// <returns>The interactions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<WorldInteraction> Of(GameTask levelRoot) => Of(levelRoot, man: null);

    /// <summary>Makes an interaction for each thing of the level a human works by hand, its mounted guns among them.</summary>
    /// <param name="levelRoot">The level, once its systems have prepared its tasks: each thing's frame is read as it stands.</param>
    /// <param name="man">
    /// What seats the human on a mounted gun, answering whether it is now manned (<c>Igi2TitlePlayer.ManGunForStandIn</c>);
    /// <see langword="null"/> to leave the guns out.
    /// </param>
    /// <returns>The interactions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<WorldInteraction> Of(GameTask levelRoot, Func<Weapons.Igi2StationaryGunTask, bool>? man)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        List<WorldInteraction> made = [];
        foreach (GameTask task in levelRoot.Descendants())
        {
            if (task is Weapons.Igi2StationaryGunTask gun)
            {
                if (man is not null)
                {
                    made.Add(ForGun(gun, man));
                }
            }
            else if (task is IIgi2Activatable target && For(task, target) is { } interaction)
            {
                made.Add(interaction);
            }
        }

        return made;
    }

    /// <summary>The level's ladders as Project I.G.I.'s humans climb them (<see cref="LadderPlacement"/>), in the order the level holds them.</summary>
    /// <param name="activatables">
    /// What the level's systems made activatable (<c>Igi2LevelBuild.Activatables</c>): its placed ladders and those its buildings'
    /// attach points make, which are no tasks of the level.
    /// </param>
    /// <returns>The ladders: each from its foot to its head, with where it is got on at the top.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="activatables"/> is <see langword="null"/>.</exception>
    /// <remarks>The reverse of <see cref="Igi2HostLadder"/>: the same four points and frame, read the other way.</remarks>
    public static IReadOnlyList<LadderPlacement> Ladders(IEnumerable<IIgi2Activatable> activatables)
    {
        ArgumentNullException.ThrowIfNull(activatables);
        List<LadderPlacement> ladders = [];
        foreach (IIgi2Ladder ladder in activatables.OfType<IIgi2Ladder>())
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
        if (target is IIgi2Wire wire)
        {
            return ForWire(task, wire);
        }

        if (target is Igi2FenceTask fence)
        {
            return ForFence(fence);
        }

        (InteractionActivateType type, IReadOnlyList<float> frame, float seconds)? shape = target switch
        {
            IIgi2Pressable => (InteractionActivateType.Switch, Upright, 0f),
            IIgi2Door door => (InteractionActivateType.Door, door.CurrentOrientation, door.PickLockTime),
            IIgi2Cabinet cabinet => (InteractionActivateType.Cabinet, cabinet.Frame, cabinet.SearchTime),
            IIgi2WorkedActivatable { Kind: Igi2ActivateKind.Terminal } terminal =>
                (InteractionActivateType.Terminal, terminal.CurrentOrientation, terminal.ActivateTime),
            IIgi2WorkedActivatable { Kind: Igi2ActivateKind.GenericTba } action =>
                (InteractionActivateType.Generic, action.CurrentOrientation, action.ActivateTime),
            _ => null,
        };
        if (shape is not var (type, frame, seconds) || frame.Count < 9)
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
            Badge(target),
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

    // A mounted gun: got on with one press, as a switch is pressed. The gun is the level's and is worked by a body of the
    // level's game; the press asks for that.
    private static WorldInteraction ForGun(Weapons.Igi2StationaryGunTask gun, Func<Weapons.Igi2StationaryGunTask, bool> man)
    {
        Vector3d centre = gun.Centre;
        return new WorldInteraction(
            gun,
            InteractionActivateType.Switch,
            centre,
            centre,
            Matrix(Upright),
            0f,
            durationTicks: 1,
            Badge(gun),
            door: null,
            isPickable: false,
            stationaryGun: null,
            localBoundsMinimum: default,
            localBoundsMaximum: default,
            hasLocalBounds: false,
            markerPoint: centre,
            markerLocal: default)
        {
            Hosted = new Thing(gun, gun) { Pressed = () => man(gun) },
            TaskPosition = centre,
        };
    }

    // A wire: the cable between its two magic vertices, measured from the model's origin along its own Y, as Project I.G.I.
    // measures its own (Wire_GetMatrix).
    private static WorldInteraction? ForWire(GameTask task, IIgi2Wire wire)
    {
        IReadOnlyList<float> frame = wire.Frame;
        if (frame.Count < 9)
        {
            return null;
        }

        Vector3f first = wire.LocalMagicVertex(0);
        Vector3f second = wire.LocalMagicVertex(1);
        Vector3d start = wire.World(new Vector3d(first.X, first.Y, first.Z));
        Vector3d end = wire.World(new Vector3d(second.X, second.Y, second.Z));
        Vector3d origin = wire.World(default);
        return new WorldInteraction(
            task,
            InteractionActivateType.Wire,
            start,
            end,
            Matrix(frame),
            Igi2Pose.Gamma(frame[0], frame[3]),
            durationTicks: 1,
            Badge(wire),
            door: null,
            isPickable: false,
            stationaryGun: null,
            localBoundsMinimum: default,
            localBoundsMaximum: default,
            hasLocalBounds: false,
            markerPoint: origin,
            markerLocal: default)
        {
            Hosted = new Thing(task, wire, start),
            TaskPosition = origin,
            WireOriginAuthored = origin,
            WireStartLocalY = first.Y,
            WireEndLocalY = second.Y,
        };
    }

    // A fence: its model's box in its own frame, which it is offered along, mounted beside and touched within.
    private static WorldInteraction? ForFence(Igi2FenceTask fence)
    {
        IReadOnlyList<float> frame = fence.Frame;
        if (frame.Count < 9 || fence.Models?.Invoke(fence.Model) is not { } model)
        {
            return null;
        }

        (Vector3d minimum, Vector3d maximum, bool hasBounds) = MefModelBounds.Of(model);
        WorldInteraction interaction = new(
            fence,
            InteractionActivateType.Fence,
            fence.Position,
            fence.Position,
            Matrix(frame),
            Igi2Pose.Gamma(frame[0], frame[3]),
            WorldInteraction.FenceTraversalTicks,
            Badge(fence),
            door: null,
            isPickable: false,
            stationaryGun: null,
            minimum,
            maximum,
            hasBounds,
            markerPoint: fence.Position,
            markerLocal: default)
        {
            Hosted = new Thing(fence, fence, fence.Position),
            TaskPosition = fence.Position,
        };

        // Live while the level's own expression says so: not offered then, and whoever walks into it is shocked.
        interaction.SetConditions(on: null, locked: null, electric: () => fence.IsElectric);
        return interaction;
    }

    // The badge I.G.I. 2 shows for the thing, which the human standing in is shown as well: the level's own art, not its game's.
    private static string Badge(IIgi2Activatable target) => Igi2ActionBadge.Of(target) ?? string.Empty;

    private static Matrix3f Matrix(IReadOnlyList<float> frame) =>
        new(frame[0], frame[1], frame[2], frame[3], frame[4], frame[5], frame[6], frame[7], frame[8]);

    // One thing of the level, as the interaction standing for it asks after it and tells it. A thing with a place of its own --
    // a wire's end, a fence's origin -- keeps it; another is wherever it last offered itself.
    private sealed class Thing(GameTask task, IIgi2Activatable target, Vector3d? place = null) : IHostedInteraction
    {
        /// <inheritdoc/>
        public Vector3d Anchor { get; private set; } = place ?? target.Centre;

        /// <inheritdoc/>
        public bool IsInWorld => ConditionalContainerGate.IsTaskActive(task);

        /// <inheritdoc/>
        public bool IsLocked => target is IIgi2Door { IsLocked: true };

        /// <inheritdoc/>
        public bool IsPickable => target is IIgi2Door { IsPickable: true };

        /// <inheritdoc/>
        /// <remarks>
        /// The view is the human's own -- its right level, its forward the way it looks, its up square to both -- and the reach is
        /// another human's, any angle: the thing's own gate is what is asked for here -- its front, its width, for a fence the way it
        /// is faced, for a wire that it is ahead -- not how well it is aimed at.
        /// </remarks>
        public bool Offer(Vector3d eye, Vector3d look)
        {
            Vector3d centre = target.Centre;
            double length = look.Length;
            Vector3d forward = length > 0.0 ? look / length : new Vector3d(0.0, 1.0, 0.0);
            double flat = Math.Sqrt((forward.X * forward.X) + (forward.Y * forward.Y));
            Vector3d right = flat > 1e-6 ? new Vector3d(forward.Y / flat, -forward.X / flat, 0.0) : new Vector3d(1.0, 0.0, 0.0);
            Vector3d up = Vector3d.Cross(right, forward);

            // Its columns the right, forward and up axes, as the view's are.
            Igi2EvaluateActivate context = new(
                eye,
                [
                    (float)right.X, (float)forward.X, (float)up.X,
                    (float)right.Y, (float)forward.Y, (float)up.Y,
                    (float)right.Z, (float)forward.Z, (float)up.Z,
                ],
                isPlayer: false);
            target.EvaluateActivate(context);
            if (!ReferenceEquals(context.Best, target))
            {
                return false;
            }

            if (place is null)
            {
                Anchor = context.BestPosition ?? centre;
            }

            return true;
        }

        /// <inheritdoc/>
        public void Press()
        {
            (target as IIgi2Pressable)?.Press(0);
            Pressed?.Invoke();
        }

        /// <summary>Gets what else a press does: a mounted gun is got on.</summary>
        public Func<bool>? Pressed { get; init; }

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
        public void Cancel()
        {
            (target as IIgi2Cabinet)?.SnapDrawerClosed();

            // A wire is let go of as its rider leaves it (Wire_Release).
            (target as IIgi2Wire)?.Release();
        }
    }
}
