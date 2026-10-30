using OpenIGI.Core.Graphics;
using OpenIGI.Presentation;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Presentation.Igi1.World;
using OpenIGI.Rendering.Headless;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>
/// The parts of a weapon that hang off bones of their own, and therefore animate through a reload.
/// </summary>
/// <remarks>
/// Retail's <c>GunClip</c>, <c>GunCABarrel</c> and <c>Gunx2</c> render through <c>0x472880</c>, which
/// asks the human for attachment 2 (bone 52); <c>GunMovingPart</c> and <c>GunCAClip</c> render through
/// <c>0x477B10</c>, which asks for attachment 3 (bone 55). Both handlers replace the part's whole
/// transform with the answer, so a magazine and a slide are driven by bones the clips move independently
/// of bone 3 — which is what makes those parts move against the gun body at all.
/// </remarks>
public sealed class HeldWeaponAttachmentTests
{
    /// <summary>How many ticks of a clip to sample, at the retail 30 Hz.</summary>
    private const int ClipTicks = 46;

    // The shipped roster is 24 weapon.qvm scripts; read it once for the whole class.
    private static readonly Lazy<WeaponTypeRegistry> Roster = new(static () =>
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        WeaponTypeRegistry roster = new();
        roster.Register(loop.Natives);
        roster.Load(fileSystem, resources, loop.Natives);
        return roster;
    });

    [RequiresGameDataFact]
    public void TheGlocksMagazineLeavesTheGunOverTheReload()
    {
        WeaponType glock = Glock;
        (double magazine, double _) = PartTravelAgainstBody(glock, glock.ReloadAnimation, boneDriven: true);

        // Bone 52 swings the magazine roughly 0.2 m clear of the body and back over the clip. Before the
        // fix the magazine was welded to the body and this was exactly zero.
        Assert.True(
            magazine > 0.1,
            $"The magazine should leave the gun over a reload; it moved {magazine:F3} m against the body.");
    }

    [RequiresGameDataFact]
    public void TheGlocksSlideRacksOverBothTheReloadAndTheShot()
    {
        WeaponType glock = Glock;
        (double _, double reloadSlide) = PartTravelAgainstBody(glock, glock.ReloadAnimation, boneDriven: true);
        (double _, double fireSlide) = PartTravelAgainstBody(glock, glock.FireAnimation1, boneDriven: true);

        // A slide travels a centimetre where the magazine travels two decimetres, so the bar is set
        // where a rack is still unmistakably a rack. A welded part reads as exactly zero, so there is
        // no noise floor to clear -- only the risk of calling a millimetre of drift an animation.
        Assert.True(
            reloadSlide > 0.005,
            $"The slide should rack over a reload; it moved {reloadSlide:F4} m against the body.");
        Assert.True(
            fireSlide > 0.005,
            $"The slide should rack on a shot; it moved {fireSlide:F4} m against the body.");
    }

    [RequiresGameDataFact]
    public void ACallerWithNoViewRigKeepsTheAuthoredPlacement()
    {
        // A dropped pickup, a soldier's weapon and a missile pass no bone attachments, and retail gates
        // all three handlers on the first-person view being the one drawing (0x4E81F0 + 92). Those parts
        // must therefore stay exactly where the magic-object tree put them, however the rig is posed.
        WeaponType glock = Glock;
        (double magazine, double slide) = PartTravelAgainstBody(glock, glock.ReloadAnimation, boneDriven: false);

        // Float round-trip through the two rigid transforms leaves a few microns; nothing moves.
        Assert.True(magazine < 1e-4, $"The magazine drifted {magazine:E2} m without a rig to drive it.");
        Assert.True(slide < 1e-4, $"The slide drifted {slide:E2} m without a rig to drive it.");
    }

    [RequiresGameDataFact]
    public void EveryWeaponPartClassResolvesToOneOfTheTwoAttachmentBones()
    {
        (LoadedMission mission, MagicObjectRegistry magic) = Load();
        string[] expected =
        [
            "TASKTYPE_CABARREL",
            "TASKTYPE_CACLIP",
            "TASKTYPE_GUNCLIP",
            "TASKTYPE_GUNMOVINGPART",
            "TASKTYPE_GUNX2",
        ];

        List<string> found = [];
        foreach (WeaponType weapon in Roster.Value.Weapons)
        {
            if (string.IsNullOrWhiteSpace(weapon.GunModel)
                || !WorldObject.TryBuild(weapon.GunModel, mission.TryGetModel, magic, out WorldObject? root)
                || root is null)
            {
                continue;
            }

            found.AddRange(root.Walk()
                .Where(part => magic.TryGet(part.Name, out MagicObjectDefinition _))
                .Select(part =>
                {
                    magic.TryGet(part.Name, out MagicObjectDefinition definition);
                    return magic.TaskTypeName(definition.TaskType);
                })
                .Where(expected.Contains));
        }

        // All five part classes of 0x477220, 0x472670 and 0x46DD00 are used by the shipped roster, so a
        // renderer that only knows about GUNX2 leaves most of every weapon static.
        Assert.Equal(expected, found.Distinct().Order());
    }

    private static WeaponType Glock =>
        Roster.Value.Weapons.Single(static w => w.ScriptId == "WEAPON_ID_GLOCK");

    /// <summary>How far the magazine and the moving part travel against the gun body over one clip.</summary>
    /// <param name="weapon">The weapon to hold.</param>
    /// <param name="animation">The view-rig clip to play.</param>
    /// <param name="boneDriven">Whether the caller supplies the two bone attachments.</param>
    /// <returns>The widest displacement of each part from where it started, relative to the body, in metres.</returns>
    private static (double Magazine, double MovingPart) PartTravelAgainstBody(
        WeaponType weapon,
        int animation,
        bool boneDriven)
    {
        (LoadedMission mission, MagicObjectRegistry magic) = Load();
        BoneAnimationSet viewSet = BoneAnimationSet.Read(File.ReadAllBytes(
            Path.Combine(GameData.RequireRoot(), "common", "anims", "005.iff")));
        HumanView view = new(viewSet);
        Assert.NotNull(view.PlayWeaponAnimation(animation, loops: false));

        using RecordingGraphicsDevice device = new();
        using HeldWeaponRenderer renderer = new(device, Resources(), magic);
        Assert.True(renderer.Load(mission, weapon.GunModel));

        Camera3D camera = new(Vector3d.Zero, Matrix3f.Identity);
        Vector3f[]? firstMagazine = null;
        Vector3f[]? firstMovingPart = null;
        double magazine = 0d;
        double movingPart = 0d;

        for (int tick = 0; tick < ClipTicks; tick++)
        {
            device.MeshDraws.Clear();
            renderer.Draw(
                view.HeldWeapon,
                camera,
                Matrix4f.Identity,
                firedThisTick: false,
                shotSequence: 0,
                WeaponDrawParts.All,
                boneDriven ? view.HeldWeaponSecondary : null,
                boneDriven ? view.HeldWeaponTertiary : null);

            // The parts draw in magic-object tree order, one mesh each however many subsets they have.
            // The Glock's tree is body, muzzle flame, moving part, magazine; the flame is unfired here.
            Matrix4f[] drawn = [.. PartTransforms(device)];
            Assert.Equal(3, drawn.Length);
            Vector3f[] thisMovingPart = InBodyFrame(drawn[1], drawn[0]);
            Vector3f[] thisMagazine = InBodyFrame(drawn[2], drawn[0]);

            firstMovingPart ??= thisMovingPart;
            firstMagazine ??= thisMagazine;
            movingPart = Math.Max(movingPart, Displacement(firstMovingPart, thisMovingPart));
            magazine = Math.Max(magazine, Displacement(firstMagazine, thisMagazine));

            view.Tick();
        }

        return (magazine, movingPart);
    }

    /// <summary>The transform each drawn part was placed by, one entry per mesh.</summary>
    private static IEnumerable<Matrix4f> PartTransforms(RecordingGraphicsDevice device)
    {
        uint last = 0;
        foreach (RecordingGraphicsDevice.MeshDrawCall draw in device.MeshDraws)
        {
            if (draw.Mesh.Id != last)
            {
                last = draw.Mesh.Id;
                yield return draw.ModelView;
            }
        }
    }

    /// <summary>Traces a part's frame into the gun body's, as four probe points 5 cm apart.</summary>
    /// <param name="part">The part's model-view.</param>
    /// <param name="body">The gun body's model-view.</param>
    /// <returns>The probes in the body's frame, in metres.</returns>
    /// <remarks>
    /// A part welded to the body holds every probe constant however the arm swings, so any variation is
    /// the part moving on the gun. Probing the axes as well as the origin catches a part that turns
    /// without sliding, which the origin alone would miss.
    /// </remarks>
    private static Vector3f[] InBodyFrame(Matrix4f part, Matrix4f body)
    {
        const float armMetres = 0.05f;
        Vector3f[] probes =
        [
            Vector3f.Zero,
            new(armMetres, 0f, 0f),
            new(0f, armMetres, 0f),
            new(0f, 0f, armMetres),
        ];

        (Matrix3f partRotation, Vector3f partOrigin) = Rigid(part);
        (Matrix3f bodyRotation, Vector3f bodyOrigin) = Rigid(body);

        // Transform applies the row-vector convention, which is the transpose of the matrix product --
        // so a transposed matrix through it is the ordinary product, and a plain one is the inverse.
        return [.. probes.Select(probe =>
        {
            Vector3f inView = partRotation.Transposed().Transform(probe);
            return bodyRotation.Transform(new Vector3f(
                inView.X + partOrigin.X - bodyOrigin.X,
                inView.Y + partOrigin.Y - bodyOrigin.Y,
                inView.Z + partOrigin.Z - bodyOrigin.Z));
        })];
    }

    /// <summary>How far the furthest probe moved between two readings, in metres.</summary>
    private static double Displacement(Vector3f[] from, Vector3f[] to) =>
        from.Zip(to, static (a, b) => Math.Sqrt(
            ((double)a.X - b.X) * ((double)a.X - b.X)
            + (((double)a.Y - b.Y) * ((double)a.Y - b.Y))
            + (((double)a.Z - b.Z) * ((double)a.Z - b.Z)))).Max();

    /// <summary>Splits a rigid transform into the rotation and the translation column.</summary>
    private static (Matrix3f Rotation, Vector3f Origin) Rigid(Matrix4f m) => (
        new Matrix3f(m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33),
        new Vector3f(m.M14, m.M24, m.M34));

    private static ResourceRegistry Resources()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        return new ResourceRegistry(fileSystem);
    }

    private static (LoadedMission Mission, MagicObjectRegistry Magic) Load()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        return (new MissionLoader(registry, fileSystem, resources, levelLoader).Load(1), magic);
    }
}
