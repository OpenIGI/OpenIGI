using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A weapon's bullets as its <c>GunShot</c> traces them (<c>GunShot_RunInstant</c>, <c>igi2 0x4C6B50</c>), with the
/// session's MP5SD and materials against faces laid along the bullet's line.
/// </summary>
public sealed class RetailIgi2GunShotTests
{
    private const int Wood = 3;
    private const int Concrete = 11;

    private static readonly Lazy<(Igi2WeaponTypeTask Mp5sd, Igi2GameMaterials Materials)> Session = new(static () =>
    {
        Igi2Session session = Igi2Session.Start();
        Igi2WeaponTypeTask mp5sd = session.Require<Igi2WeaponTables>().WeaponTypes.Single(static w => w.WeaponId == "WEAPON_ID_MP5SD");
        return (mp5sd, session.Require<Igi2GameMaterials>());
    });

    /// <summary>
    /// An MP5SD round goes through a plank and stops in concrete. Into the wood it does its damage factor, all but the
    /// little range it covered, and the wood takes one of its five points; coming out costs another; the concrete
    /// takes four of the three left and holds it, after a hit worth three fifths. The plank rings once and the AI hears
    /// it there; all three faces leave their effects once the bullet is done.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void An_mp5sd_round_goes_through_a_plank_and_stops_in_concrete()
    {
        (Igi2WeaponTypeTask mp5sd, Igi2GameMaterials materials) = Session.Value;
        RecordingTarget target = new();
        ScriptedLine line = new(
            new Face(1000.0, Wood, null, target),
            new Face(1100.0, null, Wood, target),
            new Face(5000.0, Concrete, null, target));
        Recording effects = new();
        Shooter shooter = new(new IgiRandom(0x5EEDu));
        Igi2GunShot shot = new();
        shot.AddInstant(default, new Vector3f(0f, 1f, 0f), 260f, 0.29f, 5f, mp5sd, null);

        shot.Run(shooter, 1f, new World(line, materials), effects);

        float range = (new Fpu(260f) * 4096.0).F;
        float toPlank = Travel(0f, 0.0, 1000.0);
        float throughPlank = Travel(toPlank, (new Fpu(40.96f) + 1000.0).Value, 1100.0);
        float toConcrete = Travel(throughPlank, (new Fpu(40.96f) + 1100.0).Value, 5000.0);
        float intoWood = Damage(toPlank, range, 5f);
        float intoConcrete = Damage(toConcrete, range, 3f);
        Assert.Equal([intoWood, intoConcrete], target.Hits.Select(static h => h.Damage));
        Assert.All(target.Hits, static h => Assert.Same(Session.Value.Mp5sd, h.WeaponType));
        Assert.Equal(Igi2CollisionWorld.TerrainGroup | Igi2CollisionWorld.ForestGroup | 0x58000001u, line.Groups);

        IgiRandom reference = new(0x5EEDu);
        string ring = materials[Wood]!.BulletHitSounds[(int)(reference.NextRaw() % 6)];
        Assert.Equal([ring], effects.Sounds);
        Igi2WeaponAiEvent heard = Assert.Single(effects.AiEvents);
        Assert.Equal(42, heard.Event);
        Assert.Equal(new Vector3d(0.0, 1000.0, 0.0), heard.Position);
        Assert.Equal(new Vector3f(-0f, -1f, -0f), heard.Direction);

        // The plank's way in and the concrete each throw chips and maybe smoke; the plank's way out shows only a hole.
        int smokes = 0;
        for (int face = 0; face < 2; face++)
        {
            reference.NextRaw();
            reference.NextDouble();
            reference.NextDouble();
            reference.NextDouble();
            if ((reference.NextRaw() & 1) != 0)
            {
                smokes++;
                for (int i = 0; i < 4; i++)
                {
                    reference.NextDouble();
                }
            }
        }

        Assert.Equal(reference.NextRaw(), shooter.Random.NextRaw());
        Assert.Equal(2, effects.Fragments.Count);
        Assert.Equal(smokes, effects.Smokes.Count);
        Assert.Equal(["bullethole_wood.tex", "hole-wood-out.tex", "dent-concrete-super.tex"], effects.Holes.Select(static h => h.Texture));
        Assert.Empty(shot.Instants);
    }

    /// <summary>A bullet that strikes nothing draws nothing, sounds nothing and is forgotten.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_bullet_that_strikes_nothing_draws_nothing()
    {
        (Igi2WeaponTypeTask mp5sd, Igi2GameMaterials materials) = Session.Value;
        Recording effects = new();
        Shooter shooter = new(new IgiRandom(7u));
        Igi2GunShot shot = new();
        shot.AddInstant(default, new Vector3f(0f, 1f, 0f), 260f, 0.29f, 5f, mp5sd, null);

        shot.Run(shooter, 1f, new World(new ScriptedLine(), materials), effects);

        Assert.Equal(new IgiRandom(7u).NextRaw(), shooter.Random.NextRaw());
        Assert.Empty(effects.Sounds);
        Assert.Empty(effects.AiEvents);
        Assert.Empty(shot.Instants);
        Assert.Equal(1, shot.BulletsFired);
    }

    /// <summary>
    /// Mission 1's ground stops a round fired straight down at the spawn: the ground's face is the only one struck, the
    /// AI hears it there, and the ground's material rings.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission_1s_ground_stops_a_round_fired_into_it()
    {
        (Igi2WeaponTypeTask mp5sd, Igi2GameMaterials materials) = Session.Value;
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint spawn));
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain");
        Recording effects = new();
        Igi2GunShot shot = new();
        Vector3d muzzle = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 8192.0);
        shot.AddInstant(muzzle, new Vector3f(0f, 0f, -1f), 260f, 0.29f, 5f, mp5sd, null);

        shot.Run(new Shooter(new IgiRandom(3u)), 1f, new World(world, materials), effects);

        Igi2WeaponAiEvent heard = Assert.Single(effects.AiEvents);
        Assert.InRange(heard.Position.Z, spawn.Position.Z - 4096.0, spawn.Position.Z + 4096.0);
        string ring = Assert.Single(effects.Sounds);
        Assert.Contains(Enumerable.Range(0, Igi2GameMaterials.Rows), row => materials[row]?.BulletHitSounds.Contains(ring) == true);
    }

    /// <summary>A sixteenth bullet in one tick is counted and lost (<c>GunShot_AddInstant</c>, <c>igi2 0x4C7980</c>).</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_shot_holds_fifteen_bullets()
    {
        Igi2GunShot shot = new();
        for (int i = 0; i < 16; i++)
        {
            shot.AddInstant(default, new Vector3f(0f, 1f, 0f), 260f, 0.29f, 5f, Session.Value.Mp5sd, null);
        }

        Assert.Equal(Igi2GunShot.MaxInstants, shot.Instants.Count);
        Assert.Equal(16, shot.BulletsFired);
    }

    /// <summary>
    /// A bullet is heard where it passes within two metres of a head (<c>GunShot_FindHearableIntersections</c>): at its muzzle when
    /// that is near, else at the nearest point of its line; one that passes further off is not heard.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_bullet_is_heard_where_it_passes_near()
    {
        Igi2GunShot shot = new();
        shot.AddInstant(default, new Vector3f(0f, 1f, 0f), 260f, 0.29f, 5f, Session.Value.Mp5sd, null);
        Span<Vector3d> heard = stackalloc Vector3d[4];

        Assert.Equal(1, shot.FindHearableIntersections(new Vector3d(4096.0, 0.0, 0.0), heard));
        Assert.Equal(default, heard[0]);

        Assert.Equal(1, shot.FindHearableIntersections(new Vector3d(4096.0, 409600.0, 0.0), heard));
        Assert.Equal(0.0, heard[0].X, 6);
        Assert.Equal(409600.0, heard[0].Y, 1);

        Assert.Equal(0, shot.FindHearableIntersections(new Vector3d(12288.0, 409600.0, 0.0), heard));
    }

    /// <summary>The distance run on along the line by one leg of the trace, from where it started to the face it struck.</summary>
    private static float Travel(float travelled, double from, double to)
    {
        Fpu leg = new Fpu(to) - from;
        return (Fpu.Sqrt(((new Fpu(0.0) * 0.0) + (leg * leg)) + (new Fpu(0.0) * 0.0)) + travelled).F;
    }

    private static float Damage(float travelled, float range, float penetration) =>
        ((((Fpu)1.0f - ((new Fpu(travelled) / range) * 0.30000001f)) * 0.29f) * (new Fpu(penetration) / 5f)).F;

    private sealed record Face(double Y, int? Enter, int? Leave, IIgi2Collider Struck);

    /// <summary>Faces across the line <c>x = z = 0</c>, each struck by a line that starts before it and reaches it.</summary>
    private sealed class ScriptedLine(params Face[] faces) : IIgi2CollisionQuery
    {
        public uint Groups { get; private set; }

        public void NextZ(Igi2LineTrace trace, ReadOnlySpan<double> distances, uint groups) => throw new NotSupportedException();

        public void TraceLines(Igi2LineTrace trace, uint groups)
        {
            Groups = groups;
            Vector3d start = trace.Starts[0], end = trace.Ends[0];
            Igi2Intersection hit = trace.Intersections[0];
            hit.IsHit = false;
            hit.Position = end;
            foreach (Face face in faces)
            {
                if (face.Y > start.Y && face.Y <= end.Y)
                {
                    hit.IsHit = true;
                    hit.HitObject = face.Struck;
                    hit.Position = new Vector3d(0.0, face.Y, 0.0);
                    hit.Distance = face.Y - start.Y;
                    hit.Normal = new Vector3f(0f, face.Enter is null ? 1f : -1f, 0f);
                    hit.EnterMaterial = face.Enter;
                    hit.LeaveMaterial = face.Leave;
                    return;
                }
            }
        }
    }

    private sealed class RecordingTarget : IIgi2Collider, IIgi2WeaponHitTarget
    {
        public List<Igi2WeaponHit> Hits { get; } = [];

        public object Owner => this;

        public void NextZ(Igi2LineTrace trace) => throw new NotSupportedException();

        public void WeaponHit(in Igi2WeaponHit hit) => Hits.Add(hit);
    }

    private sealed class World(IIgi2CollisionQuery collision, Igi2GameMaterials materials) : IIgi2GunShotWorld
    {
        public IIgi2CollisionQuery Collision => collision;

        public Igi2GameMaterials Materials => materials;

        public bool IsWater(IIgi2Collider struck) => false;

        public bool IsBoned(IIgi2Collider struck) => false;

        public bool IsHuman(IIgi2Collider struck) => false;
    }

    private sealed class Recording : IIgi2GunShotEffects
    {
        public List<string> Sounds { get; } = [];

        public List<Igi2WeaponAiEvent> AiEvents { get; } = [];

        public List<Igi2ImpactFragments> Fragments { get; } = [];

        public List<Igi2ImpactSmoke> Smokes { get; } = [];

        public List<Igi2BulletHoleMark> Holes { get; } = [];

        public bool NewSound(string sound, Vector3d position)
        {
            Sounds.Add(sound);
            return sound.Length != 0;
        }

        public void SendAiEvent(in Igi2WeaponAiEvent aiEvent) => AiEvents.Add(aiEvent);

        public void Tracer(in Igi2Tracer tracer)
        {
        }

        void IIgi2GunShotEffects.Fragments(in Igi2ImpactFragments fragments) => Fragments.Add(fragments);

        public void WaterSplash(in Igi2WaterSplash splash)
        {
        }

        public void BulletHole(in Igi2BulletHoleMark hole) => Holes.Add(hole);

        public void Smoke(in Igi2ImpactSmoke smoke) => Smokes.Add(smoke);
    }

    private sealed class Shooter(IgiRandom random) : IIgi2WeaponOwner
    {
        public int Ticks => 0;

        public Humans.Igi2HumanView? ZoomView => null;

        public IgiRandom Random { get; } = random;

        public bool IsPlayer => true;

        public int GrenadeHeldTime => 0;

        public Vector3f AccumulatedSpeed => default;

        public int Team => 0;

        public bool IsZoomMode => false;

        public bool HasFireFrame => false;

        public Igi2FireFrame GetFireFrame(bool addInaccuracy) => throw new NotSupportedException();

        public int WeaponObjectCount() => 0;

        public void Flash(Vector3f flash)
        {
        }

        public void IgnoreLightOnVisibility(int ticks)
        {
        }

        public void CountShots(int shots)
        {
        }
    }
}
