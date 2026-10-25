using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.World;

/// <summary>Runs the level's CarAI tasks: each decides its car's steering, throttle and brake and sends them as a packet.</summary>
/// <remarks>
/// CarAI is a normal level task, not a cutscene animation (<c>0x425DE0</c>). It follows its PatrolPath over the
/// graph's route table, or chases what its car's vision holds, and its packet reaches the car through the same
/// receiver an AnimTask's does (<see cref="CutsceneVehicleBodies.DriveCar"/>).
/// </remarks>
internal sealed class CutsceneCarAi(
    CutsceneContext context,
    LevelUpdateWalker walker,
    CutsceneVehicleBodies bodies,
    CutsceneVehicleArmament armament,
    List<CarAiState> carAis)
{
    /// <summary>CarAI's arrival radius squared (<c>dbl_533648</c>: three metres).</summary>
    private const double CarAiArrivalSquared = 150_994_944d;

    /// <summary>Within ten metres of a node the route speed looks one node further (<c>0x4255A0</c>).</summary>
    private const double CarAiLookAheadSquared = 1_677_721_600d;

    /// <summary>The chase looks ahead within fourteen metres (<c>0x425900</c>).</summary>
    private const double CarAiChaseLookAheadSquared = 3_288_334_336d;

    private readonly CutsceneContext _context = context;
    private readonly LevelUpdateWalker _walker = walker;
    private readonly CutsceneVehicleBodies _bodies = bodies;
    private readonly CutsceneVehicleArmament _armament = armament;
    private readonly List<CarAiState> _carAis = carAis;

    /// <summary>Supplies the loaded graph corpus to authored vehicle patrol tasks.</summary>
    /// <remarks>
    /// CarAI is a normal level task, not a cutscene animation. Retail's callback at 0x425DE0 starts
    /// its PatrolPath, uses the graph route table to select each next node, and applies its vehicle-only
    /// speed command on the fixed game tick. The desktop loader owns graph-file I/O, so it passes the
    /// already offset world graphs here after they have parsed.
    /// </remarks>
    public void ConfigureCarAi(IReadOnlyDictionary<int, GraphFile> graphs)
    {
        ArgumentNullException.ThrowIfNull(graphs);
        _carAis.Clear();
        AiPatrolPathRegistry paths = AiPatrolPathRegistry.Build(_context.Root);
        foreach (GameTask task in _context.Root.Descendants())
        {
            if (task is not DeclaredTask declared
                || !string.Equals(task.Type.Name, "CarAI", StringComparison.Ordinal)
                || !declared.TryGetValue("Car ID", out object? rawCarId)
                || !declared.TryGetValue("Graph ID", out object? rawGraphId)
                || !declared.TryGetValue("Route ID", out object? rawRouteId)
                || rawCarId is not int carId
                || rawGraphId is not int graphId
                || rawRouteId is not int routeId
                || graphId < 0
                || routeId < 0
                || _context.Root.Tree.FromId(carId) is not DeclaredTask car
                || !_context.TaskTransforms.TryGetValue(car, out CutsceneTransformState? transform)
                || !graphs.TryGetValue(graphId, out GraphFile? graph)
                || paths.Find(routeId) is not { } path)
            {
                continue;
            }

            CarAiState state = new(declared, car, transform, graph, AiPatrolRoute.Start(path));
            _carAis.Add(state);
            _walker.Bind(declared, _ => TickCarAi(state));
        }
    }

    /// <summary>Gets the graph node a car's CarAI is heading for (<c>+104</c>), for diagnostics and tests.</summary>
    public int? CarAiTargetNode(GameTask car) =>
        _carAis.Find(state => ReferenceEquals(state.Car, car))?.TargetNode?.Id;

    /// <summary>Runs retail's registered CarAI callback (<c>0x425DE0</c>) once per game tick.</summary>
    /// <remarks>
    /// <para>
    /// CarAI does not move its car. It decides steering, throttle and brake and sends them to the car
    /// as a channel packet (<c>0x426510</c>: ch0 steering, ch1 throttle, ch3 brake), which the car's own
    /// receiver (<c>0x42EA10</c>) turns into physics -- the same path an AnimTask-driven car takes. The
    /// car therefore pulls away as its smoothed throttle rises: probed on Mission 7, the checkpoint APC
    /// crawls at 1-4 km/h for six seconds and reaches its cruise near 35 km/h after eleven.
    /// </para>
    /// <para>
    /// While the car's vision holds a target (<c>0x426000</c>: retained 600 ticks) CarAI leaves its
    /// route and drives at 20 km/h towards the graph node nearest the target, stopping within three
    /// metres of it and turning in place to face the target (<c>0x4262F0</c>).
    /// </para>
    /// </remarks>
    private void TickCarAi(CarAiState state)
    {
        Vector3d? target = _armament.VehicleCombatTarget?.Invoke();
        {
            if (_context.Destructibles?.IsDestroyed(state.Car) == true)
            {
                state.Stopped = true;
                return;
            }

            if (!ConditionalContainerGate.IsTaskActive(state.Task) || state.Route is null)
            {
                return;
            }

            if (ChaseTarget(state, target) is { } chased)
            {
                GraphNode? goal = NearestNode(state.Graph, chased);
                if (goal is null || CarAiArrived(state, goal))
                {
                    state.CommandSpeedKmh = 0f;
                    state.Halt = 1f;
                }
                else
                {
                    state.TargetNode = CarAiNextHop(state, state.Transform.Position, goal.Id);
                    state.CommandSpeedKmh = 20f;
                    state.Halt = 0f;
                }

                SteerCarAiChase(state, chased);
            }
            else if (!state.Stopped && state.Route.Current is { } command)
            {
                switch (command.Kind)
                {
                    case AiPatrolCommandKind.WalkTo:
                    case AiPatrolCommandKind.RunTo:
                        if (state.Graph.NodeAt(command.Operand) is not { } destination || CarAiArrived(state, destination))
                        {
                            state.Route.Advance();
                        }
                        else
                        {
                            state.TargetNode = CarAiNextHop(state, state.Transform.Position, destination.Id);
                            state.CommandSpeedKmh = state.RouteSpeedKmh;
                            state.Halt = 0f;
                            SteerCarAiRoute(state);
                        }

                        break;

                    case AiPatrolCommandKind.Delay:
                    {
                        // 0x425DE0 counts the wait at +124 and moves on once it has passed the operand.
                        state.CommandSpeedKmh = 0f;
                        state.Halt = 1f;
                        int waited = state.WaitTicks++;
                        if (waited > command.Operand)
                        {
                            state.Route.Advance();
                            state.WaitTicks = 0;
                        }

                        break;
                    }

                    case AiPatrolCommandKind.Quit:
                    case AiPatrolCommandKind.End:
                        // Arms 6 and 7 stop the car and pull its brake; the cursor stays where it is.
                        state.CommandSpeedKmh = 0f;
                        state.Halt = 1f;
                        state.Brake = 1f;
                        state.DesiredSpeedKmh = 0f;
                        state.Steering = 0f;
                        break;

                    case AiPatrolCommandKind.SetSpeed:
                        state.RouteSpeedKmh = command.Operand;
                        state.Halt = 0f;
                        state.Route.Advance();
                        break;

                    default:
                        // Retail's default arm logs "Command not supported by CARAI" and advances.
                        state.Route.Advance();
                        break;
                }
            }
            else
            {
                state.Halt = 1f;
                state.Brake = 1f;
                state.Steering = 0f;
            }

            // 0x426510: the packet goes out every tick; its first word is the call counter, and a
            // zero there is the receiver's set-up packet rather than a physics step.
            if (state.Packets++ == 0)
            {
                state.Transform.Thrust = state.Transform.OriginalThrust;
                state.Transform.TargetThrust = state.Transform.OriginalThrust;
                state.Transform.CarMotion.Reset();
                _bodies.ResetRetailBody(state.Car, state.Transform);
                return;
            }

            _bodies.DriveCar(
                state.Car,
                state.Transform,
                simulationOrder: 0,
                state.Steering,
                state.Throttle,
                state.Brake == 0f ? -1f : state.Brake);

            // The car missiles, bullets and the player collide with is the same placed body: keep it
            // where the car is, or shots pass through the visible vehicle and never reach its
            // direct-hit destruction.
            _context.CollisionIndex?.UpdateTransform(
                state.Car,
                state.Transform.Position / WorldScene.UnitsPerMetre,
                state.Transform.Orientation);
            _context.Destructibles?.UpdatePosition(state.Car, state.Transform.Position, state.Transform.Orientation);
        }
    }

    /// <summary>What the car's vision holds, retained for 600 ticks (<c>0x426000</c>), or nothing.</summary>
    private Vector3d? ChaseTarget(CarAiState state, Vector3d? target)
    {
        // A car whose "Can fire" is off has no vision and chases nothing; a retained target that died is dropped.
        if (!_armament.TryGetVision(state.Car, out VehicleVision? vision) || !vision.CanFire || target is not { } seen)
        {
            state.ChaseHeld = false;
            state.ChaseRetention = 0;
            return null;
        }

        // 0x426000 takes the first slot the vision marks visible and holds it for 600 ticks, counting down while
        // nothing is visible; CarAI reads the held human's live position (+32) every tick.
        if (vision.Sees)
        {
            state.ChaseHeld = true;
            state.ChaseRetention = AiVehicleCombat.TargetRetentionTicks;
        }
        else if (state.ChaseRetention > 0)
        {
            state.ChaseRetention--;
        }
        else
        {
            state.ChaseHeld = false;
        }

        return state.ChaseHeld ? seen : null;
    }

    /// <summary>Retail's "π" (<c>0x533618</c>): three ulps above <see cref="Math.PI"/>.</summary>
    private static readonly double RetailPi = BitConverter.Int64BitsToDouble(0x400921FB54442D1B);

    /// <summary>Retail's "1/π" (<c>0x533610</c>): three ulps below <c>1 / Math.PI</c>.</summary>
    private static readonly double RetailInversePi = BitConverter.Int64BitsToDouble(0x3FD45F306DC9C880);

    /// <summary>170° as the chase eases by it (<c>0x533630</c>).</summary>
    private static readonly double Retail170Degrees = BitConverter.Int64BitsToDouble(0x4007BC89CEEED7C6);

    /// <summary>1/170° (<c>0x533628</c>).</summary>
    private static readonly double RetailInverse170Degrees = BitConverter.Int64BitsToDouble(0x3FD591F70B6D4D3F);

    /// <summary>10°, the chase's turn threshold (<c>0x533638</c>).</summary>
    private static readonly double Retail10Degrees = BitConverter.Int64BitsToDouble(0x3FC6571855555555);

    /// <summary>1/(10 m)², the final-stop ramp (<c>0x533600</c>).</summary>
    private const double CarAiRampScale = 5.960464477539063e-10;

    /// <summary>Whether the car is within three metres of a node (<c>0x4260C0</c>: 150994944 squared units).</summary>
    private static bool CarAiArrived(CarAiState state, GraphNode node)
    {
        Vector3d p = state.Transform.Position;
        X87 dx = (X87)node.Position.X - p.X, dy = (X87)node.Position.Y - p.Y, dz = (X87)node.Position.Z - p.Z;
        return (dx * dx + dy * dy + dz * dz).Value < CarAiArrivalSquared;
    }

    /// <summary>The next node from a point towards a destination (<c>0x425C10</c>).</summary>
    /// <remarks>
    /// When the route table has no link, retail logs "CarAI: Error in graph" and keeps the nearest node; a
    /// step to an empty slot is returned as the empty slot.
    /// </remarks>
    private static GraphNode? CarAiNextHop(CarAiState state, Vector3d from, int destinationId)
    {
        GraphNode? nearest = NearestNode(state.Graph, from);
        if (nearest is null || nearest.Id == destinationId)
        {
            return nearest;
        }

        return state.Graph.RouteTable?.TryGetStep(nearest.Id, destinationId, out int step, out _) == true
            ? state.Graph.NodeAt(step)
            : nearest;
    }

    /// <summary>
    /// <c>0x425B80</c>: from a point, the next position toward a destination -- the destination's own when the
    /// point is nearest it, the route step's otherwise, and the point unchanged when there is no link.
    /// </summary>
    private static Vector3d CarAiResolveToward(CarAiState state, Vector3d from, int destinationId)
    {
        GraphNode? nearest = NearestNode(state.Graph, from);
        if (nearest is null)
        {
            return from;
        }

        if (nearest.Id == destinationId)
        {
            return nearest.Position;
        }

        return state.Graph.RouteTable?.TryGetStep(nearest.Id, destinationId, out int step, out _) == true
            && state.Graph.NodeAt(step) is { } next
            ? next.Position
            : from;
    }

    private static GraphNode? NearestNode(GraphFile graph, Vector3d at)
    {
        GraphNearestNode.Find(graph, at, out GraphNode? nearest, out _);
        return nearest;
    }

    /// <summary>
    /// The heading of a world offset in the car's frame (<c>0x426140</c>'s rotation, then <c>0x414950</c>),
    /// signed positive to the left, kept at full width as retail keeps it in a register.
    /// </summary>
    /// <param name="state">The CarAI.</param>
    /// <param name="dx">The offset's X, already rounded as the caller's subtraction rounds it.</param>
    /// <param name="dy">The offset's Y.</param>
    /// <param name="dz">The offset's Z.</param>
    private static double CarAiHeading(CarAiState state, double dx, double dy, double dz)
    {
        // Mᵀ·d with the z term first (M6, M3, M0 for x; M7, M4, M1 for y), each stored as a double.
        Matrix3f m = state.Transform.Orientation;
        double lx = ((X87)m.M31 * dz + (X87)m.M21 * dy + (X87)m.M11 * dx).Value;
        double ly = ((X87)m.M32 * dz + (X87)m.M22 * dy + (X87)m.M12 * dx).Value;

        // 0x414950: z is cleared, the offset normalised by a reciprocal, its cosine to +Y through acos.
        if (lx == 0d && ly == 0d)
        {
            return 0d;
        }

        X87 inverse = (X87)1.0 / X87.Sqrt((X87)lx * lx + (X87)ly * ly + (X87)0.0 * 0.0);
        double nx = (inverse * lx).Value;
        double ny = (inverse * ly).Value;
        ny = ny < -1d ? -1d : ny > 1d ? 1d : ny;
        double heading = RetailAcos(ny);
        return nx > 0d ? -heading : heading;
    }

    /// <summary>The heading to a point, from the car (<c>d = point − P</c>, rounded toward zero).</summary>
    private static double CarAiHeadingTo(CarAiState state, Vector3d point)
    {
        Vector3d p = state.Transform.Position;
        return CarAiHeading(
            state,
            X87.Add(point.X, -p.X),
            X87.Add(point.Y, -p.Y),
            X87.Add(point.Z, -p.Z));
    }

    /// <summary>
    /// The CRT's <c>acos</c> (<c>0x516810</c>): it switches to round-to-nearest, takes
    /// <c>√((1 + c)(1 − c))</c> as a plain double and hands both to <c>fpatan</c>.
    /// </summary>
    /// <remarks><c>fpatan</c> keeps a 64-bit significand; <see cref="Math.Atan2(double, double)"/> is within a double ulp of it.</remarks>
    private static double RetailAcos(double c) =>
        c >= 1d ? 0d
        : c <= -1d ? Math.PI
        : Math.Atan2(Math.Sqrt((1d + c) * (1d - c)), c);

    /// <summary>Steering from a heading (<c>0x426140</c>, <c>0x4262F0</c>): the positive limit tests the full-width value, the rest the float.</summary>
    private static float CarAiSteering(double heading)
    {
        float hf = X87.ToFloat(heading);
        return heading > 0.17453294f ? -1f
            : hf < -0.17453294f ? 1f
            : ((X87)hf * -1.9098593f).F;
    }

    /// <summary>Route steering, throttle and brake (<c>0x426140</c>).</summary>
    private static void SteerCarAiRoute(CarAiState state)
    {
        state.DesiredSpeedKmh = X87.ToFloat(CarAiRouteSpeed(state));
        CarAiThrottle(state);
        double heading = state.TargetNode is { } node ? CarAiHeadingTo(state, node.Position) : 0d;

        // The wheel eases off as the throttle opens: full lock only at a standstill.
        state.Steering = (((((X87)1f - state.Throttle) * 0.95f) + 0.05f) * CarAiSteering(heading)).F;
    }

    /// <summary>Chase steering, throttle and brake (<c>0x4262F0</c>).</summary>
    private static void SteerCarAiChase(CarAiState state, Vector3d chased)
    {
        bool stopped = state.CommandSpeedKmh == 0f;
        Vector3d toward = !stopped && state.TargetNode is { } node ? node.Position : chased;
        double heading = CarAiHeadingTo(state, toward);
        state.Steering = CarAiSteering(heading);
        float magnitude = MathF.Abs(X87.ToFloat(heading));
        state.DesiredSpeedKmh = !stopped
            ? X87.ToFloat(CarAiChaseSpeed(state, chased))
            : magnitude > 0.17453294f ? 5f : ((X87)magnitude * 28.647888f).F;
        CarAiThrottle(state);

        // A stopped car that still has to turn keeps its brake off so it can crawl round.
        state.Brake = stopped && magnitude > 0.17453294f ? 0f : state.Halt;
    }

    /// <summary>Opens or closes the throttle towards the desired speed (<c>0x426260</c>).</summary>
    /// <remarks>
    /// The car's speed is read in world units a tick and converted to km/h (× 108/4096). The throttle
    /// moves one hundredth of the shortfall from the car's own smoothed throttle -- all in one register,
    /// truncated once -- and the brake comes on when told to halt or when the car is more than 5 km/h too fast.
    /// </remarks>
    private static void CarAiThrottle(CarAiState state)
    {
        X87 shortfall = (X87)state.DesiredSpeedKmh - ((X87)state.Transform.Speed * 0.0263671875f);
        X87 throttle = (X87)state.Transform.Thrust + (shortfall * 0.0099999998f);
        state.Throttle = throttle.Value < 0.0f ? 0f : throttle.Value > 1.0f ? 1f : throttle.F;
        state.Brake = state.Halt != 0f || shortfall.Value < -5.0 ? 1f : 0f;
    }

    /// <summary>The route speed, eased for the heading and for the turn at the node (<c>0x4255A0</c>).</summary>
    /// <remarks>
    /// Beyond ten metres of the current node the speed falls with the heading error to it. Within ten
    /// metres: mid-leg the look-ahead resolves back to the current node, so there is no turn; at a leg's last
    /// node the turn is the angle at the node between the way back to the car and the way on to the next
    /// leg's destination, so carrying straight on slows to about 5 km/h; before a Quit or End the speed runs
    /// down with the square of the distance. This is what the original computes, dead look-ahead included.
    /// </remarks>
    private static double CarAiRouteSpeed(CarAiState state)
    {
        float command = state.CommandSpeedKmh;
        if (state.TargetNode is not { } node)
        {
            return command;
        }

        Vector3d p = state.Transform.Position;
        Vector3d n = node.Position;
        double ax = X87.Add(p.X, -n.X), ay = X87.Add(p.Y, -n.Y), az = X87.Add(p.Z, -n.Z);
        double distanceSquared = ((X87)ax * ax + (X87)ay * ay + (X87)az * az).Value;
        if (distanceSquared > CarAiLookAheadSquared)
        {
            return CarAiEase(command, Math.Abs(CarAiHeading(state, -ax, -ay, -az)));
        }

        if (state.Route?.Current is not { } current || state.Graph.NodeAt(current.Operand) is not { } destination)
        {
            return command;
        }

        double turn;
        if (destination.Id != node.Id)
        {
            GraphNode? hop = NearestNode(state.Graph, destination.Position) is { } nearest
                ? CarAiNextHop(state, p, nearest.Id)
                : null;
            if (hop is null)
            {
                return command;
            }

            turn = CarAiAngleBetween(ax, ay, az, CarAiResolveToward(state, hop.Position, hop.Id), n);
        }
        else if (state.Route.Next is not { } next)
        {
            return command;
        }
        else if (next.Kind is AiPatrolCommandKind.WalkTo or AiPatrolCommandKind.RunTo)
        {
            if (state.Graph.NodeAt(next.Operand) is not { } following)
            {
                return command;
            }

            turn = CarAiAngleBetween(ax, ay, az, CarAiResolveToward(state, following.Position, following.Id), n);
        }
        else if (next.Kind is AiPatrolCommandKind.Quit or AiPatrolCommandKind.End)
        {
            return (((X87)command * distanceSquared) * CarAiRampScale).Value;
        }
        else
        {
            return command;
        }

        return command < 5f ? command : CarAiEase(command, turn);
    }

    /// <summary>The chase speed towards the node nearest the target (<c>0x425900</c>).</summary>
    private static double CarAiChaseSpeed(CarAiState state, Vector3d chased)
    {
        float command = state.CommandSpeedKmh;
        if (state.TargetNode is not { } node)
        {
            return command;
        }

        Vector3d p = state.Transform.Position;
        Vector3d n = node.Position;
        double ax = X87.Add(p.X, -n.X), ay = X87.Add(p.Y, -n.Y), az = X87.Add(p.Z, -n.Z);
        double distanceSquared = ((X87)ax * ax + (X87)ay * ay + (X87)az * az).Value;
        if (distanceSquared > CarAiChaseLookAheadSquared)
        {
            return CarAiEase(command, Math.Abs(CarAiHeading(state, -ax, -ay, -az)));
        }

        if (NearestNode(state.Graph, chased) is not { } goal)
        {
            return command;
        }

        if (goal.Id == node.Id)
        {
            return (((X87)command * distanceSquared) * CarAiRampScale).Value;
        }

        // Straight from +104 to the goal, with no nearest-node step; no link steers at the goal itself.
        Vector3d after = state.Graph.RouteTable?.TryGetStep(node.Id, goal.Id, out int step, out _) == true
            && state.Graph.NodeAt(step) is { } stepNode
            ? stepNode.Position
            : goal.Position;
        double turn = CarAiAngleBetween(ax, ay, az, after, n);
        if (command < 5f)
        {
            return command;
        }

        return turn < Retail10Degrees
            ? ((((X87)command - 5f) * ((X87)Retail170Degrees - turn)) * RetailInverse170Degrees + 5.0).Value
            : 5f;
    }

    /// <summary>The eased speed: 5 km/h at a full reversal, the command (less an ulp: retail's π) straight on.</summary>
    private static double CarAiEase(float command, double turn) =>
        ((((X87)command - 5f) * ((X87)RetailPi - turn)) * RetailInversePi + 5.0).Value;

    /// <summary>
    /// The angle at a node between the way back to the car (<c>P − N</c>, given) and the way on to
    /// <paramref name="beyond"/> (<c>0x4B31F0</c>, both vectors rounded toward zero).
    /// </summary>
    private static double CarAiAngleBetween(double ax, double ay, double az, Vector3d beyond, Vector3d node)
    {
        double bx = X87.Add(beyond.X, -node.X), by = X87.Add(beyond.Y, -node.Y), bz = X87.Add(beyond.Z, -node.Z);
        if ((ax == 0d && ay == 0d && az == 0d) || (bx == 0d && by == 0d && bz == 0d))
        {
            return 0d;
        }

        double dot = ((X87)az * bz + (X87)ay * by + (X87)ax * bx).Value;
        double la = X87.Sqrt((X87)ax * ax + (X87)ay * ay + (X87)az * az).Value;
        double c = ((X87)dot / (X87.Sqrt((X87)bx * bx + (X87)by * by + (X87)bz * bz) * la)).Value;
        if (!(c < 1d))
        {
            return 0d;
        }

        return c <= -1d ? RetailPi : RetailAcos(c);
    }
}
