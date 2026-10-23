using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// Another game's door or switch, as I.G.I. 2's player finds and uses what it may activate (<see cref="IIgi2Activatable"/>): the
/// engine's own interaction (<see cref="WorldInteraction"/>), which that game's player uses alike.
/// </summary>
/// <remarks>
/// <para>
/// A switch is pressed, a door that is not locked opens or shuts, and the task each stands for is told it was used, which that
/// game's alarms and scripts read, as its own player using it tells them. A locked door is not picked: that game's lock is
/// opened by that game's player's own way of picking.
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
    public Vector3d Centre => Interaction.Anchor;

    /// <inheritdoc/>
    public float Radius => FindRadius;

    /// <inheritdoc/>
    public abstract Igi2ActivateKind Kind { get; }

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
            _ => null,
        };
    }

    /// <inheritdoc/>
    /// <remarks>Found from its spot, in front of the view and within reach, while its task is in the world.</remarks>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Interaction.IsInWorld && Interaction.IsAvailable)
        {
            context.Resolve(Interaction.Anchor, this);
        }
    }

    // The interaction's frame, row by row.
    private protected IReadOnlyList<float> Frame()
    {
        Matrix3f m = Interaction.Orientation;
        return [m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33];
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

        public bool IsPickable => false;

        public bool IsPicked { get; set; }

        public int Status => Interaction.Door?.UseState ?? IAiDoor.Closed;

        public float PickLockTime => 0f;

        public string ActivateSound => string.Empty;

        public Vector3d MagicVertex => Interaction.Anchor;

        public IReadOnlyList<float> CurrentOrientation => Frame();

        public void ToggleOpen(Vector3d from)
        {
            Interaction.Door?.Toggle();
            Activated?.Invoke(Interaction.Task.Id);
        }
    }
}
