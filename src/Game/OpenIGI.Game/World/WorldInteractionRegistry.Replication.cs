using OpenIGI.Core.Numerics;
using OpenIGI.Net;

namespace OpenIGI.Game.World;

/// <summary>
/// The level's switches, terminals, TBAs, cabinets, locks, fences, generators and radios as a co-op server has them, and a
/// client's taken from it.
/// </summary>
/// <remarks>
/// A client's level evaluates its tasks' expressions against its own variables, which are not the server's: a switch the host
/// threw, a generator the host's charge cut, a terminal a partner hacked would stand as they were here. The server sends what
/// each one is; a client's update takes the power and the electric current from it in place of its own expressions -- so its
/// own update still sees the change, and swaps the model and plays the sound as the server's did -- and takes each one-way
/// latch, a hack, a search, a picked lock, a plant, once.
/// </remarks>
public sealed partial class WorldInteractionRegistry
{
    // How long after this client pressed a switch the server's word of a press of it is taken for that same press: a second.
    private const int OwnPressEchoTicks = 30;

    private readonly BitWriter _state = new(256);
    private bool[] _serverPressed = [];

    /// <summary>Gets or sets whether the tasks take what they are from a co-op server's (<see cref="ApplyState"/>): a client's.</summary>
    public bool Mirrored { get; set; }

    /// <summary>Gets how many of the tasks' latches were taken from the server's word: a hack, a search, a pick, a plant, a press.</summary>
    public int MirroredChanges { get; private set; }

    /// <summary>Writes what every task is, for a co-op server's clients.</summary>
    /// <returns>The state, or <see langword="null"/> for a level with no tasks at all.</returns>
    public byte[]? CaptureState()
    {
        if (_items.Count == 0 && _generators.Count == 0 && _radios.Count == 0)
        {
            return null;
        }

        _state.Reset();
        _state.WriteVarUInt((uint)_items.Count);
        foreach (WorldInteraction item in _items)
        {
            _state.WriteBool(item.IsOn);
            _state.WriteBool(item.IsElectrified);
            _state.WriteBool(item.IsCompleted);
            _state.WriteBool(item.IsSearched);
            _state.WriteBool(item.IsPicked);
            _state.WriteBool(item.Pressed.Value);
            _state.WriteBool(item.PlacedPosition is not null);
            if (item.PlacedPosition is { } placed)
            {
                _state.WriteVarInt((int)Math.Round(placed.X));
                _state.WriteVarInt((int)Math.Round(placed.Y));
                _state.WriteVarInt((int)Math.Round(placed.Z));
                _state.WriteBits(Quantize.AngleToBits(item.PlacedFacing), 16);
            }
        }

        _state.WriteVarUInt((uint)_generators.Count);
        foreach (GeneratorPowerState generator in _generators)
        {
            _state.WriteBool(generator.IsOn);
        }

        _state.WriteVarUInt((uint)_radios.Count);
        foreach (RadioRuntime radio in _radios)
        {
            _state.WriteBool(radio.IsOn);
        }

        return _state.AsSpan().ToArray();
    }

    /// <summary>Takes what a co-op server's tasks are, as <see cref="CaptureState"/> wrote it.</summary>
    /// <param name="state">The state; nothing is taken from one that does not match this level.</param>
    public void ApplyState(byte[]? state)
    {
        if (!Mirrored || state is null)
        {
            return;
        }

        BitReader reader = new(state);
        if (reader.ReadVarUInt() != (uint)_items.Count)
        {
            return;
        }

        if (_serverPressed.Length != _items.Count)
        {
            _serverPressed = new bool[_items.Count];
        }

        for (int i = 0; i < _items.Count && !reader.IsOverflowed; i++)
        {
            WorldInteraction item = _items[i];
            bool on = reader.ReadBool();
            bool electrified = reader.ReadBool();
            bool completed = reader.ReadBool();
            bool searched = reader.ReadBool();
            bool picked = reader.ReadBool();
            bool pressed = reader.ReadBool();
            Vector3d? placed = null;
            float facing = 0f;
            if (reader.ReadBool())
            {
                placed = new Vector3d(reader.ReadVarInt(), reader.ReadVarInt(), reader.ReadVarInt());
                facing = Quantize.AngleFromBits((ushort)reader.ReadBits(16));
            }

            if (reader.IsOverflowed)
            {
                return;
            }

            item.MirroredOn = on;
            if (item.Type == InteractionActivateType.Fence)
            {
                item.MirroredElectrified = electrified;
            }

            // A press the server's switch took: pulsed here once, as its rises -- unless it is this client's own press come back,
            // which it pressed, and clicked, itself a moment ago (its pulse is three ticks; the round trip is longer).
            if (pressed && !_serverPressed[i] && !item.Pressed.Value && item.Type == InteractionActivateType.Switch
                && !item.PressedHereWithin(OwnPressEchoTicks))
            {
                item.PressAsServerDid();
                MirroredChanges++;
            }

            _serverPressed[i] = pressed;
            bool latched = item.Type switch
            {
                InteractionActivateType.Cabinet => searched && !item.IsSearched,
                InteractionActivateType.Door => picked && !item.IsPicked,
                InteractionActivateType.Terminal or InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive
                    => completed && !item.IsCompleted,
                _ => false,
            };
            if (latched)
            {
                item.FinishAsServerDid();
                MirroredChanges++;
            }

            if (placed is { } contact && (item.PlacedPosition is not { } here || Vector3d.Distance(here, contact) > 1.0))
            {
                item.PlaceTask(contact, facing);
                MirroredChanges++;
            }
        }

        if (reader.ReadVarUInt() == (uint)_generators.Count)
        {
            foreach (GeneratorPowerState generator in _generators)
            {
                generator.MirroredOn = reader.ReadBool();
            }

            if (reader.ReadVarUInt() == (uint)_radios.Count)
            {
                foreach (RadioRuntime radio in _radios)
                {
                    radio.MirroredOn = reader.ReadBool();
                }
            }
        }
    }
}
