using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.ViewModel;
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
public sealed partial class Igi2EditBoneObjTask : Igi2Task, IIgi2PlacedTask
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
    private int _listAt = -1;
    private int _blinkTimer;
    private Igi2BoneBuffer? _bones;

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
    public float Blink { get; private set; }

    /// <summary>Gets the clip it plays, or <see langword="null"/>.</summary>
    public Igi2AnimationClip? Clip => _current?.Clip;

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
        if (_rig is null)
        {
            return;
        }

        _current = NewController();
        if (TransitionTime > 0f)
        {
            _other = NewController();
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
        level.RunChildren(this);
        _current?.AutoStep();
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
    public IReadOnlyList<TitleWorldModel> Models(long tick)
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
        float[] turn = Igi2Pose.Rotation(Orientation.X, Orientation.Y, Orientation.Z);
        Matrix3f frame = new(turn[0], turn[1], turn[2], turn[3], turn[4], turn[5], turn[6], turn[7], turn[8]);
        List<TitleWorldModel> models = [new TitleWorldModel(WornModel, Position, frame, pose, this, tick)];
        if (_current.Clip is { } clip)
        {
            foreach (Igi2AttachedPart part in Igi2AttachedParts.Of(clip, _current.Time, pose, WeaponModel.Length > 0 ? WeaponModel : null))
            {
                Vector3f local = frame.Transposed().Transform(part.Position);
                models.Add(new TitleWorldModel(part.Model, new Vector3d(Position.X + local.X, Position.Y + local.Y, Position.Z + local.Z), frame * part.Orientation));
            }
        }

        return models;
    }

    private Igi2AnimController NewController()
    {
        Igi2AnimController controller = new()
        {
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

        if (_rig is { } rig && _clips?.Find(rig, name) is { } clip)
        {
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
