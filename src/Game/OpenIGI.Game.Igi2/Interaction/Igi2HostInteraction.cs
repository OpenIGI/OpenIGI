using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// Another game's door or switch, as I.G.I. 2's player finds and uses what it may activate (<see cref="IIgi2Activatable"/>): the
/// engine's own interaction (<see cref="WorldInteraction"/>), which that game's player uses alike.
/// </summary>
/// <remarks>
/// <para>
/// A switch is pressed, a door that is not locked opens or shuts, and the task each stands for is told it was used, which that
/// game's alarms and scripts read, as its own player using it tells them. A locked door that game lets its player pick is picked
/// as I.G.I. 2's player picks its own, for as long as that game's picking takes, and is then that game's picked lock: open, and
/// open to that game's scripts for good.
/// </para>
/// <para>
/// A terminal, a timed action and a spot to plant a charge are worked as I.G.I. 2's player works its own terminal and timed actions:
/// held for as long as that game's work takes, each tick of it that game's use, and its end that game's finish. A charge so planted
/// stands where it was worked from. A cabinet is searched as I.G.I. 2's are, for as long as that game's search takes, and is then
/// that game's searched cabinet; its drawer is that game's to show.
/// </para>
/// <para>
/// A fence is climbed over as I.G.I. 2's are, across that game's fence, while it is not electrified. A wire is strung between that
/// game's two ends, as I.G.I. 2 strings its own, and is held, climbed along or slid down as that game's slope says; in use while it
/// is held.
/// </para>
/// </remarks>
public abstract class Igi2HostInteraction : IIgi2Activatable
{
    /// <summary>How far round the spot it is used from the player finds it: two metres.</summary>
    public const float FindRadius = 8192f;

    private protected Igi2HostInteraction(WorldInteraction interaction, Action<int>? activated)
    {
        Interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        Activated = activated;
    }

    /// <summary>Gets the interaction.</summary>
    public WorldInteraction Interaction { get; }

    /// <summary>Gets what is told of the task used, by its id.</summary>
    protected Action<int>? Activated { get; }

    /// <inheritdoc/>
    public virtual Vector3d Centre => Interaction.Anchor;

    /// <inheritdoc/>
    public virtual float Radius => FindRadius;

    /// <inheritdoc/>
    public abstract Igi2ActivateKind Kind { get; }

    /// <summary>The clip I.G.I. 2's player works a host's timed action with: its code pad's.</summary>
    public const string CodePadAnimation = "tba_hack_code_pad";

    /// <summary>The clip I.G.I. 2's player plants a host's charge with: its padlock's kneel.</summary>
    public const string PlantAnimation = "pick_lock";

    /// <summary>Makes the activatable a door or a switch is, or <see langword="null"/> for what I.G.I. 2's player cannot use.</summary>
    /// <param name="interaction">The interaction.</param>
    /// <param name="activated">What is told of the task used, by its id.</param>
    /// <returns>The activatable.</returns>
    public static Igi2HostInteraction? For(WorldInteraction interaction, Action<int>? activated)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        return interaction.Type switch
        {
            InteractionActivateType.Door when interaction.Door is not null => new HostDoor(interaction, activated),
            InteractionActivateType.Switch => new HostSwitch(interaction, activated),
            InteractionActivateType.Terminal => new HostWork(interaction, activated, Igi2ActivateKind.Terminal, string.Empty),
            InteractionActivateType.Generic => new HostWork(interaction, activated, Igi2ActivateKind.GenericTba, CodePadAnimation),
            InteractionActivateType.PlaceExplosive => new HostWork(interaction, activated, Igi2ActivateKind.GenericTba, PlantAnimation),
            InteractionActivateType.Cabinet => new HostCabinet(interaction, activated),
            InteractionActivateType.Fence => new HostFence(interaction, activated),
            InteractionActivateType.Wire when WireTransform.TryCreate(interaction.Anchor, interaction.End, null, out WireTransform cable)
                => new HostWire(interaction, activated, cable),
            _ => null,
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Found as that game's player finds it (<see cref="WorldInteractionRegistry.Resolve(Vector3d, float, float)"/>): while its task
    /// is in the world and it is to be had, only from the side that game offers it from (<see cref="WorldInteraction.CanOfferFrom"/>),
    /// so not through the wall behind it, and then from its nearest spot to the view, and a door from its own spot too, in front of
    /// the view and within reach.
    /// </remarks>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Vector3d view = context.ViewPosition;
        if (!Interaction.IsInWorld || !Interaction.IsAvailable || !Interaction.CanOfferFrom(view))
        {
            return;
        }

        context.Resolve(Interaction.CandidatePoint(view), this);
        if (Interaction.Type == InteractionActivateType.Door)
        {
            context.Resolve(Interaction.Anchor, this);
        }
    }

    // The interaction's frame, row by row.
    private protected IReadOnlyList<float> Rows()
    {
        Matrix3f m = Interaction.Orientation;
        return [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
    }

    private sealed class HostWork(WorldInteraction interaction, Action<int>? activated, Igi2ActivateKind kind, string animation)
        : Igi2HostInteraction(interaction, activated), IIgi2TimedAction
    {
        public override Igi2ActivateKind Kind => kind;

        public string ActivateAnimation => animation;

        public float ActivateTime => Interaction.DurationTicks / 30f;

        public string ActivateSound => kind == Igi2ActivateKind.Terminal ? Igi2Terminal.DefaultActivateSound : "tba_codepad";

        public Vector3d MagicVertex => Interaction.Anchor;

        public IReadOnlyList<float> CurrentOrientation => Rows();

        public void Activate(int activateTime) => Interaction.Use();

        public void Finish()
        {
            if (Interaction.Type == InteractionActivateType.PlaceExplosive)
            {
                Interaction.PlaceTask(Interaction.Anchor, Interaction.Facing);
            }

            Interaction.CompleteUse();
            Activated?.Invoke(Interaction.Task.Id);
        }
    }

    private sealed class HostCabinet(WorldInteraction interaction, Action<int>? activated)
        : Igi2HostInteraction(interaction, activated), IIgi2Cabinet
    {
        public override Igi2ActivateKind Kind => Igi2ActivateKind.Cabinet;

        public IReadOnlyList<float> Frame => Rows();

        public Vector3d SearchVertex => Interaction.Anchor;

        public float SearchTime => Interaction.DurationTicks / 30f;

        public void Activate(int activateTime) => Interaction.Use();

        public void OpenDrawer()
        {
        }

        public void CloseDrawer()
        {
        }

        public void SnapDrawerClosed()
        {
        }

        public void Searched()
        {
            Interaction.CompleteUse();
            Activated?.Invoke(Interaction.Task.Id);
        }
    }

    private sealed class HostFence(WorldInteraction interaction, Action<int>? activated)
        : Igi2HostInteraction(interaction, activated), IIgi2Fence
    {
        public override Igi2ActivateKind Kind => Igi2ActivateKind.Fence;

        // Found along its whole length, as Project I.G.I. scores its model's bounds: its anchor is the task's origin, at one end of
        // a fence eight metres long, so a sphere round that alone finds only the end of it.
        public override Vector3d Centre => Interaction.HasLocalBounds
            ? Interaction.Anchor + Interaction.Orientation.Transposed().Transform((Interaction.LocalBoundsMinimum + Interaction.LocalBoundsMaximum) * 0.5)
            : Interaction.Anchor;

        public override float Radius => Interaction.HasLocalBounds
            ? (float)((Interaction.LocalBoundsMaximum - Interaction.LocalBoundsMinimum).Length * 0.5) + FindRadius
            : FindRadius;

        public IReadOnlyList<float> Frame => Rows();

        public Vector3d Position => Interaction.TaskPosition;

        public void Activate(int activateTime)
        {
            if (activateTime == 0)
            {
                Interaction.Use();
                Activated?.Invoke(Interaction.Task.Id);
            }
        }
    }

    private sealed class HostWire : Igi2HostInteraction, IIgi2Wire
    {
        private readonly float[] _frame;
        private readonly double _half;

        public HostWire(WorldInteraction interaction, Action<int>? activated, WireTransform cable)
            : base(interaction, activated)
        {
            Matrix3f o = cable.Orientation;
            _frame = [o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33];
            _half = (interaction.End - interaction.Anchor).Length * 0.5;
        }

        public override Igi2ActivateKind Kind => Igi2ActivateKind.Wire;

        public IReadOnlyList<float> Frame => _frame;

        public bool IsSliding => Math.Abs(Igi2Pose.Angles(_frame).Alpha) > Igi2WireTask.SlideTilt;

        private Vector3d Midpoint => (Interaction.Anchor + Interaction.End) * 0.5;

        public Vector3f LocalMagicVertex(int index) => new(0f, (float)(index == 0 ? -_half : _half), 0f);

        // As I.G.I. 2's own wire reckons them (Igi2WireTask.Local and World). That game's places are far out, where a human's place,
        // kept to a float, is rounded to some units: a place just put on the wire reads back as the place put, so one put at its
        // end's margin is not taken for past it.
        public (double X, double Y, double Z) Local(Vector3d point)
        {
            if (_put is { } put && (point - put.World).Length <= PutTolerance)
            {
                return put.Local;
            }

            float[] m = _frame;
            Vector3d p = Midpoint;
            Fpu dx = (Fpu)point.X - p.X, dy = (Fpu)point.Y - p.Y, dz = (Fpu)point.Z - p.Z;
            return (
                ((((Fpu)m[6] * dz) + ((Fpu)m[3] * dy)) + ((Fpu)m[0] * dx)).Value,
                ((((Fpu)m[7] * dz) + ((Fpu)m[4] * dy)) + ((Fpu)m[1] * dx)).Value,
                ((((Fpu)m[8] * dz) + ((Fpu)m[5] * dy)) + ((Fpu)m[2] * dx)).Value);
        }

        public Vector3d World(Vector3d local)
        {
            float[] m = _frame;
            Vector3d p = Midpoint;
            Vector3d world = new(
                (((((Fpu)m[2] * local.Z) + ((Fpu)m[0] * local.X)) + ((Fpu)m[1] * local.Y)) + p.X).Value,
                (((((Fpu)m[5] * local.Z) + ((Fpu)m[3] * local.X)) + ((Fpu)m[4] * local.Y)) + p.Y).Value,
                (((((Fpu)m[8] * local.Z) + ((Fpu)m[6] * local.X)) + ((Fpu)m[7] * local.Y)) + p.Z).Value);
            _put = (world, (local.X, local.Y, local.Z));
            return world;
        }

        // How far a float's rounding takes a place this far out, with room: 64 units, a sixtieth of a metre.
        private const double PutTolerance = 64.0;

        private (Vector3d World, (double X, double Y, double Z) Local)? _put;

        public void Activate(int activateTime)
        {
            Interaction.Use();
            if (activateTime == 0)
            {
                Activated?.Invoke(Interaction.Task.Id);
            }
        }

        public void Release() => Interaction.CancelUse();
    }

    private sealed class HostSwitch(WorldInteraction interaction, Action<int>? activated) : Igi2HostInteraction(interaction, activated), IIgi2Pressable
    {
        public override Igi2ActivateKind Kind => Igi2ActivateKind.Switch;

        public void Press(int clientId)
        {
            Interaction.Press();
            Activated?.Invoke(Interaction.Task.Id);
        }
    }

    private sealed class HostDoor(WorldInteraction interaction, Action<int>? activated) : Igi2HostInteraction(interaction, activated), IIgi2Door
    {
        public override Igi2ActivateKind Kind => Igi2ActivateKind.Door;

        public bool IsLocked => Interaction.IsLocked;

        // The pick that finished this activation, which opens the door as that game's finished pick does.
        private bool _pickedNow;

        public bool IsPickable => Interaction.IsPickable && Interaction.IsLocked;

        public bool IsPicked
        {
            get => Interaction.IsPicked;
            set
            {
                if (value && !Interaction.IsPicked)
                {
                    Interaction.CompleteUse();
                    _pickedNow = true;
                }
            }
        }

        public int Status => Interaction.Door?.UseState ?? IAiDoor.Closed;

        public float PickLockTime => Interaction.DurationTicks / 30f;

        public string ActivateSound => string.Empty;

        public Vector3d MagicVertex => Interaction.Anchor;

        public IReadOnlyList<float> CurrentOrientation => Rows();

        public void ToggleOpen(Vector3d from)
        {
            // A pick just finished opened the door itself (WorldInteraction.CompleteUse): it is not toggled again.
            if (_pickedNow)
            {
                _pickedNow = false;
            }
            else
            {
                Interaction.Door?.Toggle();
            }

            Activated?.Invoke(Interaction.Task.Id);
        }
    }
}
