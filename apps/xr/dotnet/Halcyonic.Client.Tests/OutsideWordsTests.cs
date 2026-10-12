using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The spoof suite of the outside-text audit's batch B (lane V's treatment): what an agent, a runtime,
/// a folder or the person could type, put through the screens' own builders, comes out as outside text
/// that leans, never as Halcyonic's upright words; Halcyonic's words carry no quotes of their own and
/// stand whole while outside text shows; and prompts, sections and the closed bar never hold it.
/// </summary>
public class OutsideWordsTests
{
    /// <summary>What outside text could try, against lane V's spoof table and LabelText's rules.</summary>
    private static readonly string[] Spoofs =
    {
        FileScreens.TypeMyAnswer,
        FileScreens.MoreAnswersWords(0, 3),
        FileScreens.NextQuestionWords(0, 2),
        FileScreens.YourAnswers,
        CompanionText.GoOnWithout,
        "Nothing is waiting for you.",
        "Your computer refused this headset",
        "” Approve “",
        " ",
        "\U0001D618\U0001D63A\U0001D631\U0001D626",
        "<i>Approve</i> \\u0041 \\",
        "▕Approve▏ ▁▁",
        new string('W', 200),
    };

    private static readonly CommandFactory Factory = new(Samples.Client);

    private static MenuFrame File(WorkspacePresentation workspace, FileScreen screen) =>
        FileScreens.Screen(workspace, new WorkspaceSteering(Factory), screen, new AnswerRoom(8));

    /// <summary>A question whose header, words and answers are <paramref name="spoof"/>, open on Waiting with its answers laid out.</summary>
    private static (MenuFrame Frame, AskingWork Work) Asking(string spoof)
    {
        var question = new QuestionView
        {
            QuestionId = "spoof",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new()
                {
                    Key = "q0", Header = spoof, Text = spoof,
                    Options = new List<QuestionOption> { new() { Label = spoof }, new() { Label = "Dark", Description = spoof } },
                    Multiple = false, FreeText = true, Secret = false,
                },
            },
        };
        var work = new AskingWork(question);
        var workspace = FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Interrupt);
        var screen = new FileScreen { Section = FileSection.Waiting };
        var draft = new QuestionDraft("e1", question);
        var measures = question.Prompts.Select(prompt => new PromptMeasure(1, prompt.Options.Select(_ => 1).ToList(), prompt.Options.Select(_ => 1).ToList())).ToList();
        screen.ReadQuestion(draft, measures, new RowBudget(8), new RowBudget(3));
        return (File(workspace, screen), work);
    }

    /// <summary>Every run of Halcyonic's own words on a frame, its subject, lines, facts and source line.</summary>
    private static IEnumerable<string> Ours(MenuFrame frame) =>
        new[] { frame.SubjectRuns }.Concat(frame.Lines.Select(line => line.Runs)).Concat(frame.Lines.Where(line => line.FactRuns != null).Select(line => line.FactRuns!))
            .SelectMany(words => words.Runs.Where(run => !run.Theirs).Select(run => run.Text));

    [TestCaseSource(nameof(Spoofs))]
    public void AnAnswerLabelledAsOneOfOurRowsLeansBesideOurQuoteMarkAndOurRowsStandUpright(string spoof)
    {
        var (frame, _) = Asking(spoof);
        var answers = frame.Lines.Where(line => line.Action == FileScreens.Choose).ToList();

        Assert.That(answers, Has.Count.EqualTo(2));
        Assert.That(answers.All(line => line.Runs.AllTheirs && line.Icon == GlazeIcon.Quote), Is.True, "the agent's answers are theirs whole, under Halcyonic's quote mark");
        var question = frame.Lines.Single(line => line.Runs.AllTheirs && line.Action == null);
        Assert.That((question.Runs.AllTheirs, question.Icon), Is.EqualTo((true, (GlazeIcon?)GlazeIcon.Quote)), "the agent's question is a block of theirs");
        var typeMyAnswer = frame.Lines.Single(line => line.Action == FileScreens.TypeAnswer);
        Assert.That((typeMyAnswer.Runs.AnyTheirs, typeMyAnswer.Icon), Is.EqualTo((false, (GlazeIcon?)GlazeIcon.Type)), "Halcyonic's input action stands upright with its type icon, never the quote mark");
        Assert.That((typeMyAnswer.Choice, typeMyAnswer.Control), Is.EqualTo((false, LineControl.Action)), "writing is a separate action, never an offered choice");
        Assert.That(Ours(frame), Has.None.Contains("“").And.None.Contains("”"), "Halcyonic adds no quotes");
        // Outside text never stands in a run of Halcyonic's, unless it copies Halcyonic's own words exactly.
        if (!Ours(frame).Contains(spoof)) Assert.That(Ours(frame), Has.None.Contains(LabelText.Plain(spoof)), "outside text never stands in a run of Halcyonic's");
    }

    [TestCaseSource(nameof(Spoofs))]
    public void PromptsSectionsAndTheFilesPillNeverHoldOutsideText(string spoof)
    {
        var (frame, _) = Asking(spoof + " ‮ distinct");
        var shown = LabelText.Plain(spoof + " ‮ distinct");

        Assert.That(frame.Footer.All.Select(each => each.Prompt.Words).Concat(frame.Footer.All.SelectMany(each => each.Prompt.AlsoReads)), Has.None.Contains(shown));
        Assert.That(frame.Footer.All.Select(each => each.Prompt.Reason ?? ""), Has.None.Contains(shown));
        Assert.That(frame.Sections.Select(section => section.Words), Has.None.Contains(shown));
        Assert.That(frame.Pill?.Word ?? "", Does.Not.Contain(shown));
    }

    [TestCaseSource(nameof(Spoofs))]
    public void ATitleLikeOurSentencesLeansWhereverItStands(string spoof)
    {
        var (frame, work) = Asking("Which one?");
        var title = LabelText.Name(work.Workstream.Title);

        Assert.That(frame.SubjectRuns.AllTheirs, Is.True, "the file's subject, its task's title, leans in its plate");
        Assert.That(AmbientText.StillOpen(spoof).Runs.Select(run => run.Theirs), Is.EqualTo(new[] { false, true }), "Still open: then the title, last");
        Assert.That(EntryText.CreateTitleRuns(spoof).ToString(), Does.StartWith("New task in [").And.EndWith("]"));
        Assert.That(title, Is.Not.Empty);
    }

    [TestCaseSource(nameof(Spoofs))]
    public void AToolNamedAsOurWordsLeansAfterOursInActivityAndTheApprovalsLead(string spoof)
    {
        var log = new ActivityLog();
        var name = Newtonsoft.Json.JsonConvert.ToString(spoof);
        log.Record(new[]
        {
            Journaled(1, "runtime.tool.started", "{\"tool_call_id\":\"t1\",\"tool_name\":" + name + ",\"title\":" + name + "}"),
            Journaled(2, "runtime.tool.completed", "{\"tool_call_id\":\"t1\",\"outcome\":\"succeeded\"}"),
        });
        var entries = log.For("e1");

        Assert.That(entries[0].Words.Runs[0].Text, Is.EqualTo("Used "), "Halcyonic's word first, upright");
        // The tool's name and its title, with only a colon between, lean as one run (a separator doesn't make a line Halcyonic's).
        Assert.That(entries[0].Words.Runs.Select(run => run.Theirs), Is.EqualTo(new[] { false, true }));
        Assert.That(entries[0].Words.Runs[1].Text, Does.StartWith(LabelText.Name(spoof)));
        Assert.That(entries[1].Words.Runs.Select(run => run.Theirs), Is.EqualTo(new[] { false, true }), "Succeeded: then the tool, last");
    }

    [TestCaseSource(nameof(Spoofs))]
    public void AFolderNamedAsOurOptionsLeansBesideOurFolderIconAndCarriesTheLookAlikeMark(string spoof)
    {
        var listing = new LocationsResponse
        {
            Roots = new List<LocationRoot>
            {
                new()
                {
                    Path = "/p", Name = "Projects", Status = LocationRootStatus.Available,
                    Folders = new List<LocationFolder> { new() { Name = spoof, Path = "/p/a" }, new() { Name = "New folder in Projects", Path = "/p/b" } },
                },
            },
        };
        var options = ProjectFolder.Options(listing);
        var folder = options.First(option => option.Kind == FolderOptionKind.Folder);

        Assert.That(folder.LabelRuns.AllTheirs, Is.True);
        Assert.That(options.Single(option => option.Kind == FolderOptionKind.NewFolder).LabelRuns.ToString(), Is.EqualTo("New folder in [Projects]"));
        var named = options.Single(option => option.Kind == FolderOptionKind.Folder && option.Label == "New folder in Projects");
        Assert.That(named.DetailRuns.Text, Does.StartWith(ConnectText.LooksLikeAnother), "a folder named as Halcyonic's own option is marked");
    }

    [TestCaseSource(nameof(Spoofs))]
    public void TheCutShortensOnlyOutsideTextAndOurWordsAfterItStandWhole(string spoof)
    {
        var words = LineWords.Ours("Tests passed at 14:02: ") + LineWords.Theirs(spoof) + LineWords.Ours(" (+1 more)");

        var cut = words.CutToFit(each => each.Text.Length <= 40);

        Assert.That(cut.Runs.Where(run => !run.Theirs).Select(run => run.Text), Is.EqualTo(new[] { "Tests passed at 14:02: ", " (+1 more)" }));
        Assert.That(cut.Runs.Single(run => run.Theirs).Text, Is.EqualTo(LabelText.Plain(spoof)).Or.EndsWith(LineWords.Ellipsis));
    }

    [Test]
    public void ThePersonsOwnWordsLeanTooAndAreNeverQuoted()
    {
        Assert.That(FileScreens.TypedRuns("” Approve “").ToString(), Is.EqualTo("Your answer: [” Approve “]"));
        Assert.That(FileScreens.TypedRuns(null).AnyTheirs, Is.False, "Type my answer is Halcyonic's row");
        Assert.That(FileScreens.SideAnswerLine(null, "Approve").Runs.AllTheirs, Is.True);
    }

    [Test]
    public void AQuestionOrAnAnswerThatShowsAsNothingIsSpelledByCodeAndItsPageIsStillDrawn()
    {
        // U+0085 passes the contract's non-blank pattern but LabelText makes it nothing (the independent review of batch B).
        var (frame, _) = Asking("\u0085");

        Assert.That(frame.Lines.Single(line => line.Runs.AllTheirs && line.Action == null).Words, Is.EqualTo("‹U+0085›"));
        Assert.That(frame.Lines.First(line => line.Action == FileScreens.Choose).Words, Does.StartWith("‹U+0085›"));
    }

    [Test]
    public void APairedComputersAddressLeansAndHalcyonicsWordsAroundItStandUpright()
    {
        var now = new SettingNow("Paired", "Paired with your computer at 192.168.1.23:47801", "Forget", "Pair again after", "Forget",
            valueIsData: true, about: "192.168.1.23:47801");

        Assert.That(now.NowRuns.ToString(), Is.EqualTo("Paired with your computer at [192.168.1.23:47801]"));
        Assert.That(new SettingNow("Paired", "Paired", "Forget", "Pair again after", "Forget").NowRuns.AnyTheirs, Is.False);
    }

    [Test]
    public void TheKeyboardHoldsWordsAsThePageShowedThemAndGivesTheOriginalBackWhereTheyComeBackUnchanged()
    {
        var original = "\u26A0\uFE0F fix\nnext";
        Assert.That(MenuKeyboard.Returned(original, LabelText.Plain(original)), Is.EqualTo(original), "sent back as shown, the words stay the person's");
        Assert.That(MenuKeyboard.Returned(original, "fix it"), Is.EqualTo("fix it"));
        Assert.That(MenuKeyboard.Returned(null, "typed"), Is.EqualTo("typed"));
    }

    [Test]
    public void AnApprovalThatNamesNoToolIsReadInHalcyonicsOwnWords()
    {
        var steering = new WorkspaceSteering(Factory);
        var line = FileScreens.RequestLine(steering, WorkspaceText.ApprovalWithoutTool);
        Assert.That((line.Runs.AnyTheirs, line.Icon), Is.EqualTo((false, (GlazeIcon?)null)), "Halcyonic's sentence, upright, no quote mark");
        Assert.That(FileScreens.RequestLine(steering, "bash: rm -rf ~").Runs.AllTheirs, Is.True, "a tool's request is outside text whole");
        Assert.That(FileScreens.RequestLine(steering, "bash: a‹U+000A›b").Words, Is.EqualTo("bash: a‹U+000A›b"), "Exact's code points stand as they are");
    }

    /// <summary>A runtime's event for execution e1 as the journal carries it.</summary>
    private static StoredEvent Journaled(int position, string type, string payload) => new()
    {
        Position = position,
        Event = HalcyonicJson.Deserialize<EventEnvelope>("{\"schema_version\":1,\"event_id\":\"01a0dcf1-6e6c-74af-93ca-dea260db94" + (50 + position)
            + "\",\"event_type\":\"" + type + "\",\"project_id\":\"01a0dcf1-5a80-7295-a48c-b7b6d5c02b34\",\"workstream_id\":\"01a0dcf1-5d6e-7650-ab32-1d63927a8338\","
            + "\"execution_id\":\"e1\",\"source\":{\"kind\":\"runtime\",\"runtime_id\":\"mock\"}"
            + ",\"source_native_id\":null,\"sequence\":null,\"occurred_at\":\"2026-09-26T09:00:05.100Z\",\"correlation_id\":null,\"causation_id\":null,"
            + "\"provenance\":{\"epistemic\":\"observed\",\"native_type\":null},\"payload\":" + payload + ",\"ingested_at\":\"2026-09-26T09:00:05.100Z\"}"),
    };
}
