using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Saves;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <content>Its save state, <c>Train_ptSerializeParameterList</c>: a model object's, then its thrusts, its speed and its place on its rails.</content>
public sealed partial class Igi2TrainTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Igi2ModelObjState.Write(writer, _at, _orientation);
        writer.Real(_realThrust);
        writer.Real(_idealThrust);
        writer.Real(_speed);
        writer.Real(_position);
    }

    /// <inheritdoc/>
    /// <remarks>A locomotive is put back on its rails where it was, its carriages with it.</remarks>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2ModelObjState.Read(reader);
        _realThrust = reader.Real();
        _idealThrust = reader.Real();
        _speed = reader.Real();
        _position = reader.Real();
        if (!IsCarriage)
        {
            Move(int.MinValue);
        }
    }
}

/// <content>
/// Its save state, <c>Car_ptSerializeParameterList</c>: a model object's, then its velocity and its direction, whether it has blown up,
/// and its thrusts.
/// </content>
public sealed partial class Igi2CarTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Igi2ModelObjState.Write(writer, CurrentPosition, CurrentOrientation, _isExploded == 0);
        writer.Real3(_body?.Body.Velocity ?? default);
        writer.Real3(_body?.Body.VelocityDirection ?? default);
        writer.Bool(_isExploded != 0);
        writer.Real(RealThrust);
        writer.Real(IdealThrust);
    }

    /// <inheritdoc/>
    /// <remarks><c>Car_DeSerializeHandler</c>: its speed approximated from its velocity again; a car that had blown up is out of the world.</remarks>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        (Vector3d position, float[] orientation, _) = Igi2ModelObjState.Read(reader);
        Vector3f velocity = reader.Real3();
        reader.Real3();
        bool exploded = reader.Bool();
        RealThrust = reader.Real();
        IdealThrust = reader.Real();
        if (_body is { } body)
        {
            body.Position = position;
            Array.Copy(orientation, body.Orientation, 9);
            body.ResetLast();
            body.Body.Velocity = velocity;
            body.Body.UpdateSpeedApproximation();
            _lastPosition = position;
            _lastOrientation = [.. orientation];
            if (_solids.Count > 0 && Models is { } models)
            {
                Collision.Igi2CollisionWorld.Repose(_solids, models, Model, Igi2Pose.FromMatrix(position, Rows(orientation)), Magic);
            }
        }

        if (exploded && _isExploded == 0)
        {
            Remove();
            _isExploded = 1;
        }
    }
}

/// <content>
/// Its save state, <c>CarAI_ptSerializeParameterList</c>: its route's command and its last end, by level id, whether its driver is dead
/// and its damage, its tick, its way over its graph, and the speed its route last set.
/// </content>
public sealed partial class Igi2CarAiTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(_patrol.Current?.Id ?? -1);
        writer.Int(_patrol.LastEnd?.Id ?? -1);
        writer.Bool(_isDead != 0);
        writer.Real(0f);
        writer.Int(_time);
        writer.Int(_pathIndex);
        writer.Int(_startNode);
        writer.Int(_edgeNode0);
        writer.Int(_edgeNode1);
        writer.Int(_destNode);
        writer.Real(_commandSpeed);
    }

    /// <inheritdoc/>
    /// <remarks>Its route is started where it was; its search is made again as it next drives.</remarks>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int current = reader.Int();
        int lastEnd = reader.Int();
        _isDead = reader.Bool() ? (byte)1 : (byte)0;
        reader.Real();
        _time = reader.Int();
        _pathIndex = reader.Int();
        _startNode = reader.Int();
        _edgeNode0 = reader.Int();
        _edgeNode1 = reader.Int();
        _destNode = reader.Int();
        _commandSpeed = reader.Real();
        if (RouteId != -1 && World?.PatrolPath(RouteId) is { } path)
        {
            _patrol.Restore(path, current, lastEnd);
            _isRouteStarted = true;
        }
    }
}
