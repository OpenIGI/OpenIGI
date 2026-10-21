using OpenIGI.Game.Igi2.Flow;
using OpenIGI.TestSupport;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's status messages (<c>StatusMessage_RunHandler</c>, <c>igi2 0x50FB30</c>): their text read a key at a time in the
/// language, their duration shared among the pieces or taken from their sound, and the screen that prints them
/// (<c>StatusMessageScreen_UpdateMessage</c>, <c>0x43D540</c>) telling them when they may end.
/// </summary>
/// <remarks>
/// The level's lines sit in containers that run them only while a part of the mission is under way; the two "Quicksave
/// done" lines hang straight off the level's dynamic tasks and run every tick, so they are the ones driven here, given
/// whatever text and sound a case needs.
/// </remarks>
public sealed class RetailIgi2StatusMessageTests
{
    /// <summary>A line straight under the level's dynamic tasks, <c>StatusMessage_3398</c>.</summary>
    private const int Quicksave = 3398;

    /// <summary>The other, <c>StatusMessage_3314</c>.</summary>
    private const int Quicksave2 = 3314;

    /// <summary>The keys of the first line of the intro, <c>StatusMessage_2992</c>'s.</summary>
    private static readonly string[] IntroKeys = ["CT11_INTRO_01A", "CT11_INTRO_01B", "CT11_INTRO_01C"];

    /// <summary>
    /// All thirty are typed; a text is a list of keys looked up in the language, a key no archive holds being its own text,
    /// and a sound's length is its sample's in whole ticks.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_lines_are_read_with_their_pieces()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(soundLengths: true);
        Igi2StatusMessageTask[] lines = [.. m.Tree.OfType<Igi2StatusMessageTask>()];
        Assert.Equal(30, lines.Length);

        Igi2StatusMessageTask objective = lines.Single(static s => s.Id == 352);
        Assert.Equal(("AreaActivate_152.nActive", "OBJ_1_COMPLETE", "statusmessage"), (objective.SendScript, objective.Text, objective.Sound));
        Assert.Equal((true, false, 2f), (objective.IsSendOnce, objective.IsCutsceneMessage, objective.Duration));
        Assert.Equal(("Objective 1 Complete.", 1, 21), (objective.CurrentText, objective.PieceCount, objective.TextLength));
        Assert.Equal("Quicksave done", m.Strings!.Get("Quicksave done"));

        // Three pieces, the last of which is current once they are counted.
        Igi2StatusMessageTask intro = lines.Single(static s => s.Id == 2992);
        Assert.Equal((true, "3111, 3125"), (intro.IsCutsceneMessage, intro.MorphTaskIds));
        string[] pieces = [.. IntroKeys.Select(m.Strings.Get)];
        Assert.Equal((3, pieces.Sum(static p => p.Length), pieces[2]), (intro.PieceCount, intro.TextLength, intro.CurrentText));

        // A line with no text has no pieces.
        Igi2StatusMessageTask chute = lines.Single(static s => s.Id == 2971);
        Assert.Equal((string.Empty, 0, null), (chute.Text, chute.PieceCount, chute.CurrentText));

        Assert.Equal((15, 471, 179), (m.Sounds.LengthTicks("statusmessage"), m.Sounds.LengthTicks("cv11_intro_01_white"), m.Sounds.LengthTicks("cs11_chute_open")));
        Assert.Equal(0, m.Sounds.LengthTicks("no such sound"));
    }

    /// <summary>
    /// Sent, a line is shown at once and printed from its ninth tick a letter every other tick, and has finished printing
    /// thirty ticks after its last letter. Its duration running out before then only stamps it afresh, so it ends when the
    /// duration next runs out: off the screen, finished display, and, said once, never sent again.
    /// </summary>
    /// <remarks>The level's leaving-the-area lines fire too, their areas not ported; only this line is looked at.</remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_line_ends_once_printed_and_its_duration_is_out()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2StatusMessageTask line = m.Tree.OfType<Igi2StatusMessageTask>().Single(static s => s.Id == Quicksave);
        line.SendScript = "1";
        Assert.Equal(("Quicksave done", 2f), (line.CurrentText, line.Duration));

        m.Step();
        Assert.Equal((true, true, 0, 0), (line.IsSent, line.IsDisplay, line.Slot, line.TickSent));
        Igi2StatusMessageSlot slot = m.Screen.Slots[line.Slot];
        Assert.Equal(("Quicksave done", 0), (slot.Text, slot.Printed));
        Assert.Contains("Quicksave done", m.Screen.Log);

        // Fourteen letters: the last is printed on the screen's tick 9 + 2 * 13, done counts from the tick after, and the
        // thirty-first tick of done is the screen's 38 + 2 * 14.
        int printed = 38 + (2 * 14);
        for (int tick = 1; tick < printed; tick++)
        {
            m.Step();
            Assert.False(line.IsFinishedPrinting);
        }

        m.Step();
        Assert.Equal((true, false, 14), (line.IsFinishedPrinting, line.IsFinishedDisplay, slot.Printed));

        // Two seconds are sixty ticks: stamped at 0, past it at 61 unprinted and stamped again, past that at 122.
        Assert.Equal(61, line.TickSent);
        while (m.Run.LevelFlow!.Tick < 122)
        {
            m.Step();
            Assert.False(line.IsFinishedDisplay);
        }

        m.Step();
        Assert.Equal((true, false, -1, 0), (line.IsFinishedDisplay, line.IsDisplay, line.Slot, line.FinishedDisplayCount));
        Assert.False(slot.IsUsed);
        Assert.Equal(1.0, Read(m, $"StatusMessage_{Quicksave}.isFinishedDisplay"));
        Assert.Equal(1.0, Read(m, $"StatusMessage_{Quicksave}.isSendt"));

        for (int tick = 0; tick < 100; tick++)
        {
            m.Step();
        }

        Assert.Equal((false, 0), (line.IsDisplay, line.FinishedDisplayCount));
        Assert.Equal(100.0, Read(m, $"StatusMessage_{Quicksave}.nTicksSinceFinishedDisplay"));
    }

    /// <summary>
    /// A line of several keys shows them in turn, each for its share of its sound's length by its letters; a cutscene's piece
    /// is laid out whole and kept out of the log, and the line ends, its sound cut, once the last piece's share is out.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Pieces_share_the_sounds_length()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(soundLengths: true);
        Igi2StatusMessageTask line = m.Tree.OfType<Igi2StatusMessageTask>().Single(static s => s.Id == Quicksave);
        line.Text = "CT11_INTRO_01A, CT11_INTRO_01B, CT11_INTRO_01C";
        line.Sound = "cv11_intro_01_white";
        line.IsCutsceneMessage = true;
        line.SendScript = "1";
        line.InitText();
        string[] pieces = [.. IntroKeys.Select(m.Strings!.Get)];

        m.Step();
        float duration = ((Fpu)(float)471 / 30.0).F;
        Assert.Equal(duration, line.Duration);
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "cv11_intro_01_white" && s.IsAlive);
        Assert.Equal((pieces[0], 0), (line.CurrentText, line.TickSent));
        Assert.DoesNotContain(pieces[0], m.Screen.Log);

        // Each piece stands until the level's clock passes its stamp by its share, and the next is stamped on that tick.
        int stamp = 0;
        for (int piece = 0; piece < pieces.Length; piece++)
        {
            int share = (int)Math.Truncate(((((Fpu)30.0 * duration) * (double)pieces[piece].Length) / (double)line.TextLength).Value);
            while (m.Run.LevelFlow!.Tick <= stamp + share)
            {
                Assert.Equal(pieces[piece], line.CurrentText);
                m.Step();
            }

            int now = m.Run.LevelFlow.Tick;
            m.Step();
            stamp = now;
            if (piece < pieces.Length - 1)
            {
                Assert.Equal((pieces[piece + 1], stamp), (line.CurrentText, line.TickSent));
            }
        }

        Assert.Equal((true, false), (line.IsFinishedDisplay, line.IsDisplay));
        Assert.DoesNotContain(m.Sounds.Playing, static s => s.Name == "cv11_intro_01_white" && s.IsAlive);
    }

    /// <summary>
    /// A line with no text is never put on the screen, so it never finishes printing and never ends: its sound plays, its
    /// duration is the sound's, and it stays sent and shown.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_line_without_text_never_ends()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(soundLengths: true);
        Igi2StatusMessageTask line = m.Tree.OfType<Igi2StatusMessageTask>().Single(static s => s.Id == Quicksave2);
        line.Text = string.Empty;
        line.Sound = "cs11_chute_open";
        line.SendScript = "1";
        line.InitText();

        for (int tick = 0; tick < 400; tick++)
        {
            m.Step();
        }

        Assert.Equal((true, true, false, -1), (line.IsSent, line.IsDisplay, line.IsFinishedDisplay, line.Slot));
        Assert.Equal(((Fpu)(float)179 / 30.0).F, line.Duration);
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "cs11_chute_open" && s.IsAlive);
        Assert.DoesNotContain(m.Screen.Slots, s => s.Message == line);
    }

    private static double Read(RetailIgi2Mission1 m, string expression) =>
        m.Run.Evaluate(Igi2LevelExpressions.Create(expression, "test", m.Run.Natives));
}
