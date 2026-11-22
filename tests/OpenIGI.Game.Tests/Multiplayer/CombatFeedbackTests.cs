using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The local player's combat feedback: the hit marker, and where the shots that struck it came from.</summary>
public sealed class CombatFeedbackTests
{
    // Looking along +Y with +X to the right, as a yaw of nought does.
    private static readonly Matrix3f LookingNorth = new(1f, 0f, 0f, 0f, 0f, 1f, 0f, 1f, 0f);

    [Fact]
    public void A_note_reads_back_as_it_was_written_with_the_level_as_minus_one()
    {
        CombatNote note = new(-1, 7, 0.5f, Killed: true, Head: false, new Vector3d(-1234, 5678, -90));
        BitWriter writer = new();
        note.Write(writer);
        BitReader reader = new(writer.ToArray());

        CombatNote back = Assert.NotNull(CombatNote.Read(ref reader));
        Assert.Equal((note.Attacker, note.Victim, note.Killed, note.From), (back.Attacker, back.Victim, back.Killed, back.From));
        Assert.Equal(0.5f, back.Damage, 0.01f);
    }

    [Fact]
    public void Ones_own_hit_shows_a_marker_that_fades_and_a_kill_shows_longer_and_red()
    {
        CombatFeedback feedback = new();
        feedback.Take([new CombatNote(1, 2, 0.2f, false, false, default)], own: 1, now: 10);
        Assert.Equal(1f, feedback.Shown(10, default, LookingNorth)?.Marker?.Strength);
        Assert.Null(feedback.Shown(10 + CombatFeedback.MarkerSeconds, default, LookingNorth));

        feedback.Take([new CombatNote(1, -1, 0f, Killed: true, Head: true, default)], own: 1, now: 20);
        HitMarker kill = Assert.NotNull(feedback.Shown(20 + CombatFeedback.MarkerSeconds, default, LookingNorth)?.Marker);
        Assert.True(kill.Killed && kill.Head);
        Assert.Equal(2, feedback.Hits);
        Assert.Equal(0.2f, feedback.Dealt, 0.001f);
    }

    [Fact]
    public void A_shot_that_strikes_one_points_where_it_came_from_as_the_view_turns()
    {
        CombatFeedback feedback = new();
        feedback.Take([new CombatNote(5, 1, 0.3f, false, false, new Vector3d(1000, 0, 0))], own: 1, now: 0);

        // From the right while looking north; from ahead once turned to face east.
        DamageMark right = Assert.Single(feedback.Shown(0.1, default, LookingNorth)!.Marks);
        Assert.Equal(MathF.PI / 2, right.Angle, 0.01f);
        Matrix3f lookingEast = new(0f, -1f, 0f, 0f, 0f, 1f, 1f, 0f, 0f);
        Assert.Equal(0f, Assert.Single(feedback.Shown(0.1, default, lookingEast)!.Marks).Angle, 0.01f);

        Assert.Null(feedback.Shown(CombatFeedback.MarkSeconds, default, LookingNorth));
        Assert.Equal(0.3f, feedback.Taken, 0.001f);
    }

    [Fact]
    public void The_rounds_damage_is_reported_as_it_ends_and_counted_afresh_in_the_next()
    {
        CombatFeedback feedback = new();
        MatchState live = new("elim", MatchPhase.Live, 1, -1, 60);
        Assert.Null(feedback.Shown(0, default, LookingNorth, live));
        feedback.Take([new CombatNote(1, 2, 0.6f, false, false, default), new CombatNote(3, 1, 0.25f, false, false, default)], own: 1, now: 1);

        RoundReport report = Assert.NotNull(feedback.Shown(5, default, LookingNorth, live with { Phase = MatchPhase.RoundOver })?.Report);
        Assert.Equal((0.6f, 0.25f, 1), (MathF.Round(report.Dealt, 2), MathF.Round(report.Taken, 2), report.Hits));

        Assert.Null(feedback.Shown(9, default, LookingNorth, live with { Round = 2 }));
        Assert.Equal(0, feedback.Hits);
    }

    [Fact]
    public void A_killing_hit_on_one_records_its_killer_and_when()
    {
        CombatFeedback feedback = new();
        feedback.Take([new CombatNote(5, 1, 0.3f, Killed: false, false, default)], own: 1, now: 2);
        Assert.Null(feedback.KilledBy);
        feedback.Take([new CombatNote(5, 1, 0.7f, Killed: true, false, default)], own: 1, now: 3);
        Assert.Equal((5, 3d), feedback.KilledBy);
    }

    [Fact]
    public void A_hit_counted_as_it_struck_marks_at_once_and_not_again_when_the_server_tells_of_it()
    {
        Player.ShotTally tally = new();
        CombatFeedback feedback = new() { Credit = tally, SoldierKillForeseen = () => false };
        feedback.Take([], own: 1, now: 0);

        // The client's round meets its copy of a soldier: counted there and then.
        tally.Hit(head: false);
        feedback.Take([], own: 1, now: 1);
        Assert.Equal(1f, feedback.Shown(1, default, LookingNorth)?.Marker?.Strength);

        // The server's note of the same hit a round trip later marks nothing new, and counts nothing twice.
        feedback.Take([new CombatNote(1, -1, 0f, Killed: false, Head: false, default)], own: 1, now: 1 + CombatFeedback.MarkerSeconds + 0.1);
        Assert.Null(feedback.Shown(1 + CombatFeedback.MarkerSeconds + 0.1, default, LookingNorth)?.Marker);
        Assert.Equal(1, tally.Hits);
    }

    [Fact]
    public void A_soldiers_death_the_server_tells_of_is_counted_once_whether_or_not_the_client_foresaw_it()
    {
        Player.ShotTally tally = new();
        bool foreseen = true;
        CombatFeedback feedback = new() { Credit = tally, SoldierKillForeseen = () => foreseen };
        feedback.Take([], own: 1, now: 0);

        // Foreseen: the client counted it as its round struck; the note counts nothing.
        tally.Hit(head: true);
        tally.Killed(head: true);
        feedback.Take([], own: 1, now: 1);
        feedback.Take([new CombatNote(1, -1, 0f, Killed: true, Head: true, default)], own: 1, now: 1.2);
        Assert.Equal(1, tally.Kills);

        // Not foreseen (a partner left it its last health, a grenade): the note counts it, and it marks red.
        foreseen = false;
        feedback.Take([new CombatNote(1, -1, 0f, Killed: true, Head: false, default)], own: 1, now: 5);
        Assert.Equal(2, tally.Kills);
        Assert.True(feedback.Shown(5, default, LookingNorth)?.Marker?.Killed);
    }

    [Fact]
    public void On_the_host_a_soldiers_death_is_counted_where_it_falls_and_never_from_the_note()
    {
        Player.ShotTally tally = new();
        CombatFeedback feedback = new() { Credit = tally };
        feedback.Take([], own: 0, now: 0);
        feedback.Take([new CombatNote(0, -1, 0f, Killed: true, Head: false, default)], own: 0, now: 1);
        Assert.Equal(0, tally.Kills);
    }

    [Fact]
    public void Others_hits_and_ones_own_harm_to_oneself_show_nothing()
    {
        CombatFeedback feedback = new();
        feedback.Take([new CombatNote(2, 3, 0.5f, false, false, default), new CombatNote(1, 1, 0.5f, false, false, default)], own: 1, now: 0);
        Assert.Null(feedback.Shown(0, default, LookingNorth));
    }
}
