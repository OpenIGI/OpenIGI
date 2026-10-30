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

        // The server's word on where this client's player was after its newest command it has run, against the prediction.
        if (session.EntityId is { } self && session.Latest is { } newest && newest.TryGet(self, out ReplicatedHuman own))
        {
            if (session.Reconciler.Check(session.LastRunCommandTick, own.Position.ToVector()) is { } correction)
            {
                world.MoveLocal(correction);
            }

            world.ApplyOwn(own);
        }

        if (session.SendCommand(world.LocalFrame, now) is { } sent)
        {
            session.Reconciler.Record(sent, world.LocalPosition);
        }

        session.Flush(now);
        if (session.Scoreboard is { } board)
        {
            foreach (ScoreboardLine line in board.Lines)
            {
                if (line.Entity >= 0 && line.Entity != session.EntityId)
                {
                    world.Name(line.Entity, line.Name);
                }
            }
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
            world.Show(session.Remote.Sample(moment, human.Id, out ReplicatedHuman sampled) ? sampled : human);
        }

        foreach (int id in world.Shown.Where(id => !present.Contains(id)).ToArray())
        {
            world.Hide(id);
        }

        world.ApplyWorld(latest, session.EntityId);
    }
}
