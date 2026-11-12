using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <content>Each type's own update, as its task's run handler calls it.</content>
public sealed partial class WorldInteraction
{
    /// <summary>The task's update, where its pulses clear on their stamps and its expressions are read.</summary>
    /// <param name="now">The level's tick.</param>
    /// <remarks>
    /// <para>
    /// Each type's own update, as retail runs it: a switch (<c>0x4450F0</c>) reads its On expression, then, two ticks past its
    /// press, shifts <c>isPressed</c> into <c>isLastPressed</c> and clears it, so <c>isLastPressed</c> holds for the one update
    /// that clears it; a terminal (<c>0x4438E0</c>) and a GenericTBA (<c>0x4480B0</c>) read their On expression, are off while
    /// exploded, and clear <c>isInUse</c> and the finished-this-tick byte two ticks past their stamps; a cabinet
    /// (<c>0x444590</c>) clears <c>isInUse</c> likewise; a fence (<c>0x43FC30</c>) is electrified as its expression says, or
    /// not at all. Every <c>isLast*</c> takes its value in the same update, after the clear.
    /// </para>
    /// <para>
    /// An expression slot that is empty or did not compile leaves its byte as it was, so a script's write to it stands; a door's
    /// expressions are its own update's (<see cref="PlacedDoor.Conditions"/>).
    /// </para>
    /// </remarks>
    public void Run(int now)
    {
        switch (Type)
        {
            case InteractionActivateType.Switch:
                RunSwitch(now);
                break;
            case InteractionActivateType.Terminal:
            case InteractionActivateType.Generic:
            case InteractionActivateType.PlaceExplosive:
                RunTimed(now);
                break;
            case InteractionActivateType.Cabinet:
                RunCabinet(now);
                break;
            case InteractionActivateType.Fence:
                RunFence();
                break;
            default:
                RunHeld();
                break;
        }
    }

    /// <summary>A switch's update (<c>0x4450F0</c>): its On expression, its press pulse, and the model and click a change brings.</summary>
    /// <param name="now">The level's tick.</param>
    public void RunSwitch(int now)
    {
        TakeOffset();
        if ((MirroredOn ?? _onCondition?.Invoke()) is bool switchedOn)
        {
            IsOn = switchedOn;
        }

        if (now > _pressStamp + 2)
        {
            Pressed.EndPulse();
        }

        // On a change of its on, pressed or destroyed byte it wears one of its five models, and the first, on and
        // at rest, comes with its click: the "switch" definition where it stands.
        if (IsOn != LastOn || Pressed.Value != Pressed.Last || IsDestroyed != LastDestroyed)
        {
            ModelSlot = IsDestroyed ? 4 : IsOn ? (Pressed.Value ? 1 : 0) : (Pressed.Value ? 3 : 2);
            if (ModelSlot == 0)
            {
                Sound(InteractionSound.Played, SwitchSound, TaskPosition + _offset);
            }
        }

        LastOn = IsOn;
        LastDestroyed = IsDestroyed;
    }

    /// <summary>
    /// A terminal's update (<c>0x4438E0</c>) and a timed action's (<c>0x4480B0</c>): its On expression, then its in-use and
    /// finished-this-tick bytes cleared two ticks past their stamps.
    /// </summary>
    /// <param name="now">The level's tick.</param>
    public void RunTimed(int now)
    {
        TakeOffset();
        if ((MirroredOn ?? _onCondition?.Invoke()) is bool on)
        {
            IsOn = on;
        }

        ClearUse(now);
        if (now > _completeStamp + 2)
        {
            CompletedThisTick = false;
        }
    }

    /// <summary>A cabinet's update (<c>0x444590</c>): its in-use byte cleared two ticks past its stamp.</summary>
    /// <param name="now">The level's tick.</param>
    public void RunCabinet(int now)
    {
        TakeOffset();
        ClearUse(now);
    }

    /// <summary>A fence's update (<c>0x43FC30</c>): electrified as its expression says, or not at all, and humming while it is.</summary>
    public void RunFence()
    {
        TakeOffset();
        IsElectrified = MirroredElectrified ?? _electricCondition?.Invoke() ?? false;
        RunFenceHum();
        InUse.CommitTick();
    }

    /// <summary>The update of a thing that is only held while used -- a wire, a mounted gun: its in-use byte's last value.</summary>
    public void RunHeld()
    {
        TakeOffset();
        InUse.CommitTick();
    }

    // Where the task stands this tick, when something carries it.
    private void TakeOffset() => _offset = _dynamicOffset?.Invoke() ?? default;

    // 0x43FC30: a powered fence makes its own AmbientArea, buzzing elfence_loop in a box two metres up its middle, the
    // length of the span between its magic vertices 0 and 1 and a metre and a half past each end; it deletes it with the power.
    private void RunFenceHum()
    {
        if (_humming && !IsElectrified)
        {
            _humming = false;
            Sound(InteractionSound.AreaStopped, string.Empty, default);
        }
        else if (!_humming && IsElectrified && FenceSpan is (float first, float second))
        {
            _humming = true;
            double middle = ((double)second + first) * 0.5;
            Vector3f offset = Orientation.Transposed().Transform(new Vector3f((float)middle, 0f, 8192f));
            Sounds?.Invoke(new InteractionSoundEvent(
                Task,
                InteractionSound.AreaStarted,
                FenceSound,
                TaskPosition + new Vector3d(offset.X, offset.Y, offset.Z),
                Orientation,
                new Vector3d((((double)second - first) * 0.00012207031) + 1.5, 1.75, 2.0)));
        }
    }

    private void Sound(InteractionSound what, string sound, Vector3d at) =>
        Sounds?.Invoke(new InteractionSoundEvent(Task, what, sound, at, Matrix3f.Identity, default));

    private void ClearUse(int now)
    {
        if (now > _useStamp + 2)
        {
            InUse.Value = false;
        }

        InUse.CommitTick();
    }
}
