using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;

namespace OpenIGI.Host.Audio;

/// <summary>
/// One instantiated sound set a vehicle owns, both games' engine sounds: each <c>DefineGraph</c> loops with its gain and pitch read off
/// its parameter's curves (<c>0x4EAEB0</c>), each <c>DefineTriggerOnce</c> plays once each time its parameter turns true
/// (<c>0x4EA040</c>), and a plain sound plays once from its creation, all following their owner. The owner hands its sounds their
/// parameters every tick: Project I.G.I.'s cars the throttle, whether the engine drives and the speed (<c>0x42F850</c>); I.G.I. 2's
/// cars and trains their channel lists (<c>Car_SoundUpdateParameterListHandler</c>, <c>Train_SoundUpdateParameterListHandler</c>).
/// </summary>
internal sealed class VehicleEngineSound
{
    /// <summary>A vehicle's sounds' importance when the voices are all taken.</summary>
    internal const int Priority = 180;

    private const float TickSeconds = 1f / 30f;

    private readonly List<Member> _members;

    private VehicleEngineSound(List<Member> members) => _members = members;

    /// <summary>Instantiates a set: a voice slot for each sound the named definition holds, a group's every member.</summary>
    /// <param name="sounds">Where its definitions are resolved.</param>
    /// <param name="name">The definition's name.</param>
    /// <returns>The set; an empty one for a name nothing defines.</returns>
    public static VehicleEngineSound Create(SoundSystem sounds, string name)
    {
        List<Member> members = [];
        if (name.Length > 0
            && sounds.Definitions is { } definitions
            && definitions.TryResolve(name, out SoundDefinition? root)
            && root is not null)
        {
            Collect(root, members);
        }

        return new VehicleEngineSound(members);
    }

    /// <summary>Advances each sound of the set a tick.</summary>
    /// <param name="sounds">Where it plays.</param>
    /// <param name="parameters">What its owner hands its sounds this tick, in the owner's order.</param>
    /// <param name="positionUnits">Where the owner is, in world units.</param>
    /// <param name="listenerUnits">Where the ear is, in world units, for the distance a graph is culled at.</param>
    public void Tick(SoundSystem sounds, ReadOnlySpan<float> parameters, Vector3d positionUnits, Vector3d listenerUnits)
    {
        double distance = (listenerUnits - positionUnits).Length;
        foreach (Member member in _members)
        {
            member.Tick(sounds, parameters, positionUnits, distance);
        }
    }

    /// <summary>Stops every sound of the set.</summary>
    /// <param name="sounds">Where it plays.</param>
    public void Stop(SoundSystem sounds)
    {
        foreach (Member member in _members)
        {
            member.Stop(sounds);
        }
    }

    private static void Collect(SoundDefinition definition, List<Member> into)
    {
        if (definition.Kind == SoundDefinitionKind.Group)
        {
            foreach (SoundDefinition child in definition.Children)
            {
                Collect(child, into);
            }

            return;
        }

        into.Add(new Member(definition));
    }

    /// <summary>One sound of an engine set and its voice.</summary>
    private sealed class Member(SoundDefinition definition)
    {
        private AudioVoiceHandle _voice;
        private TriggerState _trigger;
        private float _elapsed;
        private bool _started;

        private enum TriggerState
        {
            Armed,
            Playing,
            Spent,
        }

        public void Tick(SoundSystem sounds, ReadOnlySpan<float> parameters, Vector3d positionUnits, double distanceUnits)
        {
            bool inRange = distanceUnits <= definition.MaximumDistanceMetres * SoundSystem.UnitsPerMetre;
            switch (definition.Kind)
            {
                case SoundDefinitionKind.Graph:
                    TickGraph(sounds, parameters, positionUnits, inRange);
                    break;
                case SoundDefinitionKind.TriggerOnce:
                    TickTrigger(sounds, parameters, positionUnits, inRange);
                    break;
                default:
                    // A plain sound in a set plays once, from its creation, following the vehicle.
                    if (!_started)
                    {
                        _started = true;
                        _voice = Start(sounds, positionUnits, Math.Clamp(definition.Volume, 0f, 1f), 1f, definition.Loop);
                    }
                    else if (_voice != AudioVoiceHandle.None && sounds.IsPlaying(_voice))
                    {
                        Move(sounds, positionUnits, Math.Clamp(definition.Volume, 0f, 1f), 1f, definition.Loop);
                    }

                    break;
            }
        }

        public void Stop(SoundSystem sounds)
        {
            sounds.Stop(_voice);
            _voice = AudioVoiceHandle.None;
        }

        // 0x4EAEB0: gain off curve A of its parameter (0 while the parameter is negative), pitch off curve B plus the
        // optional added parameter; looping, and only issued within its maximum distance. Gain 0 frees the voice, so
        // the loop starts again from the top when it rises.
        private void TickGraph(SoundSystem sounds, ReadOnlySpan<float> parameters, Vector3d positionUnits, bool inRange)
        {
            float gain = 1f;
            if (definition.VolumeParameterIndex >= 0)
            {
                float x = Parameter(parameters, definition.VolumeParameterIndex);
                gain = x < 0f ? 0f : SoundDefinition.Sample(definition.GainCurve, x);
            }

            float pitch = definition.PitchParameterIndex >= 0
                ? SoundDefinition.Sample(definition.PitchCurve, Parameter(parameters, definition.PitchParameterIndex))
                : 1f;
            if (definition.PitchAddParameterIndex >= 0)
            {
                pitch += Parameter(parameters, definition.PitchAddParameterIndex);
            }

            gain *= Math.Clamp(definition.Volume, 0f, 1f);
            if (!(gain > 0f) || !inRange)
            {
                Stop(sounds);
                return;
            }

            if (_voice == AudioVoiceHandle.None || !sounds.IsPlaying(_voice))
            {
                _voice = Start(sounds, positionUnits, gain, pitch, looping: true);
            }
            else
            {
                Move(sounds, positionUnits, gain, pitch, looping: true);
            }
        }

        // 0x4EA040: fires on its parameter turning true (inverted: zero), plays for its duration or its sample's
        // length, and arms again only once the parameter has gone false.
        private void TickTrigger(SoundSystem sounds, ReadOnlySpan<float> parameters, Vector3d positionUnits, bool inRange)
        {
            float value = definition.TriggerParameterIndex >= 0 ? Parameter(parameters, definition.TriggerParameterIndex) : 0f;
            bool fired = definition.Invert ? value == 0f : (int)value != 0;
            switch (_trigger)
            {
                case TriggerState.Armed:
                    if (fired && inRange)
                    {
                        float gain = definition.VolumeParameterIndex >= 0
                            ? Parameter(parameters, definition.VolumeParameterIndex)
                            : 1f;
                        _voice = Start(sounds, positionUnits, gain * Math.Clamp(definition.Volume, 0f, 1f), 1f, definition.Loop);
                        _elapsed = 0f;
                        _trigger = TriggerState.Playing;
                    }

                    break;
                case TriggerState.Playing:
                    _elapsed += TickSeconds;
                    bool done = definition.DurationSeconds >= 0f
                        ? _elapsed >= definition.DurationSeconds
                        : definition.Loop ? !fired : _voice == AudioVoiceHandle.None || !sounds.IsPlaying(_voice);
                    if (done)
                    {
                        Stop(sounds);
                        _trigger = fired ? TriggerState.Spent : TriggerState.Armed;
                    }
                    else if (_voice != AudioVoiceHandle.None)
                    {
                        Move(sounds, positionUnits, Math.Clamp(definition.Volume, 0f, 1f), 1f, definition.Loop);
                    }

                    break;
                case TriggerState.Spent:
                    if (!fired)
                    {
                        _trigger = TriggerState.Armed;
                    }

                    break;
            }
        }

        private AudioVoiceHandle Start(SoundSystem sounds, Vector3d positionUnits, float gain, float pitch, bool looping) =>
            sounds.Play(definition.BankKey, Parameters(positionUnits, gain, pitch, looping));

        private void Move(SoundSystem sounds, Vector3d positionUnits, float gain, float pitch, bool looping) =>
            sounds.SetVoiceParameters(_voice, Parameters(positionUnits, gain, pitch, looping));

        private VoiceParameters Parameters(Vector3d positionUnits, float gain, float pitch, bool looping) =>
            (VoiceParameters.Default with
            {
                Gain = gain,
                Pitch = Math.Clamp(pitch, 0.5f, 2f),
                Looping = looping,
                Priority = Priority,
            }).At(
                SoundSystem.ToMetres(positionUnits + definition.Offset),
                definition.MinimumDistanceMetres,
                definition.MaximumDistanceMetres);

        private static float Parameter(ReadOnlySpan<float> parameters, int index) =>
            index >= 0 && index < parameters.Length ? parameters[index] : 0f;
    }
}
