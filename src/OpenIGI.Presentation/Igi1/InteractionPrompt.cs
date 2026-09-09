using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;
using OpenIGI.UI.Animation;
using System.Globalization;

namespace OpenIGI.Presentation.Igi1;

/// <summary>How the player completes an offered interaction.</summary>
public enum InteractionPromptMethod
{
    /// <summary>One press does it — a switch, an unlocked door, a ladder, a fence, a wire, a gun.</summary>
    Press,

    /// <summary>Use has to stay down for the whole authored duration; releasing it cancels.</summary>
    Hold,
}

/// <summary>
/// What the interaction resolver is currently offering the player, in the terms the prompt needs.
/// </summary>
/// <remarks>
/// <para>
/// A flat description rather than the live <see cref="WorldInteraction"/> so the prompt's decisions —
/// its wording, its method, its ring — are pure functions of data a test can write down. The camera
/// builds one of these from whatever it resolved this tick; nothing here can reach the simulation.
/// </para>
/// <para>
/// <paramref name="AnchorUnits"/> is in world units (4096 per metre), the same space the interaction
/// registry works in, because that is what the camera has to hand. The renderer converts once.
/// </para>
/// </remarks>
/// <param name="Id">A stable identity for this offer, so the marker can pop when the target changes.</param>
/// <param name="Type">The resolved <c>GetActivate</c> type.</param>
/// <param name="ActionSprite">The retail action sprite the task names, which is what the title reads first.</param>
/// <param name="AnchorUnits">Where the marker is pinned, in world units.</param>
/// <param name="DurationTicks">The authored hold duration in 30 Hz ticks, or zero for an instant action.</param>
/// <param name="Locked">Whether a door is locked and therefore offers its lock rather than its handle.</param>
/// <param name="DoorOpen">Whether the door this offers is already open, so the verb is "close".</param>
/// <param name="Progress">The running interaction's progress in <c>[0,1]</c>, or <see langword="null"/> when idle.</param>
public readonly record struct InteractionPromptTarget(
    int Id,
    InteractionActivateType Type,
    string? ActionSprite,
    Vector3d AnchorUnits,
    int DurationTicks,
    bool Locked,
    bool DoorOpen,
    float? Progress);
/// <summary>
/// The wording, method and geometry of the remaster's world-anchored interaction prompt.
/// </summary>
/// <remarks>
/// Pure decisions only, exactly as <see cref="RemasteredHudPresentation"/> is: the marker's placement
/// is a projection of a world point, its wording is a function of the resolved task, and both are
/// testable without a device. The drawing itself lives on <see cref="IRemasteredHudPresenter"/>.
/// </remarks>
public static class InteractionPromptPresentation
{
    /// <summary>The simulation's fixed tick rate, for turning an authored duration into seconds.</summary>
    public const float TicksPerSecond = 30f;

    /// <summary>Whether the modern world-anchored prompt owns the interaction callout.</summary>
    /// <remarks>
    /// It is part of the remaster HUD, so it can only be on while that HUD is: the retail presentation
    /// draws the original's action sprites and must stay exactly as shipped.
    /// </remarks>
    public static bool UsesWorldPrompt(bool remasteredHudEnabled, bool interactionPromptEnabled) =>
        remasteredHudEnabled && interactionPromptEnabled;

    /// <summary>Decides whether the offer is a press or a held action.</summary>
    /// <remarks>
    /// The held states are exactly the ones the retail tick cancels on release (<c>picklock_loop</c>,
    /// the cabinet search, <c>typecomp_loop</c>, the generic state and the explosive placement). A
    /// fence climb is committed once it starts and a wire is ridden, so both read as a press.
    /// </remarks>
    public static InteractionPromptMethod Method(in InteractionPromptTarget target)
    {
        if (target.DurationTicks <= 0)
        {
            return InteractionPromptMethod.Press;
        }

        return target.Type switch
        {
            InteractionActivateType.Door => target.Locked ? InteractionPromptMethod.Hold : InteractionPromptMethod.Press,
            InteractionActivateType.Cabinet or InteractionActivateType.Terminal
                or InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive =>
                    InteractionPromptMethod.Hold,
            _ => InteractionPromptMethod.Press,
        };
    }

    /// <summary>Names the action in the words a player reads, not the ones the level data uses.</summary>
    /// <remarks>
    /// The door is decided first because its verb depends on live state — locked offers the lock, and
    /// an open door offers to close. Everything else asks the task's own action sprite before falling
    /// back to the activation type, since the sprite is what the level author chose for it.
    /// </remarks>
    public static string Title(in InteractionPromptTarget target)
    {
        if (target.Type == InteractionActivateType.Door)
        {
            return target.Locked ? "Pick Lock" : target.DoorOpen ? "Close Door" : "Open Door";
        }

        return target.ActionSprite switch
        {
            "a_ladder.spr" => "Climb Ladder",
            "a_wire.spr" => "Ride Zipline",
            "a_fence.spr" => "Climb Fence",
            "action_pressbutton.spr" => "Use Switch",
            "action_comput.spr" => "Use Computer",
            "action_searchcab.spr" => "Search Cabinet",
            "a_gun.spr" or "stationary.spr" => "Mount Gun",

            // Port-added, and the reason the sprite is consulted before the type: the original has no
            // drivable vehicle, so nothing in the retail activation range says "drive".
            "a_vehicle.spr" => "Drive",
            "weapon.spr" => "Take Weapon",
            "note.spr" => "Read Note",
            "anya.spr" => "Talk",
            _ => TitleForType(target.Type),
        };
    }

    /// <summary>Builds the qualifier line under the title, or an empty string when there is nothing to add.</summary>
    /// <remarks>
    /// Two tags at most, and only when they tell the player something the title cannot: that the thing
    /// is locked, and that use has to stay down. A held action also carries its length, because the
    /// difference between a one-second search and a nine-second lock pick decides whether the player
    /// starts it with a patrol in view.
    /// </remarks>
    public static string Detail(in InteractionPromptTarget target)
    {
        List<string> tags = [];
        if (target.Locked)
        {
            tags.Add("Locked");
        }

        if (Method(target) == InteractionPromptMethod.Hold)
        {
            float seconds = target.DurationTicks / TicksPerSecond;
            tags.Add(seconds >= 0.95f
                ? string.Create(CultureInfo.InvariantCulture, $"Hold {seconds:0}s")
                : "Hold");
        }

        // Sentence case, and a middot with single spaces around it. Letterspaced capitals are the HUD's
        // idiom for a fixed readout, but at the size this line is drawn they separate into characters
        // and the middot stops reading as a separator at all.
        return string.Join(" · ", tags);
    }

    /// <summary>Whether the badge draws a progress ring, and what it reads.</summary>
    /// <remarks>
    /// The same states the retail status handler gives a bar to, so the ring appears exactly when the
    /// original's progress display would: the timed states, never a fence, a wire or a mounted gun.
    /// </remarks>
    public static float? RingProgress(in InteractionPromptTarget target) =>
        target.Progress is { } progress ? Math.Clamp(progress, 0f, 1f) : null;

    /// <summary>Compacts a bound control's display name into something that fits inside the badge.</summary>
    /// <remarks>
    /// The badge shows the key the player actually uses, so a rebound control is never mislabelled.
    /// Long names are shortened the way every key cap is — the modifier names have conventional short
    /// forms, and anything unrecognised is cut rather than allowed to run out of the badge.
    /// </remarks>
    public static string BadgeLabel(string? boundKeyName)
    {
        if (string.IsNullOrWhiteSpace(boundKeyName))
        {
            return "USE";
        }

        string name = boundKeyName.Trim();
        if (name.StartsWith("Mouse Button ", StringComparison.OrdinalIgnoreCase))
        {
            return "M" + name["Mouse Button ".Length..];
        }

        string upper = name.ToUpperInvariant();
        string compact = upper switch
        {
            "SPACE" or "SPACEBAR" => "SPACE",
            "RETURN" or "ENTER" or "NUMPADENTER" => "ENTER",
            "LEFTCONTROL" or "RIGHTCONTROL" or "CONTROL" => "CTRL",
            "LEFTSHIFT" or "RIGHTSHIFT" or "SHIFT" => "SHIFT",
            "LEFTALT" or "RIGHTALT" or "ALT" => "ALT",
            "BACKSPACE" => "BKSP",
            "ESCAPE" => "ESC",
            "TAB" => "TAB",
            _ => upper,
        };

        if (compact.StartsWith("NUMPAD", StringComparison.Ordinal) && compact.Length > "NUMPAD".Length)
        {
            compact = "N" + compact["NUMPAD".Length..];
        }

        return compact.Length <= 5 ? compact : compact[..5];
    }

    /// <summary>Whether a badge label needs the wider key-cap shape rather than the round one.</summary>
    public static bool IsWideBadge(string badgeLabel) =>
        !string.IsNullOrEmpty(badgeLabel) && badgeLabel.Length > 2;

    private static string TitleForType(InteractionActivateType type) => type switch
    {
        InteractionActivateType.Switch => "Use Switch",
        InteractionActivateType.Cabinet => "Search Cabinet",
        InteractionActivateType.Terminal => "Use Computer",
        InteractionActivateType.StationaryGun or InteractionActivateType.MountedGunTransition => "Mount Gun",
        InteractionActivateType.Wire => "Ride Zipline",
        InteractionActivateType.Ladder => "Climb Ladder",
        InteractionActivateType.Fence => "Climb Fence",
        InteractionActivateType.PlaceExplosive => "Plant Explosive",
        _ => "Interact",
    };
}

/// <summary>
/// The prompt's own wall clock: which offer is on screen, how far it has faded up, and how far its
/// entry pop has run.
/// </summary>
/// <remarks>
/// <para>
/// State the renderer must not own, for the reason <c>BinocularOptics</c> does not: the fade is
/// measured in real seconds so it runs at the same rate at any frame rate, and it has to survive the
/// frames when nothing is offered at all — otherwise the marker vanishes the instant the player's aim
/// leaves the door instead of falling away from it.
/// </para>
/// <para>
/// The last offer is retained while it fades out, so the marker fades where it stood rather than
/// jumping to the screen centre or blinking off.
/// </para>
/// </remarks>
internal sealed class InteractionPromptAnimation
{
    /// <summary>How long the marker takes to reach full opacity.</summary>
    public const float FadeInSeconds = 0.11f;

    /// <summary>How long the marker takes to fade away once nothing is offered.</summary>
    public const float FadeOutSeconds = 0.08f;

    /// <summary>How long the entry pop takes to settle.</summary>
    public const float PopSeconds = 0.22f;

    /// <summary>The largest step the clock accepts, so a hitch cannot skip the animation.</summary>
    private const float MaxStepSeconds = 0.1f;

    private InteractionPromptTarget? _target;
    private float _opacity;
    private float _pop;

    /// <summary>Gets the offer being drawn, which outlives the live one by exactly its fade.</summary>
    public InteractionPromptTarget? Visible => _opacity > 0f ? _target : null;

    /// <summary>Gets the fade, in <c>[0,1]</c>.</summary>
    public float Opacity => _opacity;

    /// <summary>Gets the eased entry scale, in <c>[0,1]</c>.</summary>
    public float Pop => Easing.Evaluate(Ease.OutBack, _pop);

    /// <summary>Advances the clock with this frame's real elapsed time.</summary>
    /// <param name="target">What the resolver offers now, or <see langword="null"/> for nothing.</param>
    /// <param name="elapsedSeconds">Real wall-clock seconds since the previous frame.</param>
    public void Update(InteractionPromptTarget? target, float elapsedSeconds)
    {
        float step = Math.Clamp(elapsedSeconds, 0f, MaxStepSeconds);
        if (target is { } offered)
        {
            // A different task under the aim is a new marker, not the same one moved: restart the pop
            // so the badge lands on the new object instead of sliding across the screen to it.
            if (_target is not { } shown || shown.Id != offered.Id)
            {
                _pop = 0f;
            }

            _target = offered;
            _opacity = MathF.Min(1f, _opacity + (step / FadeInSeconds));
            _pop = MathF.Min(1f, _pop + (step / PopSeconds));
            return;
        }

        _opacity = MathF.Max(0f, _opacity - (step / FadeOutSeconds));
        if (_opacity <= 0f)
        {
            _target = null;
            _pop = 0f;
        }
    }

    /// <summary>Drops the marker outright, for a mission change or a cut that must not fade.</summary>
    public void Reset()
    {
        _target = null;
        _opacity = 0f;
        _pop = 0f;
    }
}
