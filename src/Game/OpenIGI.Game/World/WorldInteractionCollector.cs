using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>
/// Reads a level's task tree into a <see cref="WorldInteractionRegistry"/>: every direct interaction with its model
/// geometry, expressions and exports, every generator and every radio, each in the order the tree holds them.
/// </summary>
internal static class WorldInteractionCollector
{
    /// <summary>Collects every direct interaction under a task, the task first and then its children in order.</summary>
    internal static void Collect(
        GameTask task,
        WorldInteractionRegistry registry,
        IReadOnlyDictionary<string, MefModel> models,
        DoorRegistry doors,
        ScriptVarRegistry variables,
        LevelExpressionRuntime expressions,
        ElevatorRegistry? elevators)
    {
        // Every task is collected, in a container that is running or not: retail binds a task's variables as it is
        // created, and a container's ACTIVATE only puts its children in the world (0x487A70). Mission 14 streams its
        // building sections in through AreaActivate containers; skipping their subtrees at load left every door,
        // switch and terminal in them without its exports or its prompt once the player walked in.
        if (task is DeclaredTask declared && TypeOf(task.Type.Name) is { } type)
        {
            Vector3d taskPosition = PositionOf(declared, type);
            Vector3d start = taskPosition;
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

            PlacedDoor? door = type == InteractionActivateType.Door
                ? doors.Doors.FirstOrDefault(candidate => ReferenceEquals(candidate.Task, task))
                : null;
            AiStationaryGun? stationaryGun = type == InteractionActivateType.StationaryGun
                ? new AiStationaryGun(
                    declared.Id,
                    taskPosition,
                    orientation,
                    ReadReal(declared, "Max up angle", 25f),
                    ReadReal(declared, "Max down angle", -25f),
                    ReadReal(declared, "Max sideways angle", 90f),
                    ReadString(declared, "Weapon ID", "WEAPON_ID_M2HB"),
                    (int)ReadReal(declared, "Ammo", -1f))
                : null;
            if (stationaryGun is not null
                && models.TryGetValue(modelName, out MefModel? standModel)
                && standModel.MagicVertexCount >= 2
                && standModel.TryGetMagicVertex(1, out Vector3f mount))
            {
                // 0x474420 asks 0x414E20 for magic vertex 1 of a stand that has any, and 0x414E20
                // answers the origin unless the stand has more than one.
                stationaryGun.MountVertex = mount;
            }

            // State 36's seat (0x444B30) is measured from vertex 0, the origin for a stand with none.
            if (stationaryGun is not null
                && models.TryGetValue(modelName, out MefModel? seatModel)
                && seatModel.MagicVertexCount >= 1
                && seatModel.TryGetMagicVertex(0, out Vector3f seat))
            {
                stationaryGun.SeatVertex = seat;
            }

            (Vector3d localBoundsMinimum, Vector3d localBoundsMaximum, bool hasLocalBounds) =
                type == InteractionActivateType.Fence
                    && models.TryGetValue(modelName, out MefModel? fenceModel)
                    ? BoundsOf(fenceModel)
                    : default;
            // Where a marker for this task goes: the middle of its model, in the task's own frame and
            // then in the world. Every activatable in the level has a model, and its middle is the one
            // point unarguably on the thing; the task position is only where the author dropped its
            // origin, which for a door is the hinge edge and for a terminal the floor beneath it.
            (Vector3d markerPoint, Vector3d markerLocal) =
                MarkerOf(taskPosition, orientation, start, models, modelName);
            WorldInteraction item = new(
                declared, type, start, end, orientation, facing,
                DurationOf(declared, type), SpriteOf(declared, type), door,
                ReadBool(declared, "Pickable"), stationaryGun,
                localBoundsMinimum, localBoundsMaximum, hasLocalBounds,
                markerPoint, markerLocal,
                type == InteractionActivateType.PlaceExplosive ? ReadInt(declared, "Task ID to place", -1) : -1,
                // An Int16 parameter, which the task holds as a short: read as an int it answered -1 for every
                // charge, no clip started, and the body kept the pose it walked up in.
                type is InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive
                    ? ReadInt(declared, "Activate Anim", -1)
                    : -1)
            {
                IsDestructable = type == InteractionActivateType.Switch && ReadBool(declared, "Destructable"),
                IsExplodable = type is InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive && ReadBool(declared, "Explodable"),
                TaskPosition = taskPosition,
                FenceSpan = type == InteractionActivateType.Fence
                    && models.TryGetValue(modelName, out MefModel? spanned)
                    && spanned.TryGetMagicVertex(0, out Vector3f firstPost)
                    && spanned.TryGetMagicVertex(1, out Vector3f secondPost)
                        ? (firstPost.X, secondPost.X)
                        : null,
            };
            item.Sounds = sound => registry.Sounds?.Invoke(sound);
            if (elevators is not null)
            {
                // Interaction anchors, activation radii and player positions are engine units. The
                // render path uses metres, so do not reuse its converted elevator displacement here.
                item.SetDynamicOffset(() => elevators.GetCurrentDisplacementInUnits(item.Task));

                // The lift's mesh is drawn between ticks; a marker on a button riding it has to be too.
                item.SetPresentedOffset(blend =>
                    elevators.TryGetDisplacement(item.Task, blend, smooth: true, out Vector3d metres)
                        ? metres * WorldScene.UnitsPerMetre
                        : elevators.GetCurrentDisplacementInUnits(item.Task));
                if (type == InteractionActivateType.Switch)
                {
                    item.SetPressHandler(() => elevators.RequestFromSwitch(item.Task));
                }
            }
            string onExpression = ReadString(declared, "On Expression", ReadString(declared, "On", string.Empty));
            string lockedExpression = ReadString(declared, "Locked expression", string.Empty);
            string openExpression = ReadString(declared, "Open door expression", string.Empty);
            string closeExpression = ReadString(declared, "Close door expression", string.Empty);
            string electricExpression = ReadString(declared, "Electric Expression", string.Empty);

            // A terminal's update (0x4438E0) and a GenericTBA's (0x4480B0) force isOn off once the ExplodeObject they derive
            // from has exploded (+400), before they look at the On expression -- a terminal blown up is a terminal switched
            // off. A switch (0x4450F0) reads its On expression alone.
            string explodedSymbol = $"{declared.Name}.isExploded";
            Func<bool> exploded = () => variables.TryRead(explodedSymbol, out QvmValue blown) && blown.ToInt32() != 0;
            Func<bool?>? on = ByteSlot(expressions, onExpression, declared);
            item.SetConditions(
                type is InteractionActivateType.Terminal or InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive
                    ? () => exploded() ? false : on?.Invoke()
                    : type is InteractionActivateType.Switch
                    ? on
                    : null,
                type == InteractionActivateType.Door ? RealSlot(expressions, lockedExpression, declared) : null,
                type == InteractionActivateType.Door && !string.IsNullOrWhiteSpace(openExpression)
                    ? () => expressions.EvaluateCondition(openExpression, emptyValue: false, declared)
                    : null,
                type == InteractionActivateType.Door && !string.IsNullOrWhiteSpace(closeExpression)
                    ? () => expressions.EvaluateCondition(closeExpression, emptyValue: false, declared)
                    : null,
                type == InteractionActivateType.Fence ? ByteSlot(expressions, electricExpression, declared) : null);
            item.Clock = () => registry.Now;
            registry.Add(item);
            BindVariables(item, variables);
        }

        foreach (GameTask child in task.Children)
        {
            Collect(child, registry, models, doors, variables, expressions, elevators);
        }
    }

    /// <summary>Collects every <c>Generator</c> under a task and binds its exports.</summary>
    internal static void CollectGenerators(
        GameTask task,
        WorldInteractionRegistry registry,
        ScriptVarRegistry variables,
        LevelExpressionRuntime expressions)
    {
        if (task is DeclaredTask declared
            && string.Equals(task.Type.Name, "Generator", StringComparison.Ordinal))
        {
            string onExpression = ReadString(
                declared,
                "On expression",
                ReadString(declared, "On Expression", string.Empty));
            // Generator::Run (0x43B500) forces isOn off once the ExplodeObject under it has exploded
            // (+400), before it ever looks at the On expression.
            string explodedSymbol = $"{declared.Name}.isExploded";
            GeneratorPowerState state = new(
                declared,
                ReadString(declared, "Run sound", string.Empty),
                ReadString(declared, "Shutdown sound", string.Empty),
                sound => registry.Sounds?.Invoke(sound),
                ByteSlot(expressions, onExpression, declared),
                () => variables.TryRead(explodedSymbol, out QvmValue exploded) && exploded.ToInt32() != 0,
                ReadBool(declared, "Use backup timer"),
                ReadReal(declared, "Seconds until power backup", 10f),
                () => ConditionalContainerGate.IsTaskActive(declared));
            registry.AddGenerator(state);
            variables.BindBool(declared, "isOn", () => state.IsOn, value => state.IsOn = value);
            variables.BindInt(
                declared,
                "nBackupTimer",
                () => state.BackupTimer,
                value => state.BackupTimer = value);

            // The constructor (0x43B490) leaves isOn (+572) off; only the update (0x43B500) evaluates it. A generator in
            // a section the level has not streamed in yet therefore reads off until it first updates.
            if (ConditionalContainerGate.IsTaskActive(declared))
            {
                state.Initialize();
            }
        }

        foreach (GameTask child in task.Children)
        {
            CollectGenerators(child, registry, variables, expressions);
        }
    }

    /// <summary>Collects every <c>Radio</c> outside a dormant container and binds its <c>isOn</c>.</summary>
    internal static void CollectRadios(
        GameTask task,
        WorldInteractionRegistry registry,
        ScriptVarRegistry variables,
        LevelExpressionRuntime expressions,
        IgiRandom random)
    {
        if (ConditionalContainerGate.IsDormant(task))
        {
            return;
        }

        if (task is DeclaredTask declared && string.Equals(task.Type.Name, "Radio", StringComparison.Ordinal))
        {
            RadioRuntime radio = RadioRuntime.Create(declared, expressions, variables, random);
            registry.AddRadio(radio);
            variables.BindBool(declared, "isOn", () => radio.IsOn, value => radio.IsOn = value);
        }

        foreach (GameTask child in task.Children)
        {
            CollectRadios(child, registry, variables, expressions, random);
        }
    }

    private static InteractionActivateType? TypeOf(string name) => name switch
    {
        "Switch" => InteractionActivateType.Switch,
        "Door" => InteractionActivateType.Door,
        "Cabinet" => InteractionActivateType.Cabinet,
        "Terminal" => InteractionActivateType.Terminal,

        // SCameraControl (0x440C20) is registered under Terminal (word_53C680): it inherits the terminal's
        // update, its hack and its isInUse/isOn/isHacked exports, and adds only its isDetection. The cameras
        // of missions 5 to 9 are powered by SCameraControl_N.isOn, so without the terminal there is no isOn
        // and every one of them stayed dark.
        "SCameraControl" => InteractionActivateType.Terminal,
        "StationaryGun" => InteractionActivateType.StationaryGun,
        "Wire" => InteractionActivateType.Wire,
        "Fence" => InteractionActivateType.Fence,
        "GenericTBA" => InteractionActivateType.Generic,
        "PlaceExplosiveTBA" => InteractionActivateType.PlaceExplosive,
        _ => null,
    };

    // A task's expression slot that writes a byte: nothing when the slot is empty or did not compile (sub_4F16C0), else the
    // low byte of the value truncated, as the update stores (__int64)sub_4F16E0 -- so 0.5 and 256 are both false.
    private static Func<bool?>? ByteSlot(LevelExpressionRuntime expressions, string source, GameTask owner) =>
        string.IsNullOrWhiteSpace(source)
            ? null
            : () => expressions.TryEvaluateReal(source, owner, out double value) ? unchecked((byte)(long)value) != 0 : null;

    // A task's expression slot read whole: nothing when the slot is empty or did not compile (sub_4F16C0), else its value.
    private static Func<double>? RealSlot(LevelExpressionRuntime expressions, string source, GameTask owner) =>
        string.IsNullOrWhiteSpace(source) || LevelExpressionRuntime.Create(source, owner).IsEmpty
            ? null
            : () => expressions.TryEvaluateReal(source, owner, out double value) ? value : 0.0;

    private static void BindVariables(WorldInteraction item, ScriptVarRegistry variables)
    {
        switch (item.Type)
        {
            case InteractionActivateType.Switch:
                // Switch's exports (0x445860): the six bytes, each writable.
                variables.BindBool(item.Task, "isOn", () => item.IsOn, value => item.IsOn = value);
                variables.BindBool(item.Task, "isLastOn", () => item.LastOn, value => item.LastOn = value);
                variables.BindFlag(item.Task, "isPressed", item.Pressed);
                variables.BindBool(item.Task, "isDestroyed", () => item.IsDestroyed, value => item.IsDestroyed = value);
                variables.BindBool(item.Task, "isLastDestroyed", () => item.LastDestroyed, value => item.LastDestroyed = value);
                break;
            case InteractionActivateType.Cabinet:
                variables.BindFlag(item.Task, "isInUse", item.InUse);
                // The export is the object's own byte, so a script or probe write sticks.
                variables.BindBool(item.Task, "isSearched", () => item.IsSearched, value => item.IsSearched = value);
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
                // A door that has never updated reports nothing yet (see DoorState.HasTicked).
                variables.BindBool(item.Task, "isLocked", () => door.HasTicked && item.IsLocked, _ => { });
                variables.BindBool(item.Task, "isOpen", () => door.HasTicked && door.IsFullyOpen, _ => { });
                variables.BindBool(item.Task, "isClosed", () => door.HasTicked && door.IsFullyClosed, _ => { });
                variables.BindBool(item.Task, "isLastOpen", () => door.HasTicked && door.WasFullyOpen, _ => { });
                variables.BindBool(item.Task, "isLastClosed", () => door.HasTicked && door.WasFullyClosed, _ => { });
                variables.BindBool(item.Task, "isPicked", () => item.IsPicked, _ => { });
                variables.BindInt(item.Task, "nDoorOpenTicks", () => door.TicksOpen, _ => { });
                break;
        }
    }

    private static int DurationOf(DeclaredTask task, InteractionActivateType type)
    {
        string parameter = type switch
        {
            InteractionActivateType.Door => "Pick lock time (s)",
            InteractionActivateType.Cabinet => "Search Time (s)",
            InteractionActivateType.Terminal => "Hack Time (s)",
            InteractionActivateType.Generic or InteractionActivateType.PlaceExplosive => "Activate Time (s)",
            _ => string.Empty,
        };
        return type == InteractionActivateType.Fence
            ? WorldInteraction.FenceTraversalTicks
            : parameter.Length == 0 ? 0 : Math.Max(1, (int)(ReadReal(task, parameter, 4f) * 30f));
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
            ReadString(task, "Click to select sprite", "a_c4.spr")
                .Replace("STATUSSCREEN:", string.Empty, StringComparison.OrdinalIgnoreCase),
        _ => string.Empty,
    };

    /// <summary>The middle of a task's model, in the world and in the model's own frame.</summary>
    /// <remarks>
    /// Falls back to the task's activation anchor for anything whose model is missing, which keeps a
    /// marker on the object rather than at the origin of the level.
    /// </remarks>
    private static (Vector3d World, Vector3d Local) MarkerOf(
        Vector3d taskPosition,
        Matrix3f orientation,
        Vector3d anchor,
        IReadOnlyDictionary<string, MefModel> models,
        string modelName)
    {
        if (!models.TryGetValue(modelName, out MefModel? model))
        {
            return (anchor, default);
        }

        (Vector3d minimum, Vector3d maximum, bool hasBounds) = BoundsOf(model);
        if (!hasBounds)
        {
            return (anchor, default);
        }

        Vector3d local = (minimum + maximum) * 0.5;
        Vector3f world = orientation.Transposed()
            .Transform(new Vector3f((float)local.X, (float)local.Y, (float)local.Z));
        return (
            new Vector3d(taskPosition.X + world.X, taskPosition.Y + world.Y, taskPosition.Z + world.Z),
            local);
    }

    private static (Vector3d Minimum, Vector3d Maximum, bool HasBounds) BoundsOf(MefModel model)
    {
        ReadOnlySpan<MefVertex> vertices = model.Vertices;
        if (vertices.IsEmpty)
        {
            return default;
        }

        Vector3f first = vertices[0].Position;
        Vector3d minimum = new(first.X, first.Y, first.Z);
        Vector3d maximum = minimum;
        for (int i = 1; i < vertices.Length; i++)
        {
            Vector3f point = vertices[i].Position;
            minimum = new Vector3d(
                Math.Min(minimum.X, point.X),
                Math.Min(minimum.Y, point.Y),
                Math.Min(minimum.Z, point.Z));
            maximum = new Vector3d(
                Math.Max(maximum.X, point.X),
                Math.Max(maximum.Y, point.Y),
                Math.Max(maximum.Z, point.Z));
        }

        return (minimum, maximum, true);
    }

    private static Vector3d PositionOf(DeclaredTask task, InteractionActivateType type) =>
        type == InteractionActivateType.Wire
            ? ReadPosition(task, "Start position", default)
            : ReadPosition(task, "Position", ReadPosition(task, "Position start", default));

    private static string ModelNameOf(DeclaredTask task, InteractionActivateType type) =>
        type == InteractionActivateType.Switch
            ? WorldScene.SwitchModelNameOf(task)
            : ReadString(task, "Model", string.Empty);

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
        // A door's and a fence's frame are built by 0x4B38E0 exactly, the same matrix the world scene draws them with.
        if (string.Equals(task.Type.Name, "Door", StringComparison.Ordinal)
            && task.TryGetValue("Orientation", out object? doorAngles)
            && doorAngles is Vector3f door)
        {
            return RetailOrientation.EulerMatrix(door.X, door.Y, door.Z);
        }

        if (string.Equals(task.Type.Name, "Fence", StringComparison.Ordinal)
            && task.TryGetValue("Gamma", out object? fenceGamma)
            && fenceGamma is float fence)
        {
            return RetailOrientation.EulerMatrix(0f, 0f, fence);
        }

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

    /// <summary>Reads a real task parameter, or <paramref name="fallback"/> when it is missing or not a number.</summary>
    internal static float ReadReal(DeclaredTask task, string name, float fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            float single => single,
            double real => (float)real,
            int integer => integer,
            _ => fallback,
        } : fallback;

    private static string ReadString(DeclaredTask task, string name, string fallback) =>
        task.TryGetValue(name, out object? value) && value is string text && text.Length > 0 ? text : fallback;

    private static int ReadInt(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? value) ? value switch
        {
            int integer => integer,
            short small => small,
            float real => (int)real,
            double real => (int)real,
            _ => fallback,
        } : fallback;

    /// <summary>Every model the level's two packs place, by instance name.</summary>
    internal static Dictionary<string, MefModel> ModelsByName(LoadedMission mission)
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
