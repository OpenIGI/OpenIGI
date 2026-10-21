using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;
using OpenIGI.Presentation.Igi1;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// A body is a surface like any other to <c>0x47B750</c>: the round takes the face's material tests after
/// the hit, leaves through an exit face, and stops on the next thing it enters once that has taken the hit.
/// </summary>
public sealed class PlayerCameraBodyPenetrationTests
{
    private const double Metre = PlayerCamera.UnitsPerMetre;

    // Flesh: bullet resistance 1, no ricochet angle (material/material.qvm row 8).
    private const int Flesh = 8;

    /// <summary>
    /// A Dragunov round (power 3) beats Flesh (resistance 1) by two, so it goes through the first body, comes
    /// out of its back, and the second body in line takes the hit -- and stops it, because the round has
    /// already left something.
    /// </summary>
    [RequiresGameDataFact]
    public void ARifleRoundGoesThroughOneBodyIntoTheNext()
    {
        bool previousBallistics = GameplayCVars.Ballistics.Value;
        GameplayCVars.Ballistics.Value = false;

        try
        {
            (WeaponTypeRegistry roster, GameMaterialRegistry materials) = LoadTables();
            WeaponType dragunov = roster.Weapons.Single(weapon => weapon.ScriptId == "WEAPON_ID_DRAGUNOV");
            Assert.Equal(3f, dragunov.PenetrationPower);
            Assert.True(materials.TryGet(Flesh, out GameMaterial flesh));
            Assert.Equal(1f, flesh.BulletResistance);

            PlayerCamera camera = new();
            camera.UseWeaponCycle([dragunov.WeaponId]);
            camera.Carry(dragunov, new AmmoType { AmmoId = 1, CalibreId = dragunov.CalibreId });
            camera.UseViewRig(LoadViewSet());
            camera.UseWalls(ObjectCollisionIndex.Build([]));
            camera.UseMaterials(materials);

            // Two bodies along the round, by distance from the muzzle: in at 2.0 m and out at 2.3 m, then in
            // at 5.0 m and out at 5.3 m. Each call answers the nearest skin crossing ahead of where it starts.
            double[] crossings = [2.0 * Metre, 2.3 * Metre, 5.0 * Metre, 5.3 * Metre];
            Vector3d muzzle = default;
            Vector3d heading = default;
            bool started = false;
            List<double> damagedAt = [];
            List<bool> heard = [];
            camera.Humans = (Vector3d origin, Vector3d end, float _, WeaponCategory _, double stop, bool alreadyHeard, out HumanHit hit) =>
            {
                hit = default;
                heard.Add(alreadyHeard);
                if (!started)
                {
                    started = true;
                    muzzle = origin;
                    Vector3d line = end - origin;
                    heading = line / line.Length;
                }

                double from = Vector3d.Dot(origin - muzzle, heading);
                double reach = (end - origin).Length;
                for (int i = 0; i < crossings.Length; i++)
                {
                    double ahead = crossings[i] - from;
                    if (ahead <= 0.0)
                    {
                        continue;
                    }

                    if (ahead >= stop || ahead > reach)
                    {
                        return false;
                    }

                    bool exit = (i & 1) == 1;
                    if (!exit)
                    {
                        damagedAt.Add(crossings[i]);
                    }

                    hit = new HumanHit(
                        muzzle + (heading * crossings[i]),
                        ahead,
                        Zone: 0,
                        LocalForward: 1f,
                        LocalSide: 0f,
                        Bone: 0,
                        Material: Flesh,
                        IsExit: exit,
                        Normal: new Vector3f((float)-heading.X, (float)-heading.Y, (float)-heading.Z));
                    return true;
                }

                return false;
            };

            InputState input = new();
            TickUntilReady(camera, input);
            input.BeginFrame();
            input.Keyboard.SetKey(KeyCode.Backspace, down: true);
            input.Mouse.SetButton(MouseButton.Left, down: true);
            camera.Tick(input, terrain: null);

            Assert.True(camera.FiredThisTick);
            Assert.Equal([2.0 * Metre, 5.0 * Metre], damagedAt);
            Assert.Equal(2, camera.HumanHits);
            Assert.Equal(2, camera.Impacts.Count(impact => impact.StruckHuman));

            // One round, heard once: only the first cast offers the near miss.
            Assert.False(heard[0]);
            Assert.All(heard.Skip(1), Assert.True);
        }
        finally
        {
            GameplayCVars.Ballistics.Value = previousBallistics;
        }
    }

    private static void TickUntilReady(PlayerCamera camera, InputState input)
    {
        for (int tick = 0; tick < 60
            && (camera.Weapons?.InHand is null || camera.Weapons.State != HumanWeaponState.Ready); tick++)
        {
            input.BeginFrame();
            camera.Tick(input, terrain: null);
        }

        Assert.Equal(HumanWeaponState.Ready, camera.Weapons!.State);
    }

    private static BoneAnimationSet LoadViewSet() => BoneAnimationSet.Read(File.ReadAllBytes(
        Path.Combine(GameData.RequireRoot(), "common", "anims", "005.iff")));

    private static (WeaponTypeRegistry Roster, GameMaterialRegistry Materials) LoadTables()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        WeaponTypeRegistry roster = new();
        roster.Register(loop.Natives);
        roster.Load(fileSystem, resources, loop.Natives);
        GameMaterialRegistry materials = new();
        materials.Register(loop.Natives);
        materials.Load(resources, loop.Natives);
        return (roster, materials);
    }
}
