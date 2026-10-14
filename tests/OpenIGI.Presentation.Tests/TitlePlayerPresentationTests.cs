using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Presentation.Tests;

/// <summary>Title camera and weapon motion use the same tick pair without changing simulation.</summary>
public sealed class TitlePlayerPresentationTests
{
    /// <summary>Mouse changes render immediately between simulation ticks, and consumption does not rewind the camera.</summary>
    [Fact]
    public void Live_mouse_aim_is_not_interpolated_back_to_the_previous_tick()
    {
        LookPlayer player = new();
        TitlePlayerPresentation presentation = new();
        presentation.Reset(player);
        player.Heading = 0.4f;
        presentation.CaptureTick(player);
        Matrix3f preview = LookPlayer.Rotation(0.6f);
        presentation.SetLookPreview(preview);
        AssertDirection(preview.Column2, presentation.Camera(0.25f).Orientation.Row3);
        AssertDirection(preview.Column2, presentation.FirstPerson(0.25f)!.Orientation.Column2);
        Vector3f expectedOffset = preview.Transposed().Transform(LookPlayer.Offset);
        Vector3d origin = presentation.FirstPerson(0.25f)!.Origin;
        Assert.Equal(expectedOffset.X, (float)origin.X, 4);
        Assert.Equal(expectedOffset.Y, (float)origin.Y, 4);
        Assert.Equal(0.4f, player.Heading);
        player.Heading = 0.6f;
        presentation.CaptureTick(player);
        presentation.SetLookPreview(player.LookOrientation);
        AssertDirection(preview.Column2, presentation.Camera(0f).Orientation.Row3);
        presentation.SetLookPreview(null);
        AssertDirection(LookPlayer.Rotation(0.4f).Column2, presentation.Camera(0f).Orientation.Row3);
    }

    private static void AssertDirection(Vector3f expected, Vector3f actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.Z, actual.Z, 5);
    }

    private sealed class LookPlayer : ITitlePlayer, ITitleFirstPerson, ITitlePlayerLook
    {
        public static Vector3f Offset => new(10f, 20f, -5f);
        public float Heading { get; set; }
        public Vector3d Position => default;
        public Vector3d EyePosition => default;
        public Matrix3f LookOrientation => Rotation(Heading);
        public Matrix3f ViewOrientation => Matrix3f.FromRows(LookOrientation.Column1, LookOrientation.Column3, LookOrientation.Column2);
        public bool CanPreviewLook => true;
        public Matrix3f PreviewLook(float mouseX, float mouseY, float sensitivity, bool invertMouse) => Rotation(Heading + mouseX);
        public TitleFirstPersonView FirstPerson
        {
            get
            {
                Vector3f offset = LookOrientation.Transposed().Transform(Offset);
                return new(new Vector3d(offset.X, offset.Y, offset.Z), LookOrientation, 0.5f, 0, []);
            }
        }

        public void Tick(in TitlePlayerInput input) { }
        public static Matrix3f Rotation(float angle)
        {
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return new(c, -s, 0f, s, c, 0f, 0f, 0f, 1f);
        }
    }

    /// <summary>The camera, lens, arms origin and rigid attachment agree at a halfway frame.</summary>
    [Fact]
    public void Camera_and_hands_share_the_tick_fraction()
    {
        Player player = new();
        TitlePlayerPresentation presentation = new();
        presentation.Reset(player);
        BonePose previous = player.FirstPerson.Parts[0].Pose!;
        player.EyePosition = new Vector3d(8192.0, 0.0, 0.0);
        player.FieldOfViewScale = 0.5f;
        player.TickNumber++;
        presentation.CaptureTick(player);
        Camera3D eye = presentation.Camera(0.5f);
        Assert.Equal(new Vector3d(1.0, 0.0, 0.0), eye.Position);
        Assert.Equal(Camera3D.Default.Orientation.Row3, eye.Orientation.Row3);
        Assert.Equal(Camera3D.Default.Orientation.Row2, eye.Orientation.Row2);
        Assert.Equal(0.75f, presentation.FieldOfView(0.5f));
        TitleFirstPersonView hands = presentation.FirstPerson(0.5f)!;
        Assert.Equal(new Vector3d(4096.0, 0.0, 0.0), hands.Origin);
        Assert.Equal(50f, hands.Parts[1].Position.X);
        Assert.Equal(0.5f, hands.PoseInterpolation);
        Assert.Same(previous, hands.Parts[0].PreviousPose);
        Assert.Equal(new Vector3d(8192.0, 0.0, 0.0), player.EyePosition);
    }

    /// <summary>Pause/disabled smoothing uses the exact current frame; cuts and teleports reset history.</summary>
    [Fact]
    public void Disabled_smoothing_cutscenes_and_teleports_do_not_blend_stale_frames()
    {
        Player player = new();
        TitlePlayerPresentation presentation = new();
        presentation.Reset(player);
        player.EyePosition = new Vector3d(4096.0, 0.0, 0.0);
        presentation.CaptureTick(player);
        Assert.Equal(new Vector3d(1.0, 0.0, 0.0), presentation.Camera(1f).Position);
        Assert.Equal(1f, presentation.FirstPerson(1f)!.PoseInterpolation);
        player.IsCutscenePlaying = true;
        player.EyePosition = new Vector3d(8192.0, 0.0, 0.0);
        presentation.CaptureTick(player);
        Assert.Equal(new Vector3d(2.0, 0.0, 0.0), presentation.Camera(0.25f).Position);
        player.IsCutscenePlaying = false;
        presentation.CaptureTick(player);
        player.EyePosition = new Vector3d(409600.0, 0.0, 0.0);
        presentation.CaptureTick(player);
        Assert.Equal(new Vector3d(100.0, 0.0, 0.0), presentation.Camera(0.25f).Position);
    }

    private sealed class Player : ITitlePlayer, ITitleFirstPerson
    {
        private readonly BonePose _pose = BonePose.FromBones([Matrix3f.Identity], [default], [-1]);
        public Vector3d Position => EyePosition;
        public Vector3d EyePosition { get; set; }
        public Matrix3f ViewOrientation => Camera3D.Default.Orientation;
        public float FieldOfViewScale { get; set; } = 1f;
        public bool IsCutscenePlaying { get; set; }
        public long TickNumber { get; set; }
        public TitleFirstPersonView FirstPerson => new(EyePosition, Matrix3f.Identity, 0.5f, TickNumber,
            [new("arms", _pose, default, Matrix3f.Identity),
             new("weapon", null, new Vector3f(TickNumber * 100f, 0f, 0f), Matrix3f.Identity)]);
        public void Tick(in TitlePlayerInput input) { }
    }
}
