using OpenIGI.Game.Igi2.Interaction;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>The doors, each told what the server's was told.</content>
public sealed partial class Igi2ClientWorld
{
    // Snapshots a door may stand otherwise than the server's before the server's is taken: about a second, long enough for this
    // client's own use of a door to reach the server and come back, so the door doesn't flick shut and open again.
    private const int DoorPatience = 30;

    private Igi2DoorTask[]? _doors;
    private int[] _doorDisagreement = [];
    private byte[]? _serverDoors;

    // Each door told as the server's is, swinging here on its own as it does there. Taken when the server's command changes,
    // which is how a door the host opened opens here; and taken when this client's has stood otherwise for DoorPatience
    // snapshots, which settles one this client opened that the server didn't.
    private void ApplyDoors(byte[] bits)
    {
        _doors ??= Igi2DoorCommands.Of(_level);
        if (_doorDisagreement.Length != _doors.Length)
        {
            _doorDisagreement = new int[_doors.Length];
        }

        for (int i = 0; i < _doors.Length; i++)
        {
            if (Igi2DoorCommands.Read(bits, i) is not { } told)
            {
                break;
            }

            Igi2DoorTask door = _doors[i];
            bool changed = _serverDoors is null || Igi2DoorCommands.Read(_serverDoors, i) != told;
            _doorDisagreement[i] = door.IsCommandedOpen == told.Open ? 0 : _doorDisagreement[i] + 1;
            if (changed || _doorDisagreement[i] > DoorPatience)
            {
                door.Command(told.Open, told.SwingsBack);
                _doorDisagreement[i] = 0;
            }
        }

        _serverDoors = bits;
    }
}
