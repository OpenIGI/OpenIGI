using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Input;

namespace OpenIGI.Desktop.Tests.Completion;

/// <summary>What the player does for a goal, through the game's own paths.</summary>
internal sealed partial class MissionSolver
{
    private void Act(Goal goal)
    {
        _input.Keyboard.SetKey(KeyCode.E, down: false);
        switch (goal.Kind)
        {
            case GoalKind.Stand or GoalKind.Ride:
                StandIn((DeclaredTask)goal.Task!);
                break;
            case GoalKind.Take:
                if (Find((DeclaredTask)goal.Task!) is { } at)
                {
                    _player.Body.Position = at;
                }

                break;
            case GoalKind.Use:
                Use((DeclaredTask)goal.Task!);
                break;
            case GoalKind.Kill:
                Kill(goal.Task!);
                break;
            case GoalKind.Blow:
                Blow(goal.Task!);
                break;
        }
    }

    // Stands the player in the box, its feet at the centre and then its origin, whichever the area counts.
    private void StandIn(DeclaredTask area)
    {
        if (!area.TryGet("Position", out Vector3d centre))
        {
            return;
        }

        if (_goalTicks == 0)
        {
            Note($"area {area.Name} at {Fmt(centre)}, dimensions {(area.TryGetValue("Dimensions", out object? d) ? d : "?")}, criteria '{Text(area, "Criteria")}'");
        }

        bool byOrigin = (_goalTicks / 30) % 2 == 1;
        _player.Body.Position = byOrigin ? centre - new Vector3d(0, 0, OriginAboveFeet) : centre;
    }

    private Vector3d? Find(DeclaredTask task)
    {
        if (_mission.GunPickups?.Items.FirstOrDefault(p => ReferenceEquals(p.Task, task)) is { } gun)
        {
            return gun.Position;
        }

        if (_mission.GenericPickups?.Items.FirstOrDefault(p => ReferenceEquals(p.Task, task)) is { } item)
        {
            return item.Position;
        }

        return task.TryGet("Position", out Vector3d position) ? position : null;
    }

    // Stands beside the interaction where it can be offered, turns until the scan offers this one, presses the use key on its
    // edge, and stays while it runs.
    private void Use(DeclaredTask task)
    {
        if (_mission.Interactions is not { } interactions || !interactions.TryGet(task, out WorldInteraction? interaction) || interaction is null)
        {
            return;
        }

        Vector3d anchor = interaction.Anchor;
        if (_goalTicks == 0 || _spots.Count == 0)
        {
            _spots.Clear();
            _spot = -1;
            double eyeHeight = _player.Body.Eye.Z - _player.Body.Position.Z;
            List<Vector3d> fallback = [];
            foreach (double reach in new[] { 4096.0, 6144.0, 3072.0, 8192.0 })
            {
                for (int step = 0; step < 16; step++)
                {
                    double angle = step * Math.PI / 8.0;
                    Vector3d feet = new(anchor.X + (Math.Cos(angle) * reach), anchor.Y + (Math.Sin(angle) * reach), anchor.Z - OriginAboveFeet);
                    (interaction.CanOfferFrom(feet + new Vector3d(0, 0, eyeHeight)) ? _spots : fallback).Add(feet);
                }
            }

            _spots.AddRange(fallback);
            NextSpot();
        }

        int phase = _goalTicks - _spotSince;
        if (phase == 0)
        {
            _player.Body.Position = _spots[_spot];
            return;
        }

        if (phase == 4)
        {
            Vector3d eye = _player.Body.Eye;
            for (int yawStep = 0; yawStep < 72 && _useStep == 0; yawStep++)
            {
                for (int pitchStep = -8; pitchStep <= 8 && _useStep == 0; pitchStep++)
                {
                    float yaw = (float)(yawStep * Math.PI / 36.0);
                    float pitch = (float)(pitchStep * Math.PI / 24.0);
                    if (ReferenceEquals(interactions.Resolve(eye, yaw, pitch), interaction))
                    {
                        _player.Body.Yaw = yaw;
                        _player.Body.Pitch = pitch;
                        _useStep = 1;
                    }
                }
            }

            if (_useStep == 0)
            {
                Note($"no offer from {Fmt(_player.Body.Position)} (anchor {Fmt(anchor)}, inWorld {interaction.IsInWorld}, available {interaction.IsAvailable}, offerable {interaction.CanOfferFrom(eye)})");
                NextSpot();
            }

            return;
        }

        if (phase == 5 && _useStep == 1)
        {
            _input.Keyboard.SetKey(KeyCode.E, down: true);
            _useStep = 2;
            Note($"pressed use at {Fmt(_player.Body.Position)} yaw {_player.Body.Yaw:0.00} pitch {_player.Body.Pitch:0.00}, {interaction.Type}, {interaction.DurationTicks} ticks");
            return;
        }

        // Holds the key while it runs -- a timed state is cancelled the moment Use is released (0x460EE0) -- then lets go
        // and tries again from the next place if nothing came of it.
        if (_useStep == 2 && phase <= 5 + interaction.DurationTicks + 15)
        {
            _input.Keyboard.SetKey(KeyCode.E, down: true);
        }

        if (_useStep == 2 && phase > 5 + Math.Max(interaction.DurationTicks, 30) + 90)
        {
            Note($"no result from the press at {Fmt(_spots[_spot])}");
            NextSpot();
        }
    }

    private void NextSpot()
    {
        _spot = (_spot + 1) % Math.Max(1, _spots.Count);
        _spotSince = _goalTicks + 1;
        _useStep = 0;
    }

    // The LAW rocket's blast (0x428390): radius 10240, falloff 7168, damage 5, weapon 12.
    private void Kill(GameTask soldier)
    {
        if (_goalTicks % 30 != 0)
        {
            return;
        }

        foreach (SoldierView view in _mission.Soldiers?.SoldierViews ?? [])
        {
            if (ReferenceEquals(view.Soldier, soldier) && !view.IsDead)
            {
                Rocket(view.Position + new Vector3d(0, 0, 2048));
            }
        }
    }

    private void Blow(GameTask target)
    {
        if (_goalTicks % 30 != 0)
        {
            return;
        }

        if (_mission.Destructibles?.Objects.FirstOrDefault(o => ReferenceEquals(o.Owner, target)) is { } item)
        {
            Rocket(item.PositionUnits);
        }
        else if (target is DeclaredTask declared && declared.TryGet("Position", out Vector3d at))
        {
            Rocket(at);
        }
    }

    // Every soldier death, as it happens: a mission that fails on one says which.
    private void WatchDeaths()
    {
        foreach (SoldierView view in _mission.Soldiers?.SoldierViews ?? [])
        {
            if (view.IsDead && _dead.Add(view.Soldier))
            {
                Note($"{view.Soldier.Name} died at {Fmt(view.Position)}, player at {Fmt(_player.Body.Position)}");
            }
        }
    }

    private void Rocket(Vector3d at) => _game.Blasts.Explode(
        at,
        MissileExplosion.RadiusUnits,
        MissileExplosion.FalloffUnits,
        MissileExplosion.DamageFactor,
        WeaponCategory.Grenade,
        sourceWeaponId: DestructibleObjectRegistry.CarDestroyingWeaponId);
}
