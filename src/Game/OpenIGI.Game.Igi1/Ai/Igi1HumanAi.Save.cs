using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Saves;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>
/// What a save game keeps of a soldier's mind. Not retail: Project I.G.I. kept no save games. As I.G.I. 2 does
/// (<c>HumanAI_DeSerializeHandler</c>), it keeps where the soldier stands, whether it is alerted and the patrol it was on, and
/// lets everything it was in the middle of -- what it had seen and heard, its actions, its fight -- start afresh.
/// </content>
public sealed partial class Igi1HumanAi : IExchangedState
{
    // The patrol a save had it on, taken up at that command when its script next starts it on that path.
    private (int Path, int Index)? _resumePatrol;

    /// <inheritdoc/>
    void IExchangedState.Exchange(StateExchange exchange)
    {
        Vector3d position = exchange.Position(Position);
        float facing = exchange.Real(Facing);
        bool dead = exchange.Bool(Activity == AiSoldierActivity.Dead);
        bool alerted = exchange.Bool(Alerted);
        // The patrol it is on, or the one a save has it take up once its script starts it.
        int path = exchange.Int(Route?.Path.Id ?? _resumePatrol?.Path ?? -1);
        int command = exchange.Int(Route?.CurrentIndex ?? _resumePatrol?.Index ?? AiPatrolRoute.NoCommand);
        bool clipEnded = exchange.Bool(_deathClipEnded);
        int stay = exchange.Int(_bodyStayTicks);
        bool left = exchange.Bool(HasLeftWorld);
        bool released = exchange.Bool(_deadClaimsReleased);
        if (!exchange.IsReading)
        {
            return;
        }

        _occupancy?.Release(this);
        if (dead)
        {
            // Its death is over: nothing is cried, dropped or told again. A mind made dead before it ever ran is not created.
            Kill(default, facing);
            Position = position;
            _created = true;
            _deathClipEnded = clipEnded;
            _bodyStayTicks = stay;
            HasLeftWorld = left;
            _deadClaimsReleased = released;
            return;
        }

        Position = position;
        Facing = facing;
        CurrentNode = NearestNode(position);
        DestinationNode = -1;
        _route.Clear();
        _goToNode = -1;
        _groundLegSet = false;
        Alerted = alerted;
        _resumePatrol = path >= 0 && command >= 0 ? (path, command) : null;

        // A mind already running takes its patrol up now, or stands off it as it stood; one not yet created does as its
        // script starts it.
        if (_created && _resumePatrol is not null)
        {
            BeginPatrol(path);
        }
        else if (_created)
        {
            Route = null;
        }
    }
}
