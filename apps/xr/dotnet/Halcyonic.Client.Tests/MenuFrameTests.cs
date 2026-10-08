using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The menu's frames, sections, lines, side panels, footers and bar (ADR 0026): what they refuse.</summary>
public class MenuFrameTests
{
    private static Prompt Close => new(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);

    private static Prompt Action(string id, bool main = false, bool available = true, string? reason = null) =>
        new(id, id, GlazeIcon.Approve, main: main, available: available, reason: reason);

    private static Prompt Next(string words = "Next page") => new(Footer.NextPage, words, GlazeIcon.Next, PromptKind.NextPage);

    private static Prompt HoldToTalk => new("hold-to-talk", "Hold to talk", GlazeIcon.HoldToTalk, holds: true);

    private static Prompt Yes(bool available = true) => new("yes", "Yes, approve", GlazeIcon.Approve, PromptKind.Yes, available: available, reason: "Read to part 3 first");

    private static Prompt Cancel => new("cancel", "Cancel", GlazeIcon.Close, PromptKind.Cancel);

    private static StateBadge Waiting
    {
        get
        {
            var (tone, icon, fill, edge, turns) = StateLanguage.Look(WorkState.WaitingForYou);
            return new StateBadge(WorkState.WaitingForYou, StateLanguage.WordOf(WorkState.WaitingForYou), tone, icon, fill, edge, turns, breathes: true, count: 1, lastKnown: false);
        }
    }

    [Test]
    public void APromptKeepsTheOtherWordsItMayShowAndRefusesBlankOnes()
    {
        Assert.That(Action("plain").AlsoReads, Is.Empty);
        var talk = new Prompt("talk", VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true);
        Assert.That(talk.AlsoReads, Is.EqualTo(new[] { VoiceText.ListeningWords, VoiceText.WritingDownWords }), "Hold to talk also reads the voice's words, in every column");
        Assert.Throws<ArgumentException>(() => _ = new Prompt("talk", VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true, alsoReads: new[] { " " }));
    }

    [Test]
    public void HoldToTalkShowsWhereTheVoiceStandsTheSameInEveryColumnAtOneWidth()
    {
        var talk = new Prompt("talk", VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true);
        Assert.That(talk.Voiced(VoiceStage.Idle), Is.SameAs(talk));
        Assert.That(Action("plain").Voiced(VoiceStage.Listening).Words, Is.EqualTo("plain"), "only a held prompt shows the voice");

        var listening = talk.Voiced(VoiceStage.Listening);
        Assert.That((listening.Id, listening.Words, listening.Icon, listening.Holds), Is.EqualTo(("talk", VoiceText.ListeningWords, GlazeIcon.HoldToTalk, true)));
        Assert.That((listening.Listens, listening.Waits), Is.EqualTo((true, false)), "listening is the person talking: the active tone and the pulse, no wait");

        var writing = talk.Voiced(VoiceStage.WritingDown);
        Assert.That((writing.Words, writing.Icon), Is.EqualTo((VoiceText.WritingDownWords, GlazeIcon.WritingDown)));
        Assert.That((writing.Listens, writing.Waits), Is.EqualTo((false, true)), "writing down is under way, so it shimmers");

        foreach (var shown in new[] { talk, listening, writing })
        {
            Assert.That(shown.AlsoReads.Prepend(shown.Words), Is.EquivalentTo(new[] { VoiceText.HoldToTalk, VoiceText.ListeningWords, VoiceText.WritingDownWords }),
                "laid at the widest of the same words whatever it shows, so it never changes width under the hand");
        }
        Assert.Throws<ArgumentException>(() => _ = new Prompt("plain", "Plain", GlazeIcon.Next, listens: true), "only a held prompt listens");
    }

    [Test]
    public void AFooterVoicesOnlyTheHeldPromptTheVoiceRecordsFor()
    {
        var talk = new Prompt("talk", VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true);
        var footer = new Footer(Close, secondary: talk, farRight: Action("send", main: true));
        Assert.That(footer.Voiced("talk", VoiceStage.Idle), Is.SameAs(footer));
        Assert.That(footer.Voiced("other", VoiceStage.Listening), Is.SameAs(footer), "another column's hold changes nothing here");
        var voiced = footer.Voiced("talk", VoiceStage.WritingDown);
        Assert.That(voiced[PromptSlot.Secondary]!.Words, Is.EqualTo(VoiceText.WritingDownWords));
        Assert.That(voiced.All.Select(each => (each.Slot, each.Prompt.Id)), Is.EqualTo(footer.All.Select(each => (each.Slot, each.Prompt.Id))), "every prompt keeps its place");
        Assert.That(footer[PromptSlot.Secondary], Is.SameAs(talk), "the footer the column gave is untouched");
    }

    [Test]
    public void AFooterStandsEachPromptInItsPlaceLeftToRight()
    {
        var footer = new Footer(Close, rare: Action("stop"), secondary: Action("deny"), farRight: Action("approve", main: true));
        Assert.That(footer.All.Select(each => (each.Slot, each.Prompt.Id)), Is.EqualTo(new[]
        {
            (PromptSlot.Close, Footer.Close), (PromptSlot.Rare, "stop"), (PromptSlot.Secondary, "deny"), (PromptSlot.FarRight, "approve"),
        }));
        Assert.That(footer.Confirming, Is.False);
        Assert.That(footer[PromptSlot.Free], Is.Null, "the free middle holds nothing until a confirmation");
    }

    [Test]
    public void AFooterRefusesASecondPromptInASlotAndAnythingInThePlaceOfAnother()
    {
        var footer = new Footer(Close, rare: Action("stop"), secondary: Action("deny"), farRight: Action("approve", main: true));
        Assert.Throws<InvalidOperationException>(() => footer.With(PromptSlot.FarRight, Action("again", main: true)), "two main actions");
        Assert.Throws<InvalidOperationException>(() => footer.With(PromptSlot.Secondary, Action("second")), "two secondary actions");
        Assert.Throws<InvalidOperationException>(() => footer.With(PromptSlot.Rare, Action("rare")), "two rare actions");
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(close: Action("close")), "only Close stands far left");
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(farRight: Close), "Close stands far left");
        Assert.Throws<InvalidOperationException>(() => new Footer().With(PromptSlot.Free, Action("middle")), "only Yes takes the free middle");
    }

    [Test]
    public void TheMainActionStandsOnlyAtTheFarRightAndOnlyAnActionIsOne()
    {
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(secondary: Action("approve", main: true)));
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(rare: Action("approve", main: true)));
        Assert.Throws<ArgumentException>(() => _ = new Prompt(Footer.NextPage, "Next page", GlazeIcon.Next, PromptKind.NextPage, main: true), "paging is never the main action");
        Assert.Throws<ArgumentException>(() => _ = new Prompt("yes", "Yes, approve", GlazeIcon.Approve, PromptKind.Yes, main: true), "nor a confirmation");
        Assert.Throws<ArgumentException>(() => _ = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close, main: true));
        var plain = new Footer(farRight: Action("send"));
        Assert.That(plain[PromptSlot.FarRight]!.DrawnAsMain, Is.False, "an action at the far right that isn't the main action is drawn plain");
    }

    [Test]
    public void AnUnavailablePromptKeepsItsPlaceDrawnQuietAndItsReasonIsThePagesLastContentLine()
    {
        var footer = new Footer(Close, farRight: Action("make-recap", main: true, available: false, reason: "The companion is still answering."));
        var main = footer[PromptSlot.FarRight]!;
        Assert.That((main.Main, main.Available, main.DrawnAsMain), Is.EqualTo((true, false, false)), "it keeps its place, drawn quiet");
        Assert.That(footer.Reason, Is.EqualTo("The companion is still answering."));
        Assert.That(new Footer(Close, farRight: Action("go", main: true)).Reason, Is.Null);
        Assert.That(Action("ready", reason: "never said").Reason, Is.Null, "an available prompt has no reason");

        // Answers waiting to be chosen say why Send answer waits: its reason keeps its words but isn't drawn.
        var send = new Prompt("send", "Send answer", GlazeIcon.SendAnswer, main: true, available: false, reason: "Choose an answer first.", pageExplains: true);
        Assert.That((send.Reason, send.PageExplains), Is.EqualTo(("Choose an answer first.", true)));
        Assert.That(new Footer(Close, farRight: send).Reason, Is.Null, "the page's answers already say it");
        var stop = new Prompt("stop", "Stop", GlazeIcon.Stop, available: false, reason: "It already stopped.");
        Assert.That(new Footer(Close, rare: stop, farRight: send).Reason, Is.EqualTo("It already stopped."), "another prompt's reason still shows");
        Assert.Throws<ArgumentException>(() => _ = Action("approve", available: false), "an action that can't be taken says why");
        Assert.Throws<ArgumentException>(() => _ = new Prompt("yes", "Yes, approve", GlazeIcon.Approve, PromptKind.Yes, available: false), "so does a locked Yes");
        var frame = new MenuFrame("New project", footer);
        Assert.That(frame.Reason, Is.EqualTo("The companion is still answering."));
    }

    [Test]
    public void ALongListPagesByNextPageAloneAtTheFarRightPlainOrBesideTheMainAction()
    {
        var tasks = new Footer(Close).WithNext(Next());
        var next = tasks[PromptSlot.FarRight]!;
        Assert.That((next.Kind, next.DrawnAsMain), Is.EqualTo((PromptKind.NextPage, false)), "Next page at the far right is drawn plain");
        Assert.That(tasks[PromptSlot.Rare], Is.Null, "nothing pages back: the last page's prompt goes to the first");

        var projects = new Footer(Close, farRight: Action("new-project", main: true)).WithNext(Next());
        Assert.That(projects[PromptSlot.Secondary]!.Kind, Is.EqualTo(PromptKind.NextPage), "where a main action holds the far right, Next page is secondary");
        Assert.That(projects[PromptSlot.FarRight]!.Id, Is.EqualTo("new-project"));
        var built = new Footer(Close, secondary: Next(), farRight: Action("new-project", main: true));
        Assert.That(built[PromptSlot.Secondary]!.Kind, Is.EqualTo(PromptKind.NextPage), "the constructor takes Next page beside a main action, as WithNext does");

        Assert.Throws<InvalidOperationException>(() => _ = new Footer(secondary: Next(), farRight: Action("send")), "Next page is secondary only beside a main action");
        Assert.Throws<InvalidOperationException>(() => new Footer(Close, secondary: Action("hide"), farRight: Action("add-task", main: true)).WithNext(Next()), "no place left for it");
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(rare: Next()), "it never takes the rare place");
        Assert.Throws<ArgumentException>(() => new Footer(Close).WithNext(Action("next")));

        Assert.That((Footer.NextPageWords(0, 3), Footer.NextPageWords(1, 3), Footer.NextPageWords(2, 3)), Is.EqualTo(("Next page", "Next page", "First page")));
        Assert.Throws<ArgumentOutOfRangeException>(() => Footer.NextPageWords(0, 1), "a list of one page doesn't page");
        Assert.Throws<ArgumentOutOfRangeException>(() => Footer.NextPageWords(3, 3));
    }

    [Test]
    public void HoldToTalkIsTheSecondaryPromptAndOnlyAHeldPromptShowsTheMicrophone()
    {
        var waiting = new Footer(Close, secondary: HoldToTalk, farRight: Action("send-answer", main: true));
        Assert.That(waiting[PromptSlot.Secondary]!.Holds, Is.True);
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(rare: HoldToTalk));
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(farRight: HoldToTalk));
        Assert.Throws<ArgumentException>(() => _ = new Prompt("approve", "Approve", GlazeIcon.HoldToTalk), "the microphone only on a held prompt");
        Assert.Throws<ArgumentException>(() => _ = new Prompt("hold", "Hold to talk", GlazeIcon.HoldToTalk, main: true, holds: true), "a held prompt is plain");
        Assert.Throws<ArgumentException>(() => _ = new PageLine("Type my answer", action: "type", icon: GlazeIcon.HoldToTalk), "never a microphone on a row");
    }

    [Test]
    public void ASidePanelInItsFramesPlaceCarriesOnlyWhatItShowsEverythingOfWithCloseDetailsWhereCloseStood()
    {
        var details = new SidePanel("Details", facts: new[] { new SideFact("Seen", "just now") });
        var change = new Prompt("change", "Make text standard", GlazeIcon.Change, main: true, safeInPlace: true);
        var settings = new Footer(Close, farRight: change).InPlace(details);
        Assert.That(settings.All.Select(each => each.Prompt.Id), Is.EqualTo(new[] { SidePanel.Close, "change" }), "a setting's change, which its panel shows all of");
        Assert.That(settings[PromptSlot.Close], Is.SameAs(SidePanel.Footer[PromptSlot.Close]), "Close details where Close stood");

        var waiting = new Footer(Close, rare: Action("stop"), secondary: new Prompt("talk", "Hold to talk", GlazeIcon.HoldToTalk, holds: true),
            farRight: Action("send", main: true));
        var answer = waiting.InPlace(details);
        Assert.That(answer.All.Select(each => each.Prompt.Id), Is.EqualTo(new[] { SidePanel.Close }),
            "Send answer sends more than an answer's panel shows, and Hold to talk adds to it: only Close details, which brings the page back");

        var turning = new Footer(Close, farRight: Action("send", main: true)).WithNext(Next());
        var inParts = new SidePanel("An answer", lines: new[] { new PageLine("Part of it") }, parts: (0, 2));
        Assert.That(turning.InPlace(inParts).All.Select(each => (each.Slot, each.Prompt.Id)), Is.EqualTo(new[] { (PromptSlot.Close, SidePanel.Close), (PromptSlot.FarRight, Footer.NextPage) }),
            "the panel's own parts paged, at the far right once the main action beside it stays behind");
        Assert.That(turning.InPlace(details).All.Select(each => each.Prompt.Id), Is.EqualTo(new[] { SidePanel.Close }),
            "a panel of one part: the page's Next page would turn the page it hides");
        var onePart = new SidePanel("Changes", lines: new[] { new PageLine("All of it") }, parts: (0, 1));
        Assert.That(turning.InPlace(onePart).All.Select(each => each.Prompt.Id), Is.EqualTo(new[] { SidePanel.Close }), "nor one counted as a single part");

        var forgetting = Footer.Confirm(new Footer(Close, farRight: change), PromptSlot.FarRight,
            new Prompt("yes", "Yes, forget this computer", GlazeIcon.Change, PromptKind.Yes, safeInPlace: true), Cancel).InPlace(details);
        Assert.That(forgetting.All.Select(each => each.Prompt.Kind), Is.EqualTo(new[] { PromptKind.Close, PromptKind.Yes, PromptKind.Cancel }),
            "a Yes its panel shows all of, and Cancel always");
        var approving = Footer.Confirm(waiting, PromptSlot.FarRight, Yes(), Cancel).InPlace(details);
        Assert.That(approving.All.Select(each => each.Prompt.Kind), Is.EqualTo(new[] { PromptKind.Close, PromptKind.Cancel }), "no Yes its panel doesn't show all of");

        var connecting = new Footer(Close, farRight: new Prompt("connect", "Connect", GlazeIcon.ConnectProjects, main: true, available: false,
            reason: "Choose a folder first", safeInPlace: true)).InPlace(details);
        Assert.That(connecting.All.Select(each => each.Prompt.Id), Is.EqualTo(new[] { SidePanel.Close, "connect" }), "one its panel shows all of, though it can't be taken now");
        Assert.That(connecting.Reason, Is.EqualTo("Choose a folder first"), "saying why, as the page's last content line");
    }

    [Test]
    public void AConfirmationsYesTakesTheFreeMiddleAndCancelThePlaceOfTheFirstPress()
    {
        var waiting = new Footer(Close, rare: Action("stop"), secondary: Action("deny"), farRight: Action("approve", main: true));
        var confirming = Footer.Confirm(waiting, PromptSlot.FarRight, Yes(available: false), Cancel);
        Assert.That(confirming.All.Select(each => (each.Slot, each.Prompt.Kind)), Is.EqualTo(new[]
        {
            (PromptSlot.Close, PromptKind.Close), (PromptSlot.Free, PromptKind.Yes), (PromptSlot.FarRight, PromptKind.Cancel),
        }), "Close, Yes and Cancel; the other actions step aside, and the request's parts page by a row on the page");
        Assert.That(confirming.Confirming, Is.True);
        Assert.That(waiting[PromptSlot.Free], Is.Null, "Yes stands where nothing stood on that page");
        Assert.That(confirming.Reason, Is.EqualTo("Read to part 3 first"), "the locked Yes says why, as the page's last content line");
        Assert.That(confirming.All.Count(each => each.Prompt.DrawnAsMain), Is.Zero, "nothing in a confirmation is drawn as the main action");

        var reading = Footer.Confirm(waiting, PromptSlot.FarRight, null, Cancel);
        Assert.That(reading.All.Select(each => each.Slot), Is.EqualTo(new[] { PromptSlot.Close, PromptSlot.FarRight }),
            "before the last part has shown: Close and Cancel, no Yes");
        Assert.That(reading.Confirming, Is.True, "armed while the parts are read");
        var stopping = Footer.Confirm(waiting, PromptSlot.Rare, Yes(), Cancel);
        Assert.That(stopping[PromptSlot.Rare]!.Kind, Is.EqualTo(PromptKind.Cancel), "Cancel where Stop was pressed");
        Assert.Throws<ArgumentException>(() => Footer.Confirm(new Footer(Close), PromptSlot.FarRight, Yes(), Cancel), "no press there to undo");
        Assert.Throws<ArgumentException>(() => Footer.Confirm(waiting, PromptSlot.Close, Yes(), Cancel), "Close is no press to confirm");
        Assert.Throws<InvalidOperationException>(() => Footer.Confirm(confirming, PromptSlot.FarRight, Yes(), Cancel), "one confirmation at a time");
        Assert.Throws<ArgumentException>(() => Footer.Confirm(waiting, PromptSlot.FarRight, Action("yes"), Cancel));
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(farRight: Yes()), "Yes comes only with a confirmation");
        Assert.Throws<InvalidOperationException>(() => _ = new Footer(farRight: Cancel), "and so does Cancel");
        Assert.Throws<InvalidOperationException>(() => confirming.With(PromptSlot.Free, Yes()), "Yes stays where it stood while the parts page");
    }

    [Test]
    public void SectionsHaveOneChosenEachItsOwnKeyAndAStepNotReachedTakesNoPress()
    {
        var file = new[]
        {
            new FrameSection("waiting", "Waiting", chosen: true, waits: true),
            new FrameSection("activity", "Activity"),
            new FrameSection("changes", "Changes"),
            new FrameSection("checks", "Checks"),
        };
        var frame = new MenuFrame("Add rate limiting", new Footer(Close), subjectIsData: true, pill: Waiting, sections: file);
        Assert.That(frame.Sections.Single(section => section.Chosen).Key, Is.EqualTo("waiting"));
        Assert.That(frame.Pill!.Word, Is.EqualTo("Waiting for you"));

        Assert.Throws<ArgumentException>(() => _ = new MenuFrame("x", new Footer(), sections: new[] { new FrameSection("a", "A"), new FrameSection("b", "B") }), "none chosen");
        Assert.Throws<ArgumentException>(() => _ = new MenuFrame("x", new Footer(), sections: new[] { new FrameSection("a", "A", chosen: true), new FrameSection("b", "B", chosen: true) }));
        Assert.Throws<ArgumentException>(() => _ = new MenuFrame("x", new Footer(), sections: new[] { new FrameSection("a", "A", chosen: true), new FrameSection("a", "B") }));
        Assert.Throws<ArgumentException>(() => _ = new FrameSection("recap", "Recap", chosen: true, reached: false), "a step not reached can't be chosen");

        // A row still waiting for sections to join it keeps their slots, and may have none lit, as Settings alone before the first task.
        var alone = new MenuFrame("x", new Footer(), sections: MenuBar.SettingsAlone(chosen: false), sectionSlots: 4);
        Assert.That((alone.Sections.Single().Words, alone.SectionSlots), Is.EqualTo(("Settings", 4)));
        Assert.That(new MenuFrame("x", new Footer(), sections: file).SectionSlots, Is.EqualTo(4), "as many slots as sections unless given");
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new MenuFrame("x", new Footer(), sections: file, sectionSlots: 3), "a slot for each section");
        var passedOver = new FrameSection("questions", "Questions", reached: true);
        Assert.That(passedOver.Reached, Is.True, "a step passed over stays reached, so it can be chosen: the way back");
        Assert.Throws<ArgumentException>(() => _ = new FrameSection("", "Waiting"));
        Assert.Throws<ArgumentException>(() => _ = new FrameSection("waiting", " "));
    }

    [Test]
    public void ALineOnlySaysSomethingOrTakesThePersonSomewhere()
    {
        var opens = new PageLine("src/auth/rate-limit.ts", wordsAreData: true, icon: GlazeIcon.Change, fact: "2 min ago", chip: "Agent says",
            action: "open-file", key: "rate-limit", opens: true);
        Assert.That((opens.Pressable, opens.Opens, opens.Chip), Is.EqualTo((true, true, "Agent says")));
        var answer = new PageLine("Use Postgres", wordsAreData: true, action: "answer", key: "0", choice: true, chosen: true);
        Assert.That(answer.Choice, Is.True);
        var part = new PageLine("Write and apply a migration that moves sessions into their own table.", wordsAreData: true, rows: 3, fromRow: 3);
        Assert.That((part.Rows, part.FromRow), Is.EqualTo((3, (int?)3)), "a part of a long request shows from where the one before ended");
        Assert.That(new PageLine("Use Postgres", wordsAreData: true, rows: 2).FromRow, Is.Null, "words not in parts are cut with an ellipsis");
        Assert.That(new PageLine("Write and apply", wordsAreData: true, rows: 3, fromRow: 0).FromRow, Is.EqualTo(0), "the first part is a part too");
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PageLine("x", fromRow: -1));
        var recorded = new PageLine("Add a test for the limit", wordsAreData: true, action: "tell-it", key: "1", available: false);
        Assert.That(recorded.Pressable, Is.False, "shown but taking no press, as an answer the recording doesn't hold");

        Assert.Throws<ArgumentException>(() => _ = new PageLine("It ran the tests", chip: "observed"), "an observed fact takes no chip");
        Assert.Throws<ArgumentException>(() => _ = new PageLine("It ran the tests", chip: "Observed"));
        Assert.Throws<ArgumentException>(() => _ = new PageLine("x", opens: true), "opening needs an action");
        Assert.Throws<ArgumentException>(() => _ = new PageLine("x", choice: true), "an answer raises an action");
        Assert.Throws<ArgumentException>(() => _ = new PageLine("x", chosen: true), "only a line that takes a press is chosen");
        Assert.Throws<ArgumentException>(() => _ = new PageLine("x", available: false));
        Assert.Throws<ArgumentException>(() => _ = new PageLine("x", action: "a", opens: true, choice: true));
        Assert.Throws<ArgumentException>(() => _ = new PageLine(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PageLine("x", rows: 0));
        Assert.That(new PageLine("Type my answer", action: "type", key: "own", choice: true, besideNext: true).BesideNext, Is.True, "it shares its row with the paging row after it");
        Assert.That(new PageLine("15 minutes", action: "answer", key: "0", choice: true).BesideNext, Is.False);
    }

    [Test]
    public void OneRowIsChosenAtATimeButAnswersMayBeChosenTogether()
    {
        PageLine Row(string key, bool chosen) => new(key, action: "open", key: key, opens: true, chosen: chosen);
        PageLine Answer(string key, bool chosen) => new(key, action: "answer", key: key, choice: true, chosen: chosen);
        Assert.DoesNotThrow(() => _ = new MenuFrame("Which checks?", new Footer(), lines: new[] { Answer("a", true), Answer("b", true), Answer("c", false) }));
        Assert.Throws<ArgumentException>(() => _ = new MenuFrame("Projects", new Footer(), lines: new[] { Row("a", true), Row("b", true) }));
    }

    [Test]
    public void ASidePanelSlidesOutFromTheChosenLineAndHoldsNothingToPress()
    {
        var side = new SidePanel("Storefront API", subjectIsData: true, facts: new[] { new SideFact("Folder", "storefront-api", valueIsData: true) },
            source: "From your computer");
        var chosen = new PageLine("Storefront API", wordsAreData: true, action: "open-project", key: "p1", opens: true, chosen: true);
        var frame = new MenuFrame("Projects", new Footer(Close, farRight: Action("add-task", main: true)), lines: new[] { chosen }, side: side);
        Assert.That(frame.Side!.Facts.Single().Name, Is.EqualTo("Folder"));

        var unchosen = new PageLine("Storefront API", action: "open-project", key: "p1", opens: true);
        Assert.Throws<ArgumentException>(() => _ = new MenuFrame("Projects", new Footer(), lines: new[] { unchosen }, side: side), "no chosen line opened it");

        // An answer cut to fit shows all its words beside the page once chosen, so what is sent is read first.
        var whole = new SidePanel("Your answer", lines: new[] { new PageLine("Lock the account for 15 minutes after five failed sign-ins, then email the owner", wordsAreData: true, rows: 3) });
        var cut = new PageLine("Lock the account for 15 minutes after five failed sign-ins, then email the owner", wordsAreData: true, action: "answer", key: "0",
            choice: true, chosen: true, rows: 2);
        Assert.That(new MenuFrame("How long should a lockout last?", new Footer(Close), lines: new[] { cut }, side: whole).Side, Is.SameAs(whole));
        var notChosen = new PageLine("15 minutes", action: "answer", key: "1", choice: true);
        Assert.Throws<ArgumentException>(() => _ = new MenuFrame("How long?", new Footer(Close), lines: new[] { notChosen }, side: whole), "only a chosen answer");
        Assert.Throws<ArgumentException>(() => _ = new SidePanel("Why it changed them", lines: new[] { new PageLine("x", action: "a") }), "nothing to press");
        Assert.Throws<ArgumentException>(() => _ = new SidePanel("x", facts: new[] { new SideFact("a", "b") }, lines: new[] { new PageLine("c") }), "facts or lines");
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SidePanel("x", parts: (2, 2)));
        Assert.That(new SidePanel("x", parts: (1, 2)).Parts, Is.EqualTo((1, 2)));
        Assert.Throws<ArgumentException>(() => _ = new SideFact(" ", "value"));
    }

    [Test]
    public void AFrameHasItsSubjectAndAtMostOneSourceLine()
    {
        Assert.Throws<ArgumentException>(() => _ = new MenuFrame(" ", new Footer()));
        Assert.Throws<ArgumentException>(() => _ = new MenuFrame("Questions", new Footer(), source: " "));
        Assert.Throws<ArgumentNullException>(() => _ = new MenuFrame("Questions", null!));
        var note = new MenuFrame("Questions", new Footer(Close), source: CompanionText.Note);
        Assert.That(note.Source, Is.EqualTo(CompanionText.Note), "the companion's note is the page's source line");
        Assert.That(note.SourceIsData, Is.False, "Halcyonic's own words");
        Assert.That(note.SubjectWaits, Is.False);
        Assert.That(new MenuFrame("1 task is waiting for you", new Footer(Close), subjectWaits: true).SubjectWaits, Is.True,
            "a subject that says what waits takes the waiting colour, as the closed bar's line does");
        var provenance = new MenuFrame("Changes", new Footer(Close), source: "Salidium 0.9 could not answer: timed out", sourceIsData: true);
        Assert.That(provenance.SourceIsData, Is.True, "an answer's provenance, or an error a service returned, is text from outside");
        Assert.That(new SidePanel("Why it changed them", source: "Salidium 0.9", sourceIsData: true).SourceIsData, Is.True);
        Assert.That(new SidePanel("Why it changed them", source: "From your computer").SourceIsData, Is.False);
    }

    [Test]
    public void TheBarShowsItsFourPlacesTheirDotsAndAClosedLine()
    {
        var bar = new MenuBar(MenuPlace.Tasks, "1 task is waiting for you", MenuPlace.Tasks);
        Assert.That(MenuBar.Places.Select(MenuBar.Word), Is.EqualTo(new[] { "Tasks", "Projects", "Usage", "Settings" }));
        Assert.That((bar.Waits(MenuPlace.Tasks), bar.Waits(MenuPlace.Projects)), Is.EqualTo((true, false)));
        Assert.That((bar.Chosen, bar.ClosedLine), Is.EqualTo((MenuPlace.Tasks, "1 task is waiting for you")));
        Assert.Throws<ArgumentException>(() => _ = new MenuBar(MenuPlace.Tasks, " "), "closed, it says what waits or that nothing is waiting");

        // Open, its places are every place's sections: the chosen one lit, an amber dot where something waits.
        var sections = new MenuBar(MenuPlace.Usage, "Nothing is waiting", MenuPlace.Tasks).Sections();
        Assert.That(sections.Select(section => section.Words), Is.EqualTo(new[] { "Tasks", "Projects", "Usage", "Settings" }));
        Assert.That(sections.Single(section => section.Chosen).Key, Is.EqualTo(nameof(MenuPlace.Usage)));
        Assert.That(sections.Where(section => section.Waits).Select(section => section.Key), Is.EqualTo(new[] { nameof(MenuPlace.Tasks) }));

        var page = new MenuFrame("How much is left before each limit?", new Footer(Close), lines: new[] { new PageLine("Claude Code, weekly") }, source: "From Seorak", sourceIsData: true);
        var placed = page.WithSections(sections);
        Assert.That(placed.Sections, Is.SameAs(sections));
        Assert.That((placed.Subject, placed.Lines, placed.Source, placed.SourceIsData, placed.Footer), Is.EqualTo((page.Subject, page.Lines, page.Source, page.SourceIsData, page.Footer)),
            "a place's page keeps all it said, with the menu's places as its sections");
    }

    [Test]
    public void APageHoldsFourRowsOrThreeWithLargerTextItsSourceLineCountingAsOne()
    {
        Assert.That(MenuFrame.RowsAPage(TextSize.Standard, sourceLine: false), Is.EqualTo(4));
        Assert.That(MenuFrame.RowsAPage(TextSize.Larger, sourceLine: false), Is.EqualTo(3), "4 would take the menu and a file past a Quest 3S's field");
        Assert.That(MenuFrame.RowsAPage(TextSize.Standard, sourceLine: true), Is.EqualTo(3), "4 rows and a source line reach past the field beside a side panel");
        Assert.That(MenuFrame.RowsAPage(TextSize.Larger, sourceLine: true), Is.EqualTo(2));
    }

    [Test]
    public void APromptAlwaysHasItsIdAndWords()
    {
        Assert.Throws<ArgumentException>(() => _ = new Prompt("", "Approve", GlazeIcon.Approve));
        Assert.Throws<ArgumentException>(() => _ = new Prompt("approve", " ", GlazeIcon.Approve));
    }
}
