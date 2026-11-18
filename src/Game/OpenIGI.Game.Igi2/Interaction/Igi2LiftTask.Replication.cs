using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>A lift as a co-op server has it: the floor it stands at or left, the floor it travels to, where its car is and how fast it goes.</summary>
/// <param name="Floor">The floor it stands at, or the one it left, counting its floors from its first.</param>
/// <param name="Target">The floor it travels to, or -1 when it stands.</param>
/// <param name="Position">Where its car is, in world units.</param>
/// <param name="UnitsPerTick">How far it moves a tick, in world units.</param>
public readonly record struct Igi2LiftLeg(int Floor, int Target, Vector3d Position, float UnitsPerTick);

/// <content>The lift's travel as a co-op server shares it, and a client's copy kept to it.</content>
public sealed partial class Igi2LiftTask
{
    // How far a client's car may stray from where the server's leads it, as a share of the way, before it is put there.
    private const float StrayShare = 0.02f;

    /// <summary>Gets the lift's travel now, as a server sends it.</summary>
    /// <returns>The leg.</returns>
    public Igi2LiftLeg Leg()
    {
        Igi2FloorTask[] floors = [.. Floors()];
        int to = _activeFloor is null ? -1 : Array.IndexOf(floors, _activeFloor);
        if (_along >= 1f || to < 0)
        {
            return new Igi2LiftLeg(Math.Max(to, 0), -1, _position, 0f);
        }

        int from = Array.FindIndex(floors, f => f.Position == _startPosition);
        return new Igi2LiftLeg(from < 0 ? to : from, to, _position, (float)(_speed * Vector3d.Distance(_startPosition, _stopPosition)));
    }

    /// <summary>
    /// Keeps a co-op client's copy of the lift to the server's: on the server's leg, and where the server's car will be once this
    /// client's own player has run the ticks it is ahead -- the car its player rides is the one it predicts on.
    /// </summary>
    /// <param name="leg">The server's leg.</param>
    /// <param name="leadTicks">The ticks this client's own player runs ahead of the snapshot.</param>
    /// <remarks>
    /// A car already near enough is left to run on: put back every snapshot it would jerk under its riders. Its own floor scripts
    /// still run here, on the server's variables, and set off as the server's do; one that set off alone is put back.
    /// </remarks>
    public void Follow(in Igi2LiftLeg leg, int leadTicks)
    {
        Igi2FloorTask[] floors = [.. Floors()];
        if ((uint)leg.Floor >= (uint)floors.Length)
        {
            return;
        }

        Igi2FloorTask from = floors[leg.Floor];
        if (leg.Target < 0 || leg.Target >= floors.Length)
        {
            if (!ReferenceEquals(_activeFloor, from) || _along < 1f)
            {
                SetLeg(from, from, 1f);
                _speed = 0f;
                _floorCache = leg.Floor;
                _currentFloor = leg.Floor;
            }

            return;
        }

        Igi2FloorTask to = floors[leg.Target];
        double length = Vector3d.Distance(from.Position, to.Position);
        if (length <= 0)
        {
            return;
        }

        float along = (float)Math.Clamp((Vector3d.Distance(from.Position, leg.Position) + (leg.UnitsPerTick * Math.Max(0, leadTicks))) / length, 0, 1);
        bool sameLeg = ReferenceEquals(_activeFloor, to) && _startPosition == from.Position;
        if (!sameLeg || Math.Abs(_along - along) > StrayShare)
        {
            SetLeg(from, to, along);
            _floorCache = leg.Target;
        }

        _speed = (float)(leg.UnitsPerTick / length);
    }

    // The car put on a leg between two floors, so far along it, and its riders' attachments with it.
    private void SetLeg(Igi2FloorTask from, Igi2FloorTask to, float along)
    {
        _startPosition = from.Position;
        _startOrientation = [.. from.Frame];
        _stopPosition = to.Position;
        _stopOrientation = [.. to.Frame];
        _activeFloor = to;
        _along = along;
        UpdatePosition();
        UpdateAttachments();
    }
}
