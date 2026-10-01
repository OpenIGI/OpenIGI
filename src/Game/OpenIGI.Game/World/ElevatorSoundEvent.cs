namespace OpenIGI.Game.World;

/// <summary>The sound transitions emitted by an authored elevator.</summary>
public enum ElevatorSoundEvent
{
    /// <summary>Native <c>isStart</c> (+0x5D7): one-shot start definition.</summary>
    Started,

    /// <summary>Native <c>isMoving</c> (+0x5D6) rose: looping travel definition.</summary>
    Traveling,

    /// <summary>Native <c>isStop</c> (+0x5D8): one-shot stop definition.</summary>
    Stopped,
}
