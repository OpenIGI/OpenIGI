namespace OpenIGI.Game.Igi2.Vehicles;

/// <summary>A train as a co-op server has it: how far along its rail, how fast, and how hard it pulls.</summary>
/// <param name="Distance">Its distance along the spline.</param>
/// <param name="Speed">Its speed, world units a tick.</param>
/// <param name="Thrust">Its thrust.</param>
public readonly record struct Igi2TrainLeg(float Distance, float Speed, float Thrust);

/// <content>A co-op client's copy of the train: running on the rail where the server's runs, killing nobody of itself.</content>
public sealed partial class Igi2TrainTask
{
    /// <summary>
    /// Gets or sets a value indicating whether it is a co-op client's copy of the server's: its program does not drive it, nor its
    /// kill boxes kill, but it runs where the server's runs (<see cref="Hold"/>).
    /// </summary>
    public bool IsPuppet { get; set; }

    /// <summary>Gets the train as it is now, as a server sends it.</summary>
    /// <returns>Its run.</returns>
    public Igi2TrainLeg Leg() => new(_position, _speed, _realThrust);

    /// <summary>Runs it where the server's runs, on a co-op client, its carriages behind it.</summary>
    /// <param name="leg">The server's train.</param>
    /// <param name="leadTicks">The ticks this client's own player runs ahead of the snapshot: the train it may ride is that far on.</param>
    /// <param name="flowTick">The level's tick, which keeps where it stood as the tick began.</param>
    public void Hold(in Igi2TrainLeg leg, int leadTicks, int flowTick)
    {
        foreach (Igi2TrainTask car in _rake)
        {
            car.IsPuppet = true;
        }

        _speed = leg.Speed;
        _realThrust = leg.Thrust;
        _idealThrust = leg.Thrust;
        _position = leg.Distance + ((FlipDirection ? -leg.Speed : leg.Speed) * Math.Max(0, leadTicks));
        Move(flowTick);
    }
}
