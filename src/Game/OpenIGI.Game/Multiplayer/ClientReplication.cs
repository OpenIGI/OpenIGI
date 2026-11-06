namespace OpenIGI.Game.Multiplayer;

/// <summary>Each tick of a client: its own command sent up, and every other player shown where the snapshots put it.</summary>
/// <remarks>
/// Run after the client's own tick. The other players are sampled at the session clock's interpolation moment, so each is
/// drawn between two snapshots, behind the server by the interpolation delay; one the newest snapshot no longer holds is
/// taken out.
/// </remarks>
public static class ClientReplication
{
    /// <summary>Runs one client tick's replication.</summary>
    /// <param name="session">The client's session.</param>
    /// <param name="world">The client's copy of the level.</param>
    /// <param name="now">The current time in seconds.</param>
    public static void Tick(ClientSession session, IReplicatedClientWorld world, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(world);
        session.Receive(now);

        // Nothing of a level the server has begun is shown on the one this client is leaving, nor commanded from it.
        if (session.Behind)
        {
            session.Flush(now);
            return;
        }

        // A new round's level, put back here as the server's was, before anything it sent after is taken.
        if (session.TakeLevelReset())
        {
            world.ResetToLevelStart();
        }

        // What the server handed this client's player -- a buy -- its own copy carries too.
        foreach ((string weapon, int rounds) in session.TakeGiven())
        {
            world.GiveOwn(weapon, rounds);
        }

        // The rest of the server's world first -- its lifts, its doors, and what it did to this client's player that the
        // client learns of only now, a knock among them -- so the server's word on the player below is weighed against a
        // prediction that has taken them. The snapshot's world stands after the server ran this client's command for
        // LastRunCommandTick; this client's own has run every command since, this tick's too, which goes out below.
        if (session.Latest is { } snapshot)
        {
            long seen = session.LastRunCommandTick != long.MinValue ? session.LastRunCommandTick : snapshot.Tick;
            int ahead = session.SentCommands.Count > 0 ? (int)Math.Clamp(session.SentCommands[^1].Tick + 1 - seen, 0, 600) : 0;
            world.ApplyWorld(snapshot, session.EntityId, ahead, session.Reconciler);
        }

        // The server's word on where this client's player was after its newest command it has run, against the prediction.
        if (session.EntityId is { } self && session.Latest is { } newest && newest.TryGet(self, out ReplicatedHuman own))
        {
            // A prediction made in a lift is weighed against the server's word as a place in the server's car, the car taken
            // from the same snapshot: the two ran the command at a different moment of the car's travel.
            ReplicatedLift[]? serverLifts = newest.Lifts;
            Func<int, Core.Numerics.Vector3d?> serverPlatform = index =>
                serverLifts is not null && (uint)index < (uint)serverLifts.Length ? serverLifts[index].Position.ToVector() : null;
            if (session.Reconciler.Check(session.LastRunCommandTick, own.Position.ToVector(), serverPlatform) is { } correction)
            {
                world.MoveLocal(correction, (own.Flags & ReplicatedHumanCondition.OnGround) != 0);
            }

            world.ApplyOwn(own);
        }

        ReadOnlySpan<float> frame = world.LocalFrame;
        int use = (int)Player.PlayerInputSlot.Activate;
        session.NoteUse(use < frame.Length && frame[use] > 0.5f, now);
        if (session.SendCommand(frame, now) is { } sent)
        {
            session.Reconciler.Record(sent, world.LocalPosition, world.LocalPlatform);
        }

        session.Flush(now);
        if (session.Scoreboard is { } board)
        {
            foreach (ScoreboardLine line in board.Lines)
            {
                if (line.Entity >= 0 && line.Entity != session.EntityId)
                {
                    world.Name(line.Entity, line.Name);
                    world.Skin(line.Entity, line.Skin);
                }
            }

            // The sides, which say whose names and revive marks this player is shown: the server's mode, the board's teams.
            int? ownTeam = session.EntityId is { } ownEntity ? TeamOf(board, ownEntity) : null;
            world.Sides(session.MatchState?.Mode ?? Modes.GameModes.Coop, entity => TeamOf(board, entity), ownTeam);
        }

        // The server level's running state, taken each time a new one comes.
        if (session.TakeLevel() is { } level)
        {
            world.ApplyLevel(level);
        }

        if (session.Latest is not { } latest || session.Clock is not { HasSample: true } clock)
        {
            return;
        }

        double moment = clock.InterpolationTick(now);
        HashSet<int> present = [];
        foreach (ReplicatedHuman human in latest.Humans)
        {
            if (human.Id == session.EntityId)
            {
                continue;
            }

            present.Add(human.Id);
            ReplicatedHuman shown = session.Remote.Sample(moment, human.Id, out ReplicatedHuman sampled) ? sampled : human;

            // One riding a lift is drawn where it stood on the server's car at that moment, on the car this client draws now:
            // that car is the server's projected on to this client's present, while the rider is drawn in the past -- where
            // the snapshots had it, it stood under the floor of a car going up and in the roof of one coming down.
            if (shown.Platform > 0
                && world.PlatformOrigin(shown.Platform - 1) is { } car
                && session.Remote.SampleLift(moment, shown.Platform - 1, out Core.Numerics.Vector3d carThen))
            {
                shown = shown with { Position = Int3.From(shown.Position.ToVector() + (car - carThen)) };
            }

            world.Show(shown);
        }

        foreach (int id in world.Shown.Where(id => !present.Contains(id)).ToArray())
        {
            world.Hide(id);
        }
    }

    private static int? TeamOf(Scoreboard board, int entity)
    {
        foreach (ScoreboardLine line in board.Lines)
        {
            if (line.Entity == entity)
            {
                return line.Team;
            }
        }

        return null;
    }
}
