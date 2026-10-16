using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>A level's soldiers: a body for every <c>HumanSoldier</c> it places.</summary>
public sealed class Igi2Soldiers
{
    private readonly List<Igi2HumanSoldier> _bodies;
    private TitleWorldModel[] _drawn = [];
    private long _drawnTick = -1;

    private Igi2Soldiers(List<Igi2HumanSoldier> bodies)
    {
        _bodies = bodies;
    }

    /// <summary>Gets the bodies, in the order the level makes them.</summary>
    public IReadOnlyList<Igi2HumanSoldier> Bodies => _bodies;

    /// <summary>The models the soldiers draw as a tick left them: each body posed, and the weapon in its hand.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The models, built once a tick.</returns>
    public IReadOnlyList<TitleWorldModel> Models(long tick)
    {
        if (_drawnTick != tick)
        {
            List<TitleWorldModel> models = [];
            foreach (Igi2HumanSoldier body in _bodies)
            {
                if (body.Model is { } model)
                {
                    models.AddRange(Igi2BodyFrame.Build(body, body.BodyPose(), model.Name, body.Weapon.Inventory?.Current?.WeaponType.FixedModel, tick, body.Weapon.Firing?.Flashes));
                }
            }

            _drawn = [.. models];
            _drawnTick = tick;
        }

        return _drawn;
    }

    /// <summary>
    /// Makes a body for each of a level's soldiers, as <c>Human_CreateHandler</c> (<c>igi2 0x4FDC90</c>),
    /// <c>HumanSoldier_CreateHandler</c> (<c>0x4E9E10</c>) and <c>Human_UpdateInternalDataHandler</c> (<c>0x4FF2F0</c>) make one.
    /// </summary>
    /// <param name="levelRoot">The level.</param>
    /// <param name="body">Builds a body's surroundings, weapon and animation for a soldier's task and rig.</param>
    /// <param name="models">Resolves a placed model name to its mesh.</param>
    /// <param name="random">The session's run stream, which every soldier draws from.</param>
    /// <param name="worn">The model each soldier wears, its face given; <see langword="null"/> for the level's own.</param>
    /// <returns>The soldiers.</returns>
    /// <remarks>
    /// Each stands where the level puts it, turned by its <c>Gamma</c> and upright; its weapon script arms it. Its task runs it
    /// from then on (<see cref="Igi2HumanSoldierTask.Body"/>). Every soldier gets a collider over its model's posed mesh
    /// (<see cref="Igi2Human.Collider"/>), and they share one death clip counter.
    /// </remarks>
    public static Igi2Soldiers Spawn(
        GameTask levelRoot,
        Func<Igi2HumanSoldierTask, Igi2BoneHierarchy?, (IIgi2HumanSurroundings Surroundings, IIgi2PlayerWeaponControl Weapon, IIgi2HumanAnimation Animation)> body,
        Func<string, Formats.World.MefModel?> models,
        IgiRandom random,
        IReadOnlyDictionary<GameTask, string>? worn = null)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(random);
        List<Igi2HumanSoldier> bodies = [];
        Dictionary<string, Igi2BodyModel> looks = new(StringComparer.OrdinalIgnoreCase);
        Igi2DeathAnimCycle cycle = new();
        foreach (Igi2HumanSoldierTask task in Igi2TaskTree.Descendants(levelRoot).OfType<Igi2HumanSoldierTask>())
        {
            string wornModel = worn is not null && worn.TryGetValue(task, out string? face) ? face : task.Model;
            Formats.World.MefModel? model = models(wornModel);
            Igi2BoneHierarchy? rig = model?.BoneHierarchy;
            (IIgi2HumanSurroundings surroundings, IIgi2PlayerWeaponControl weapon, IIgi2HumanAnimation animation) = body(task, rig);
            Igi2HumanSoldier soldier = new(weapon, animation, surroundings)
            {
                Random = random,
                Position = task.Position,
                Gamma = task.Gamma,
                IdealGamma = task.Gamma,
                Team = task.Team,
                DeathCycle = cycle,
            };
            soldier.Create();
            soldier.BoneHierarchy = task.BoneHierarchy;
            soldier.StandAnimation = task.StandAnimation;
            float[] facing = Igi2Pose.Rotation(0f, 0f, task.Gamma);
            soldier.Orientation = new Matrix3f(
                facing[0], facing[1], facing[2],
                facing[3], facing[4], facing[5],
                facing[6], facing[7], facing[8]);
            if (model is not null)
            {
                // BoneDynCubeObj_SetLODVirModel (igi2 0x567BE0): the root's bind translation and the mesh's radius.
                soldier.ModelRadius = model.NativeMarkerBound;
                if (rig is { BoneCount: > 0 })
                {
                    soldier.RootTranslation = rig.Translations[0];
                }
            }

            if (!looks.TryGetValue(wornModel, out Igi2BodyModel? look))
            {
                look = Igi2BodyModel.From(wornModel, model);
                looks[wornModel] = look;
            }

            soldier.Model = look;
            if (animation is Igi2HumanAnimation layer)
            {
                layer.SetBranches(look.BoneNames);
            }

            soldier.Collider = new Igi2BoneCollider(soldier, soldier);
            task.Body = soldier;
            bodies.Add(soldier);
        }

        return new Igi2Soldiers(bodies);
    }

    /// <summary>
    /// Readies each soldier's damage after load as its type and the difficulty tune it: the soldier's own after-load
    /// (<see cref="Igi2HumanSoldier.AfterLoad"/>), then its <c>HumanAI</c>'s (<c>HumanAI_UpdateInternalDataAfterLoadHandler</c>,
    /// <c>igi2 0x460120</c>), a child's after its parent's (<c>QTask_TriggerQTaskEventRecursivelyNoContext</c>, <c>0x40B040</c>).
    /// </summary>
    /// <param name="levelRoot">The level.</param>
    /// <param name="config">The AI's tuning.</param>
    /// <param name="difficulty">The session's difficulty, nought to two.</param>
    /// <remarks>A soldier with no <c>HumanAI</c> keeps the damage it was made with.</remarks>
    public static void AfterLoad(GameTask levelRoot, Igi2HumanAiConfig config, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(config);
        foreach (Igi2HumanSoldierTask task in Igi2TaskTree.Descendants(levelRoot).OfType<Igi2HumanSoldierTask>())
        {
            if (task.Body is not { } soldier || task.Ai is not { } ai || (uint)ai.AiType >= (uint)config.BaseRows.Count)
            {
                continue;
            }

            soldier.AfterLoad(config.BaseRows[ai.AiType].DamageMultipliers);
            if (config.Tweaks(ai.AiType, difficulty) is { } tweaks)
            {
                soldier.ApplyTweaks(tweaks);
            }
        }
    }
}

/// <summary>
/// A soldier's mind until the AI's is ported: it starts at once and sends the body an update with nothing on any channel each
/// tick, so the soldier stands where it was put. It says its lines as a <c>HumanAI</c> does.
/// </summary>
public sealed class Igi2StandingSoldierMind : IIgi2SoldierMind
{
    /// <summary>How long a line said keeps the mind from saying another, in ticks: <c>60</c>.</summary>
    public const int SayTimeout = 60;

    private readonly float[] _frame = new float[Igi2HumanChannels.Count];

    /// <inheritdoc/>
    public bool HasStarted { get; private set; }

    /// <summary>Gets the ticks before the mind may say another line, <c>HumanAI+0x1154</c>.</summary>
    public int SayTimer { get; private set; }

    /// <inheritdoc/>
    /// <remarks>The line timer counts down first, as <c>HumanAI_RunHandler</c> (<c>igi2 0x45F575</c>) does.</remarks>
    public void Run(Igi2HumanSoldier soldier)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        if (SayTimer != 0)
        {
            SayTimer--;
        }

        soldier.Update(_frame);
        HasStarted = true;
    }

    /// <inheritdoc/>
    public void Say(Igi2HumanSoldier soldier, string sound)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        if (SayTimer != 0)
        {
            return;
        }

        SayTimer = SayTimeout;
        soldier.Surroundings.PlaySound(sound);
    }
}
