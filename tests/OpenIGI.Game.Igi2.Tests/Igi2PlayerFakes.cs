using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Test doubles for the parts of the engine the player's states reach into.</summary>
internal static class Igi2PlayerFakes
{
    /// <summary>Builds a player with recording doubles.</summary>
    /// <param name="weapon">The weapon controls; none when omitted.</param>
    /// <returns>The player and its doubles.</returns>
    public static (Igi2HumanPlayer Player, FakeAnimation Animation, FakeSurroundings Surroundings) Player(
        IIgi2PlayerWeaponControl? weapon = null)
    {
        FakeAnimation animation = new();
        FakeSurroundings surroundings = new();
        return (new Igi2HumanPlayer(weapon ?? Igi2NoWeaponControl.Instance, animation, surroundings), animation, surroundings);
    }

    /// <summary>Feeds one frame of held actions through the player's own reader.</summary>
    /// <param name="player">The player.</param>
    /// <param name="held">The actions held this tick.</param>
    public static void Feed(Igi2HumanPlayer player, Igi2PlayerAction held)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        new Igi2PlayerInputReader().Read(new Igi2PlayerInputState(0f, 0f, held, 0), frame);
        player.Channels.Accept(frame);
    }

    /// <summary>An animation layer that records what the states ask it to play.</summary>
    internal sealed class FakeAnimation : IIgi2HumanAnimation
    {
        public List<(int State, int Time, float Blend)> Played { get; } = [];

        public int MainTime { get; set; }

        public int MainTotalTime { get; set; }

        public int MainTimeStep { get; set; }

        public bool MainEnded { get; set; }

        public bool Looping { get; private set; }

        public void LoopMain() => Looping = true;

        public List<float> UpperBodyClears { get; } = [];

        public void ClearUpperBody(float blendSeconds) => UpperBodyClears.Add(blendSeconds);

        public void PlayState(int state, int time, float blendSeconds)
        {
            Played.Add((state, time, blendSeconds));
            Looping = false;
        }
    }

    /// <summary>Surroundings with a settable fit and a count of walk events.</summary>
    internal sealed class FakeSurroundings : IIgi2HumanSurroundings
    {
        public bool Fits { get; set; } = true;

        public int WalkEvents { get; private set; }

        public bool FitsLyingFlat() => Fits;

        public void SendWalkEvent() => WalkEvents++;

        public int LandingEvents { get; private set; }

        public List<string> Sounds { get; } = [];

        public bool IsOnElevator { get; set; }

        public float? Water { get; set; }

        public Dictionary<int, string> LandingSounds { get; } = [];

        public FakeCollision World { get; } = new();

        public IIgi2CollisionQuery? Query { get; set; }

        public IIgi2CollisionQuery Collision => Query ?? World;

        public string? LandingSound(int material) => LandingSounds.GetValueOrDefault(material);

        public Vector3f? Conveyor { get; set; }

        public int Bumps { get; private set; }

        public Vector3f? PushOnConveyor(IIgi2Collider ground, Igi2Intersection footing) => Conveyor;

        public void BumpHumans(Vector3d start, Vector3d destination) => Bumps++;

        public void SendLandingEvent() => LandingEvents++;

        public bool IsUnderWater(out float depth)
        {
            depth = Water ?? 0f;
            return Water is not null;
        }

        public void PlaySound(string name) => Sounds.Add(name);

        public List<FakeSound> Started { get; } = [];

        public IIgi2SoundInstance StartSound(string name)
        {
            FakeSound sound = new(name);
            Started.Add(sound);
            return sound;
        }
    }

    /// <summary>A held sound that records being moved and deleted.</summary>
    internal sealed class FakeSound(string name) : IIgi2SoundInstance
    {
        public string Name { get; } = name;

        public bool IsAlive { get; set; } = true;

        public int Moves { get; private set; }

        public bool Deleted { get; private set; }

        public void MoveToHuman() => Moves++;

        public void Delete()
        {
            Deleted = true;
            IsAlive = false;
        }
    }

    /// <summary>Weapon controls that record each request by name.</summary>
    internal sealed class RecordingWeapon : IIgi2PlayerWeaponControl
    {
        public List<string> Calls { get; } = [];

        public int? ActiveCategory { get; init; }

        public bool ActiveIsZoom => false;

        public Igi2WeaponPrecision? ActivePrecision => null;

        public bool IsZoomFireModeActive => false;

        public bool IsZoomedModeAllowed => true;

        public void Fire() => Calls.Add("Fire");

        public void ClearTrigger() => Calls.Add("ClearTrigger");

        public void Zoom(bool zoomIn) => Calls.Add(zoomIn ? "ZoomIn" : "ZoomOut");

        public void LeaveZoomAlternateFireMode() => Calls.Add("LeaveZoom");

        public void StopSoundSlot() => Calls.Add("StopSoundSlot");

        public void ClearOverlay() => Calls.Add("ClearOverlay");

        public void ReleaseTriggers() => Calls.Add("ReleaseTriggers");

        public void ClearAllWeapons() => Calls.Add("ClearAllWeapons");

        public void StepActionMenu(int direction) => Calls.Add($"Menu{direction}");
    }

    /// <summary>A level of flat floors and ceilings, each answering vertical lines as a handler does.</summary>
    internal sealed class FakeCollision : IIgi2CollisionQuery
    {
        public void TraceLines(Igi2LineTrace trace, uint groups)
        {
            Queries++;
            for (int i = 0; i < trace.Count; i++)
            {
                trace.Intersections[i].IsHit = false;
                trace.Intersections[i].HitObject = null;
            }

            foreach (FakeCollider surface in Surfaces)
            {
                surface.NextZ(trace);
            }
        }

        public List<FakeCollider> Surfaces { get; } = [];

        public int Queries { get; private set; }

        public void NextZ(Igi2LineTrace trace, ReadOnlySpan<double> distances, uint groups)
        {
            Queries++;
            for (int i = 0; i < trace.Count; i++)
            {
                Vector3d start = trace.Starts[i];
                trace.Ends[i] = new Vector3d(start.X, start.Y, start.Z + distances[i]);
                trace.Intersections[i].IsHit = false;
                trace.Intersections[i].HitObject = null;
            }

            foreach (FakeCollider surface in Surfaces)
            {
                surface.NextZ(trace);
            }
        }
    }

    /// <summary>A horizontal surface at one height that faces up or down, with a material and a light.</summary>
    internal sealed class FakeCollider(double z, bool facesUp = true, int? material = null, float light = 0f, object? owner = null) : IIgi2Collider
    {
        public object Owner => owner ?? this;

        public double Z { get; } = z;

        public int Probes { get; private set; }

        public void NextZ(Igi2LineTrace trace)
        {
            Probes++;
            for (int i = 0; i < trace.Count; i++)
            {
                double from = trace.Starts[i].Z, to = trace.Ends[i].Z;
                if (Z < Math.Min(from, to) || Z > Math.Max(from, to))
                {
                    continue;
                }

                double distance = Math.Abs(from - Z);
                Igi2Intersection hit = trace.Intersections[i];
                if (hit.IsHit && !(distance < hit.Distance))
                {
                    continue;
                }

                hit.IsHit = true;
                hit.HitObject = this;
                hit.Distance = distance;
                hit.Position = new Vector3d(trace.Starts[i].X, trace.Starts[i].Y, Z);
                hit.Normal = new Vector3f(0f, 0f, facesUp ? 1f : -1f);
                hit.LocalNormal = hit.Normal;
                hit.EnterMaterial = material;
                hit.Light = light;
            }
        }
    }
}
