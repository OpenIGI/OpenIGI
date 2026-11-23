using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// An animated figure, I.G.I. 2's <c>EditBoneObj</c> (editboneobj.c): the actors of cutscenes and the people standing about, a boned
/// model that plays a list of clips in turn, blending from one to the next, with a weapon in hand and blinking eyes.
/// </summary>
/// <remarks>
/// <para>
/// <c>EditBoneObj_UpdateInternalDataHandler</c>: the model is given its face (<see cref="Igi2HumanFaces"/>); with a rig, a clip controller
/// is made (two and a blend when its transition time is above nought), at its time factor, smoothed when asked; the list starts at
/// its first clip. After load its weapon expression, when it has one, names the weapon model.
/// </para>
/// <para>
/// <c>EditBoneObj_RunHandler</c>: its children run; when the clip ends, the next in the list (split on commas, spaces, tabs and new
/// lines) plays, blending over its transition time, back to the first when the list loops; a list that does not loop holds its last.
/// While it blinks, a pause of up to 150 ticks is drawn from the module's blink stream, then three frames close the eyes by 1, 0.6
/// and 0.2.
/// </para>
/// </remarks>
public sealed partial class Igi2EditBoneObjTask : Igi2Task, IIgi2PlacedTask, Animation.IIgi2MorphTargets, Interaction.IIgi2Carried
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "EditBoneObj";

    /// <summary>How the eyes close on the three frames of a blink, <c>igi2 0x6B0734</c>.</summary>
    public static readonly float[] BlinkFrames = [0.2f, 1f, 0.6f, 0.2f];

    private static readonly char[] Separators = [',', ' ', '\t', '\n'];

    private Igi2AnimationLibrary? _clips;
    private Igi2BoneHierarchy? _rig;
    private Igi2AnimController? _current;
    private Igi2AnimController? _other;
    private Igi2AnimBlend? _blend;
    private readonly Igi2AnimController?[] _made = new Igi2AnimController?[2];
    private int _listAt = -1;
    private int _blinkTimer;
    private Igi2BoneBuffer? _bones;
    private readonly Igi2Attachment _attachment = new();
    private float[]? _carriedFrame;

    /// <summary>Gets or sets where it stands.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets its turn, as three Euler angles.</summary>
    [ScriptParam(ScriptParamType.Real32x9, "Orientation")]
    public Vector3f Orientation { get; set; }

    /// <summary>Gets or sets a value indicating whether its clip list starts again at its end.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Loop animation list")]
    public bool LoopList { get; set; }

    /// <summary>Gets or sets a value indicating whether its clips are smoothed between frames.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Use spline interpolation")]
    public bool SplineInterpolation { get; set; } = true;

    /// <summary>Gets or sets its model.</summary>
    [ScriptParam(ScriptParamType.String16, "Model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether it blinks.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Blinking")]
    public bool Blinking { get; set; } = true;

    /// <summary>Gets or sets the weapon in its hand.</summary>
    [ScriptParam(ScriptParamType.String16, "WeaponModel")]
    public string WeaponModel { get; set; } = string.Empty;

    /// <summary>Gets or sets the expression that names the weapon in its hand, run once the level is loaded.</summary>
    [ScriptParam(ScriptParamType.VarString, "WeaponModel Expression")]
    public string WeaponExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets its clips, in the order they play.</summary>
    [ScriptParam(ScriptParamType.VarString, "Animation list")]
    public string AnimationList { get; set; } = string.Empty;

    /// <summary>Gets or sets how fast its clips play.</summary>
    [ScriptParam(ScriptParamType.Real32, "Time factor")]
    public float TimeFactor { get; set; } = 1f;

    /// <summary>Gets or sets how long it blends from one clip to the next, in seconds.</summary>
    [ScriptParam(ScriptParamType.Real32, "Transition time")]
    public float TransitionTime { get; set; }

    /// <summary>Gets the model it wears, its face given.</summary>
    public string WornModel { get; private set; } = string.Empty;

    /// <summary>Gets how far its eyes are closed, nought open.</summary>
    public float Blink
    {
        get => MorphWeights[Animation.Igi2MorphWeights.Eyelids];
        private set => MorphWeights[Animation.Igi2MorphWeights.Eyelids] = value;
    }

    /// <inheritdoc/>
    /// <remarks>The eyelids' channel is <see cref="Blink"/>.</remarks>
    public float[] MorphWeights { get; } = new float[Formats.World.MefMorph.Channels];

    /// <summary>Gets the clip it plays, or <see langword="null"/>.</summary>
    public Igi2AnimationClip? Clip => _current?.Clip;

    /// <summary>Gets the time of the clip it plays, in clip ticks.</summary>
    public int ClipTime => _current?.Time ?? 0;

    /// <summary>Gets the clips of its list, split as the original splits them.</summary>
    public IReadOnlyList<string> Clips => AnimationList.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Readies it as the level loads: its face, its rig, its controllers and first clip, and its weapon; one draw of the blink stream for
    /// its first pause (<c>EditBoneObj_CreateHandler</c>).
    /// </summary>
    /// <param name="clips">The level's clips.</param>
    /// <param name="models">Resolves a model's mesh.</param>
    /// <param name="wornModel">The model it wears, its face given (<see cref="Igi2HumanFaces.Assign"/>).</param>
    /// <param name="blink">The module's blink stream.</param>
    public void Prepare(Igi2AnimationLibrary? clips, Func<string, Formats.World.MefModel?> models, string wornModel, IgiRandom blink)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(wornModel);
        ArgumentNullException.ThrowIfNull(blink);
        BlinkRandom = blink;
        _blinkTimer = (int)(blink.NextRaw() % 150u);
        WornModel = wornModel;
        _clips = clips;
        _rig = models(WornModel)?.BoneHierarchy;
        StartAnimation();
    }

    // Its clip controllers made anew, a blend between two when it has a transition, and its list played from its first clip
    // (EditBoneObj_UpdateInternalDataHandler; EditBoneObj_DeSerializeDoneHandler does the same once a save is read).
    private void StartAnimation()
    {
        _current = null;
        _other = null;
        _blend = null;
        Array.Clear(_made);
        if (_rig is null)
        {
            return;
        }

        _current = NewController();
        _made[0] = _current;
        if (TransitionTime > 0f)
        {
            _other = NewController();
            _made[1] = _other;
            _blend = new Igi2AnimBlend();
            _blend.Create(_current, _other);
        }

        _listAt = Clips.Count > 0 ? 0 : -1;
        UpdateAnimation();
    }

    /// <summary>Names its weapon from its expression, once the level is loaded (<c>EditBoneObj_UpdateInternalDataAfterLoadHandler</c>).</summary>
    /// <param name="natives">The script namespace.</param>
    /// <param name="functions">The natives scripts may call.</param>
    public void AfterLoad(OpenIGI.Scripting.Runtime.NativeRegistry natives, Igi2ScriptFunctions functions)
    {
        if (WeaponExpression.Length > 0)
        {
            string weapon = Igi2LineScript.EvaluateText(WeaponExpression, natives, functions);
            WeaponModel = weapon.Length > 15 ? weapon[..15] : weapon;
        }
    }

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        // The controllers and the blender are the figure's own child tasks, made after the level's children, so they run after them
        // and in the order they were made: both controllers step, the one blended from as well, then the blend.
        level.RunChildren(this);
        _made[0]?.AutoStep();
        _made[1]?.AutoStep();
        _blend?.Run();
        if (_current is { IsEnded: true } && _listAt >= 0)
        {
            _listAt++;
            if (_listAt >= Clips.Count)
            {
                _listAt = LoopList ? 0 : -1;
            }

            if (_listAt >= 0)
            {
                UpdateAnimation();
            }
        }

        if (Blinking)
        {
            RunBlink();
        }
    }

    /// <summary>The models it draws as this tick: itself posed, and what its clip hangs on it.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The models, or none without a rig or while its container keeps it out of the world.</returns>
    /// <remarks>
    /// Built once a tick, as the soldiers' are (<see cref="Soldiers.Igi2Soldiers.Models"/>): the draw asks every frame, and posing
    /// the rig again between two ticks gave the same answer. Moved, turned or dressed between ticks, it is built again.
    /// </remarks>
    public IReadOnlyList<TitleWorldModel> Models(long tick)
    {
        if (_modelsTick == tick && _modelsAt == Position && _modelsFacing == Orientation && ReferenceEquals(_modelsWorn, WornModel))
        {
            return _models;
        }

        _models = BuildModels(tick);
        _modelsTick = tick;
        _modelsAt = Position;
        _modelsFacing = Orientation;
        _modelsWorn = WornModel;
        return _models;
    }

    private IReadOnlyList<TitleWorldModel> _models = [];
    private long _modelsTick = long.MinValue;
    private Vector3d _modelsAt;
    private Vector3f _modelsFacing;
    private string? _modelsWorn;

    private List<TitleWorldModel> BuildModels(long tick)
    {
        if (_rig is not { } rig || _current is null || !IsInWorld())
        {
            return [];
        }

        _bones ??= new Igi2BoneBuffer(rig.BoneCount);
        IIgi2PoseSource source = _blend is { } blend ? blend : _current;
        source.Interpolate(_bones, Igi2AnimController.EveryBone);
        Igi2BoneMath.UpdateHierarchy(rig, _bones, null);
        BonePose pose = _bones.ToPose(rig.Parents);
        float[] turn = _carriedFrame ?? EngineEuler.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
        Matrix3f frame = new(turn[0], turn[1], turn[2], turn[3], turn[4], turn[5], turn[6], turn[7], turn[8]);
        List<TitleWorldModel> models = [new TitleWorldModel(WornModel, Position, frame, pose, this, tick) { MorphWeights = Animation.Igi2MorphWeights.Drawn(MorphWeights) }];
        if (_current.Clip is { } clip)
        {
            // Each part named by its place on the body, so it is drawn between ticks with the body rather than stepping beside it.
            int index = 0;
            foreach (Igi2AttachedPart part in Igi2AttachedParts.Of(clip, _current.Time, pose, WeaponModel.Length > 0 ? WeaponModel : null))
            {
                Vector3f local = frame.Transposed().Transform(part.Position);
                models.Add(new TitleWorldModel(
                    part.Model,
                    new Vector3d(Position.X + local.X, Position.Y + local.Y, Position.Z + local.Z),
                    frame * part.Orientation,
                    Instance: (this, index++),
                    Tick: tick));
            }
        }

        return models;
    }

    /// <inheritdoc/>
    public bool AttachTo(OpenIGI.Game.World.IAttachParent parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _attachment.Attach(parent, Position, EngineEuler.Rotation(Orientation.X, Orientation.Y, Orientation.Z));
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>EditBoneObj_UpdateAttachmentHandler</c> over <c>ModelObj_UpdateAttachment</c> (<c>igi2 0x56CB40</c>): it stands and faces
    /// as what carries it now holds it -- a pilot stays in the seat of a helicopter that flies.
    /// </remarks>
    public void UpdateAttachment()
    {
        if (_attachment.TryResolve(out Vector3d position, out float[] frame))
        {
            Position = position;
            _carriedFrame = frame;
        }
    }

    // AnimController_CreateHandler (animcontroller.c): a new controller steps itself and has no clip yet.
    private Igi2AnimController NewController()
    {
        Igi2AnimController controller = new()
        {
            Flags = Igi2AnimControllerBits.AutoStep | Igi2AnimControllerBits.NoClip,
            TimeStep = Fpu.ToInt32((Fpu)TimeFactor * (double)Igi2AnimController.TicksPerFrame),
        };
        controller.Flags = SplineInterpolation ? controller.Flags | Igi2AnimControllerBits.Spline : controller.Flags & ~Igi2AnimControllerBits.Spline;
        return controller;
    }

    /// <summary>Plays the list's clip at its place, blending from the last over the transition (<c>EditBoneObj_UpdateAnimation</c>).</summary>
    private void UpdateAnimation()
    {
        if (_listAt < 0 || _listAt >= Clips.Count || _current is null)
        {
            return;
        }

        if (_blend is { } blend && _other is { } other)
        {
            (_current, _other) = (other, _current);
            blend.Blend(Fpu.ToInt32((Fpu)TransitionTime * 4800.0), _current, _other);
        }

        string name = Clips[_listAt];
        if (name.Length > 255)
        {
            name = name[..255];
        }

        // EditBoneObj_SetAnimation: a figure's clips fire no anim triggers.
        if (_rig is { } rig && _clips?.Find(rig, name) is { } clip)
        {
            _current.Flags |= Igi2AnimControllerBits.SuppressTriggers;
            _current.SetAnimationAndTime(clip, 0);
            _current.TimeStep = Fpu.ToInt32((Fpu)TimeFactor * (double)Igi2AnimController.TicksPerFrame);
        }
    }

    private void RunBlink()
    {
        _blinkTimer--;
        if (_blinkTimer >= 0)
        {
            return;
        }

        int frame = -_blinkTimer;
        if (frame >= 4)
        {
            Blink = 0f;
            _blinkTimer = (int)((BlinkRandom?.NextRaw() ?? 0u) % 150u);
        }
        else
        {
            Blink = BlinkFrames[frame];
        }
    }

    /// <summary>Gets or sets the module's blink stream, which every figure's pauses are drawn from.</summary>
    public IgiRandom? BlinkRandom { get; set; }

    private bool IsInWorld()
    {
        for (GameTask? task = Parent; task is not null; task = task.Parent)
        {
            if (task is Igi2ConditionalContainerTask { IsActive: false })
            {
                return false;
            }
        }

        return true;
    }
}
