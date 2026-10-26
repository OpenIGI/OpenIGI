using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.Igi1.Computer;

/// <content>The map's clicks: double clicks and the wheel zoom, and a click moves the map to what it lands on.</content>
public sealed partial class ComputerMap
{
    // 0x46A330: only while no control is under the pointer.
    private void Trace()
    {
        bool clicked = false;
        int buttons = _computer.Input.Buttons;
        if (_computer.Panel.Selected is null)
        {
            bool doubled = false, rightDoubled = false;
            if ((_computer.Pressed & Igi1Computer.Left) != 0)
            {
                if (_clickTime == 0)
                {
                    clicked = true;
                    _clickX = _computer.PointerX;
                    _clickY = _computer.PointerY;
                    _clickTime = DoubleClick;
                }
                else
                {
                    doubled = true;
                    _clickTime = 0;
                }
            }

            if ((_computer.Pressed & Igi1Computer.Right) != 0)
            {
                if (_rightClickTime == 0)
                {
                    _rightClickTime = DoubleClick;
                }
                else
                {
                    rightDoubled = true;
                    _rightClickTime = 0;
                }
            }

            if (((buttons & Igi1Computer.WheelUp) != 0 && (_lastButtons & Igi1Computer.WheelUp) == 0) || doubled)
            {
                if (_computer.Zoom < 3)
                {
                    _computer.Zoom++;
                    SetFieldOfView(Tan(ZoomAngles[_computer.Zoom]));
                    Startup.Start();
                }
            }

            if (((buttons & Igi1Computer.WheelDown) != 0 && (_lastButtons & Igi1Computer.WheelDown) == 0) || rightDoubled)
            {
                if (_computer.Zoom != 0)
                {
                    _computer.Zoom--;
                    SetFieldOfView(Tan(ZoomAngles[_computer.Zoom]));
                    Startup.Start();
                }
            }
        }

        if (_clickTime > 0)
        {
            _clickTime--;
        }

        if (_rightClickTime > 0)
        {
            _rightClickTime--;
        }

        if (clicked)
        {
            Pick(_clickX, _clickY);
        }
        else if (Target is { } target)
        {
            if (_computer.World.TryLocate(target, out Vector3d at))
            {
                LookAt(at);
            }
            else
            {
                ClearTarget();
            }
        }

        _lastButtons = buttons;
    }

    // A click cast from the camera the picture was last drawn from, through the pointer.
    private void Pick(float mx, float my)
    {
        float w = _computer.Width, h = _computer.Height;
        if (!(mx >= 0f) || !(my >= 0f) || !(w > mx) || !(h > my))
        {
            return;
        }

        IIgi1ComputerWorld world = _computer.World;
        float fov = CameraFieldOfView;
        double sx = (((double)(mx + mx) - w) * fov) / w;
        double sy = (((double)(my + my) - h) * (_computer.PixelAspect * fov)) / w;
        Matrix3f m = View;
        Vector3d dir = new(
            (m.M12 * sy) + (m.M11 * sx) + m.M13,
            (m.M22 * sy) + (m.M21 * sx) + m.M23,
            (m.M32 * sy) + (m.M31 * sx) + m.M33);
        double length = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y) + (dir.Z * dir.Z));
        Vector3d start = CameraPosition;
        Vector3d end = start + (dir * (Far / length));
        bool found = world.TryLine(start, end, out Vector3d hit, out GameTask? struck);
        double near = fov * Near;

        GameTask? human = null;
        foreach (ComputerObject candidate in world.Humans())
        {
            if (Flat(candidate.Position, hit) < near)
            {
                human = candidate.Task;
                break;
            }
        }

        bool mine = false;
        foreach (ComputerObject candidate in world.Mines())
        {
            if (Flat(candidate.Position, hit) < near)
            {
                world.SetOffMine(candidate.Task);
                mine = true;
                break;
            }
        }

        bool lens = false;
        foreach (ComputerObject candidate in world.Lenses())
        {
            if (Flat(candidate.Position, hit) < near)
            {
                SelectedLens = candidate.Task;
                lens = true;
                break;
            }
        }

        if (found)
        {
            if (!mine && !lens && (_computer.Limit?.Contains<X87>(hit) ?? true))
            {
                LookAt(hit);
            }

            if (human is not null)
            {
                SetTarget(human);
                LookAtTask(human);
                return;
            }

            if (struck is not null)
            {
                SetTarget(struck);
                return;
            }
        }

        ClearTarget();
    }

    // The distance across, squared: the click's point taken to the candidate's own height.
    private static double Flat(Vector3d at, Vector3d hit)
    {
        double dx = at.X - hit.X, dy = at.Y - hit.Y;
        return (dx * dx) + (dy * dy);
    }
}
