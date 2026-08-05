using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Rendering;

#pragma warning disable CS1591 // Public data members are documented by the owning interaction contract.

namespace OpenIGI.Game.World;

/// <summary>One retail task which can answer the human player's <c>GetActivate</c> query.</summary>
public sealed class WorldInteraction
{
    private int _pressedTicks;
    private Func<bool>? _onCondition;
    private Func<bool>? _lockedCondition;
    private Func<bool>? _openCondition;
    private Func<bool>? _closeCondition;

    internal WorldInteraction(
        DeclaredTask task,
        InteractionActivateType type,
        Vector3d anchor,
        Vector3d end,
        Matrix3f orientation,
        float facing,
        int durationTicks,
        string actionSprite,
        DoorState? door)
    {
        Task = task;
        Type = type;
        Anchor = anchor;
        End = end;
        Orientation = orientation;
        Facing = facing;
        DurationTicks = durationTicks;
        ActionSprite = actionSprite;
        Door = door;
    }

    public DeclaredTask Task { get; }
    public InteractionActivateType Type { get; }
    public Vector3d Anchor { get; }
    public Vector3d End { get; }
    public Matrix3f Orientation { get; }
    public float Facing { get; }
    public int DurationTicks { get; }
    public string ActionSprite { get; }
    public DoorState? Door { get; }
    public ScriptFlag InUse { get; } = new();
    public ScriptFlag Pressed { get; } = new();
    public bool IsOn { get; set; } = true;
    public bool IsCompleted { get; private set; }
    public bool CompletedThisTick { get; private set; }
    public bool IsSearched { get; private set; }
    public bool IsPicked { get; private set; }
    public bool IsLocked { get; private set; }

    /// <summary>Whether the task still offers itself to the retail activation scan.</summary>
    public bool IsAvailable => Type switch
    {
        InteractionActivateType.Terminal or InteractionActivateType.Generic
            or InteractionActivateType.PlaceExplosive => IsOn && !IsCompleted,
        InteractionActivateType.Cabinet => !IsSearched,
        _ => true,
    };

    /// <summary>The nearest point offered to the shared activation resolver.</summary>
    public Vector3d CandidatePoint(Vector3d from)
    {
        if (Type == InteractionActivateType.Door)
        {
            // 0x446C62/0x446CCF offer magic vertex 0 and the human projected onto the door's
            // local X/Z interaction plane. The registry scores the projected point first and the
            // magic point separately, just as the original resolver receives both offers.
            Vector3d local = Orientation.Transform(from - Anchor);
            Vector3d projected = Orientation.Transposed().Transform(new Vector3d(local.X, 0.0, local.Z));
            return Anchor + projected;
        }

        if (Type != InteractionActivateType.Wire || End == Anchor)
        {
            return Anchor;
        }

        Vector3d segment = End - Anchor;
        double along = Vector3d.Dot(from - Anchor, segment) / segment.LengthSquared;
        return Vector3d.Lerp(Anchor, End, Math.Clamp(along, 0.0, 1.0));
    }

    /// <summary>Projects a mount onto a wire while preserving its retail 3072-unit end margins.</summary>
    public Vector3d WireMountPoint(Vector3d from)
    {
        Vector3d segment = End - Anchor;
        if (Type != InteractionActivateType.Wire || segment.LengthSquared == 0.0)
        {
            return Anchor;
        }
        double margin = Math.Min(0.49, 3072.0 / segment.Length);
        double along = Vector3d.Dot(from - Anchor, segment) / segment.LengthSquared;
        return Vector3d.Lerp(Anchor, End, Math.Clamp(along, margin, 1.0 - margin));
    }

    /// <summary>Gets the human task position whose animation places its hands on the cable.</summary>
    /// <remarks>
    /// IGI1 <c>sub_411F20</c> subtracts 3686.4 along column 2 of the frame built by
    /// <c>sub_43F820</c>/<c>sub_414A60</c>. That column is cable-relative up, not world Z; on a slope
    /// it deliberately contains a horizontal component.
    /// </remarks>
    public Vector3d WireBodyPoint(Vector3d from)
    {
        Vector3d segment = End - Anchor;
        if (Type != InteractionActivateType.Wire || segment.LengthSquared == 0.0)
        {
            return Anchor;
        }

        Vector3d up = WireTransform.CableUp(segment.Normalized());

        return WireMountPoint(from) - (up * 3686.4);
    }

    /// <summary>Applies the task-specific front/side gate before the shared aim resolver.</summary>
    public bool CanOfferFrom(Vector3d from)
    {
        if (Type == InteractionActivateType.Door)
        {
            Vector3d local = Orientation.Transform(from - Anchor);
            return local.X > -4096.0 && local.X < 1843.2
                && local.Z > -4096.0 && local.Z < 4096.0;
        }

        if (Type == InteractionActivateType.Wire)
        {
            return true;
        }

        Vector3d toHuman = from - Anchor;
        Vector3d forward = new(Math.Sin(Facing), Math.Cos(Facing), 0.0);
        Vector3d right = new(Math.Cos(Facing), -Math.Sin(Facing), 0.0);
        double inFront = Vector3d.Dot(toHuman, forward);
        double lateral = Math.Abs(Vector3d.Dot(toHuman, right));

        return Type switch
        {
            InteractionActivateType.Switch or InteractionActivateType.Cabinet
                or InteractionActivateType.Terminal or InteractionActivateType.Generic
                or InteractionActivateType.StationaryGun => inFront > 0.0 && lateral < 3072.0,
            InteractionActivateType.PlaceExplosive => inFront > 0.0,
            InteractionActivateType.Fence => inFront > 0.0,
            _ => true,
        };
    }

    public void Press()
    {
        Pressed.Value = true;
        _pressedTicks = 3;
    }

    public void BeginUse() => InUse.Value = true;

    public void CancelUse() => InUse.Value = false;

    public void CompleteUse()
    {
        InUse.Value = false;
        CompletedThisTick = true;
        switch (Type)
        {
            case InteractionActivateType.Cabinet:
                IsSearched = true;
                break;
            case InteractionActivateType.Door:
                IsPicked = true;
                Door?.Toggle();
                break;
            default:
                IsCompleted = true;
                break;
        }
    }

    internal void Tick()
    {
        IsOn = _onCondition?.Invoke() ?? IsOn;
        IsLocked = !IsPicked && (_lockedCondition?.Invoke() ?? false);
        // Door_RunHandler only evaluates the open expression while fully closed and the close
        // expression while fully open (0x446DF0 / 0x446E38). Evaluating close during the opening
        // channel cancels button-driven gates on their first movement tick.
        if (Door?.IsFullyClosed == true && _openCondition?.Invoke() == true)
        {
            Door?.CommandOpen();
        }
        if (Door?.IsFullyOpen == true && _closeCondition?.Invoke() == true)
        {
            Door?.CommandClosed();
        }
        CompletedThisTick = false;
        InUse.CommitTick();
        Pressed.CommitTick();
        if (_pressedTicks > 0 && --_pressedTicks == 0)
        {
            Pressed.Value = false;
        }
    }

    internal void SetConditions(
        Func<bool>? on,
        Func<bool>? locked,
        Func<bool>? open = null,
        Func<bool>? close = null)
    {
        _onCondition = on;
        _lockedCondition = locked;
        _openCondition = open;
        _closeCondition = close;
        IsOn = on?.Invoke() ?? true;
        IsLocked = locked?.Invoke() ?? false;
    }
}

/// <summary>
/// All direct retail world interactions, with the original two-metre, most-head-on resolver.
/// </summary>
public sealed class WorldInteractionRegistry
{
    /// <summary><c>Human_UpdateActivate</c>'s regular query radius: two metres.</summary>
    public const double ActivateRadius = 8192.0;

    private readonly List<WorldInteraction> _items = [];

    public IReadOnlyList<WorldInteraction> Items => _items;

    public static WorldInteractionRegistry Build(
        GameTask root,
        LoadedMission mission,
        DoorRegistry doors,
        ScriptVarRegistry variables)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(variables);

        Dictionary<string, MefModel> models = ModelsByName(mission);
        LevelExpressionRuntime expressions = new(variables);
        WorldInteractionRegistry registry = new();
        Collect(root, registry, models, doors, variables, expressions);
        return registry;
    }

    /// <summary>Advances task pulses and <c>Last</c> script shadows once at 30 Hz.</summary>
    public void Tick()
    {
        foreach (WorldInteraction item in _items)
        {
            item.Tick();
        }
    }

    /// <summary>Applies the task activation produced by an AI body's activate input channel.</summary>
    /// <param name="taskId">The level task id named by <c>AIAction_Activate</c>.</param>
    /// <returns><see langword="true"/> when the id names a direct interaction.</returns>
    public bool ActivateFromAi(int taskId)
    {
        WorldInteraction? item = _items.FirstOrDefault(candidate => candidate.Task.Id == taskId);
        if (item is null)
        {
            return false;
        }

        switch (item.Type)
        {
            case InteractionActivateType.Switch:
                item.Press();
                break;
            case InteractionActivateType.Door:
                item.Door?.Toggle();
                break;
            default:
                item.BeginUse();
                item.CompleteUse();
                break;
        }

        return true;
    }

    /// <summary>Finds the eligible task nearest the centre of the player's aim.</summary>
    public WorldInteraction? Resolve(Vector3d eye, float yaw, float pitch = 0f)
        => Resolve(eye, yaw, pitch, out _);

    /// <summary>Finds the eligible task and returns its off-axis angle for cross-family arbitration.</summary>
    public WorldInteraction? Resolve(Vector3d eye, float yaw, float pitch, out double resolvedAngle)
    {
        Vector3d look = new(
            Math.Sin(yaw) * Math.Cos(pitch),
            Math.Cos(yaw) * Math.Cos(pitch),
            Math.Sin(pitch));
        WorldInteraction? best = null;
        double bestAngle = double.MaxValue;

        foreach (WorldInteraction item in _items)
        {
            if (!item.IsAvailable || !item.CanOfferFrom(eye))
            {
                continue;
            }

            double radius = item.Type == InteractionActivateType.PlaceExplosive
                ? Math.Max(ActivateRadius, ReadReal(item.Task, "Activate radius(meters)", 2f) * WorldScene.UnitsPerMetre)
                : ActivateRadius;
            Consider(item, item.CandidatePoint(eye), radius);
            if (item.Type == InteractionActivateType.Door)
            {
                Consider(item, item.Anchor, radius);
            }
        }

        resolvedAngle = bestAngle;
        return best;

        void Consider(WorldInteraction item, Vector3d point, double radius)
        {
            Vector3d delta = point - eye;
            double distance = delta.Length;
            if (distance <= 0.0 || distance >= radius)
            {
                return;
            }

            double cosine = Vector3d.Dot(delta / distance, look);
            if (cosine <= 0.0)
            {
                return;
            }

            double angle = Math.Acos(Math.Clamp(cosine, -1.0, 1.0));
            if (angle < bestAngle)
            {
                bestAngle = angle;
                best = item;
            }
        }
    }

    private static void Collect(
        GameTask task,
        WorldInteractionRegistry registry,
        IReadOnlyDictionary<string, MefModel> models,
        DoorRegistry doors,
        ScriptVarRegistry variables,
        LevelExpressionRuntime expressions)
    {
        if (ConditionalContainerGate.IsDormant(task))
        {
            return;
        }

        if (task is DeclaredTask declared && TypeOf(task.Type.Name) is { } type)
        {
            Vector3d start = PositionOf(declared, type);
            Vector3d end = ReadPosition(declared, "Stop position", start);
            float facing = FacingOf(declared);
            Matrix3f orientation = OrientationOf(declared);
            int magicIndex = type is InteractionActivateType.Terminal
                or InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive ? 1 : 0;
            string modelName = ModelNameOf(declared, type);
            // A wire traverses the dynamic model's transformed magic-vertex line. Its Start/Stop task
            // parameters place and stretch that model, but the small local X/Z magic offset is still
            // part of the cable the hands touch. The scene builder uses this same value.
            if (type == InteractionActivateType.Wire
                && models.TryGetValue(modelName, out MefModel? wireModel)
                && WireTransform.TryCreate(declared, wireModel, out WireTransform wire))
            {
                start = wire.CableStart;
                end = wire.CableEnd;
            }

            // Fence evaluates its model bounds. It does not substitute magic vertex 0 for the task position.
            if (type is not InteractionActivateType.Wire and not InteractionActivateType.Fence
                && models.TryGetValue(modelName, out MefModel? model)
                && model.TryGetMagicVertex(magicIndex, out Vector3f local))
            {
                Vector3f offset = orientation.Transposed().Transform(local);
                start += new Vector3d(offset.X, offset.Y, offset.Z);
            }

            DoorState? door = type == InteractionActivateType.Door
                ? doors.Doors.FirstOrDefault(candidate => ReferenceEquals(candidate.Task, task))?.State
                : null;
            WorldInteraction item = new(
                declared, type, start, end, orientation, facing,
                DurationOf(declared, type), SpriteOf(declared, type), door);
            string onExpression = ReadString(declared, "On Expression", ReadString(declared, "On", string.Empty));
            string lockedExpression = ReadString(declared, "Locked expression", string.Empty);
            string openExpression = ReadString(declared, "Open door expression", string.Empty);
            string closeExpression = ReadString(declared, "Close door expression", string.Empty);
            item.SetConditions(
                type is InteractionActivateType.Terminal or InteractionActivateType.Generic
                    or InteractionActivateType.PlaceExplosive
                    ? () => expressions.EvaluateCondition(onExpression, emptyValue: true)
                    : null,
                type == InteractionActivateType.Door && ReadBool(declared, "Pickable")
                    ? () => expressions.EvaluateCondition(lockedExpression, emptyValue: false)
                    : null,
                type == InteractionActivateType.Door && !string.IsNullOrWhiteSpace(openExpression)
                    ? () => expressions.EvaluateCondition(openExpression, emptyValue: false)
                    : null,
                type == InteractionActivateType.Door && !string.IsNullOrWhiteSpace(closeExpression)
                    ? () => expressions.EvaluateCondition(closeExpression, emptyValue: false)
                    : null);
            registry._items.Add(item);
            BindVariables(item, variables);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry, models, doors, variables, expressions);
        }
    }

    private static InteractionActivateType? TypeOf(string name) => name switch
    {
        "Switch" => InteractionActivateType.Switch,
        "Door" => InteractionActivateType.Door,
        "Cabinet" => InteractionActivateType.Cabinet,
        "Terminal" => InteractionActivateType.Terminal,
        "StationaryGun" => InteractionActivateType.StationaryGun,
        "Wire" => InteractionActivateType.Wire,
        "Fence" => InteractionActivateType.Fence,
        "GenericTBA" => InteractionActivateType.Generic,
        "PlaceExplosiveTBA" => InteractionActivateType.PlaceExplosive,
        _ => null,
    };

    private static void BindVariables(WorldInteraction item, ScriptVarRegistry variables)
    {
        switch (item.Type)
        {
            case InteractionActivateType.Switch:
                variables.BindFlag(item.Task, "isPressed", item.Pressed);
                variables.BindBool(item.Task, "isOn", () => item.IsOn, value => item.IsOn = value);
                break;
            case InteractionActivateType.Cabinet:
                variables.BindFlag(item.Task, "isInUse", item.InUse);
                variables.BindBool(item.Task, "isSearched", () => item.IsSearched, _ => { });
                break;
            case InteractionActivateType.Terminal:
                variables.BindFlag(item.Task, "isInUse", item.InUse);
                variables.BindBool(item.Task, "isOn", () => item.IsOn, value => item.IsOn = value);
                variables.BindBool(item.Task, "isHacked", () => item.IsCompleted, _ => { });
                variables.BindBool(item.Task, "isHackedThisTick", () => item.CompletedThisTick, _ => { });
                break;
            case InteractionActivateType.Generic:
            case InteractionActivateType.PlaceExplosive:
                variables.BindFlag(item.Task, "isInUse", item.InUse);
                variables.BindBool(item.Task, "isOn", () => item.IsOn, value => item.IsOn = value);
                variables.BindBool(item.Task, "isFinished", () => item.IsCompleted, _ => { });
                variables.BindBool(item.Task, "isFinishedThisTick", () => item.CompletedThisTick, _ => { });
                break;
            case InteractionActivateType.Door when item.Door is { } door:
                variables.BindBool(item.Task, "isLocked", () => item.IsLocked, _ => { });
                variables.BindBool(item.Task, "isOpen", () => door.IsFullyOpen, _ => { });
                variables.BindBool(item.Task, "isClosed", () => door.IsFullyClosed, _ => { });
                variables.BindBool(item.Task, "isLastOpen", () => door.WasFullyOpen, _ => { });
                variables.BindBool(item.Task, "isLastClosed", () => door.WasFullyClosed, _ => { });
                variables.BindBool(item.Task, "isPicked", () => item.IsPicked, _ => { });
                variables.BindInt(item.Task, "nDoorOpenTicks", () => door.TicksOpen, _ => { });
                break;
        }
    }

    private static int DurationOf(DeclaredTask task, InteractionActivateType type)
    {
        string parameter = type switch
        {
            InteractionActivateType.Door => "Pick lock time",
            InteractionActivateType.Cabinet => "Search Time",
            InteractionActivateType.Terminal => "Hack Time",
            InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive => "Activate Time",
            _ => string.Empty,
        };
        return parameter.Length == 0 ? 0 : Math.Max(1, (int)(ReadReal(task, parameter, 1f) * 30f));
    }

    private static string SpriteOf(DeclaredTask task, InteractionActivateType type) => type switch
    {
        InteractionActivateType.Switch => "action_pressbutton.spr",
        InteractionActivateType.Door => "a_door.spr",
        InteractionActivateType.Cabinet => "action_searchcab.spr",
        InteractionActivateType.Terminal => "action_comput.spr",
        InteractionActivateType.StationaryGun => "stationary.spr",
        InteractionActivateType.Wire => "a_wire.spr",
        InteractionActivateType.Fence => "a_fence.spr",
        InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive =>
            ReadString(task, "Click sprite", "a_c4.spr").Replace("STATUSSCREEN:", string.Empty, StringComparison.OrdinalIgnoreCase),
        _ => string.Empty,
    };

    private static Vector3d PositionOf(DeclaredTask task, InteractionActivateType type) =>
        type == InteractionActivateType.Wire
            ? ReadPosition(task, "Start position", default)
            : ReadPosition(task, "Position", ReadPosition(task, "Position start", default));

    private static string ModelNameOf(DeclaredTask task, InteractionActivateType type) =>
        type == InteractionActivateType.Switch ? "switch" : ReadString(task, "Model", string.Empty);

    private static float FacingOf(DeclaredTask task)
    {
        if (task.TryGetValue("Orientation", out object? orientation))
        {
            return orientation switch { float angle => angle, Vector3f angles => angles.Z, _ => 0f };
        }
        return task.TryGetValue("Gamma", out object? gamma) && gamma is float value ? value : 0f;
    }

    private static Matrix3f OrientationOf(DeclaredTask task)
    {
        if (task.TryGetValue("Orientation", out object? orientation))
        {
            return orientation switch
            {
                Vector3f angles => WorldScene.ToOrientation(angles),
                float gamma => Matrix3f.RotationZ(gamma),
                _ => Matrix3f.Identity,
            };
        }

        return task.TryGetValue("Gamma", out object? rawGamma) && rawGamma is float value
            ? Matrix3f.RotationZ(value)
            : Matrix3f.Identity;
    }

    private static Vector3d ReadPosition(DeclaredTask task, string name, Vector3d fallback) =>
        task.TryGetValue(name, out object? value) && value is Vector3d position ? position : fallback;

    private static bool ReadBool(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? value) && value is bool flag && flag;

    private static float ReadReal(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            float single => single,
            double real => (float)real,
            int integer => integer,
            _ => fallback,
        } : fallback;

    private static string ReadString(DeclaredTask task, string name, string fallback) =>
        task.TryGetValue(name, out object? value) && value is string text && text.Length > 0 ? text : fallback;

    private static Dictionary<string, MefModel> ModelsByName(LoadedMission mission)
    {
        Dictionary<string, MefModel> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (pack.TryGetModel(instance, out MefModel? model) && model is not null)
                {
                    result[instance.Name] = model;
                }
            }
        }
        return result;
    }
}
