using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A judge's walk through the directed task's file (ADR 0026) on the recorded demonstration, played
/// as the headset plays it: Waiting under its pill with the agent's question, its pages and Send
/// answer; the approval, its request in parts and Yes only after the last; Checks; and Tell it with
/// the recorded instructions. At each stop the file offers exactly what the recording answers, and
/// the rules the file's reviews settled hold on the recording itself: no Yes before every part of the
/// request is drawn, Yes sends once, Deny's Yes from the first part, Cancel sends nothing, nothing out
/// of view is sent, and a question asking for a secret can't be answered here. The menu's bar and
/// Tasks come with lane U's screens; the walk starts at the file a task opens.
/// </summary>
public class JudgeFileWalkTests
{
    private readonly CommandFactory factory = new(Samples.Client);
    private RealtimeSession? session;
    private DateTimeOffset clock = DateTimeOffset.Parse("2026-10-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [TearDown]
    public async Task StopSession()
    {
        if (session != null) await session.StopAsync();
    }

    /// <summary>A second passes: long enough for any part or page drawn before it to take a press.</summary>
    private DateTimeOffset Later() => clock += TimeSpan.FromSeconds(1);

    private DemonstrationPlayer Play(DemonstrationRecording? recording = null)
    {
        var player = new DemonstrationPlayer(recording ?? Demonstration.Recording(), Samples.Client, Demonstration.Fast());
        session = player.Session;
        session.Start();
        return player;
    }

    private WorkspacePresentation Directed(ActivityLog activity, CommandSubmissions submissions) =>
        WorkspacePresenter.Present(Demonstration.DirectedWorkstream(session!), session!.State, activity, session.Status.IsLive, submissions);

    /// <summary>What the recording answers where it holds at the end of <paramref name="node"/>.</summary>
    private static ISet<DemonstrationAnswerKind> Recorded(DemonstrationNode node) =>
        node.BranchesAfter(node.Events.Count).Select(branch => branch.Answer.Kind).ToHashSet();

    /// <summary>
    /// The agent's question as a layout of <paramref name="room"/> measures it: the question across
    /// <paramref name="questionRows"/> rows and each answer, its description after it, across two.
    /// </summary>
    private static IReadOnlyList<PromptMeasure> Measured(QuestionView question, int questionRows) =>
        question.Prompts.Select(prompt => new PromptMeasure(questionRows, prompt.Options.Select(_ => 2).ToList(), prompt.Options.Select(_ => 2).ToList())).ToList();

    /// <param name="rows">A page's rows, its source line's included: four, or three with larger text.</param>
    /// <param name="questionRows">The rows the question wraps to at that size.</param>
    [TestCase(4, 2)]
    [TestCase(3, 3)]
    public async Task AJudgeAnswersTheQuestionApprovesTheWholeRequestReadsChecksAndTellsIt(int rows, int questionRows)
    {
        var room = new AnswerRoom(rows);
        var player = Play();
        var activity = new ActivityLog();
        var submissions = new CommandSubmissions { Demonstration = () => true };
        var steering = new WorkspaceSteering(factory);
        var screen = new FileScreen { Speak = false, Zone = TimeZoneInfo.Utc };
        MenuFrame Frame(WorkspacePresentation workspace) => FileScreens.Screen(workspace, steering, screen, room);

        // The question: the file opens on Waiting, its pill and dot saying something waits.
        activity.Record((await Pumping.Until(session!, Demonstration.AsksItsQuestion, "the directed work asks its question")).Events);
        var workspace = Directed(activity, submissions);
        screen.Section = FileScreens.Opening(workspace);
        Assert.That(screen.Section, Is.EqualTo(FileSection.Waiting));
        var question = workspace.QuestionToAnswer!;
        var draft = new QuestionDraft(workspace.Execution!.ExecutionId, question);
        // The page's rows less the one its source line takes, as the director gives them.
        screen.ReadQuestion(draft, Measured(question, questionRows), new RowBudget(room.Rows - 1), new RowBudget(room.Rows - 1));
        var frame = Frame(workspace);
        Assert.That(StateLanguage.WordOf(frame.Pill!.State), Is.EqualTo("Waiting for you"));
        Assert.That(frame.Sections.Single(section => section.Waits).Words, Is.EqualTo("Waiting"));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.False, "nothing to send before an answer is chosen");

        // A question longer than its head reads first on pages of its own, a part at a time.
        var parts = 0;
        while (screen.Question.QuestionPart != null)
        {
            Assert.That(JudgePath.Options(frame), Is.Empty, "no answer to choose before the whole question has shown");
            parts++;
            screen.Question.Drawn(clock);
            screen.Question.NextPart(Later());
            frame = Frame(workspace);
        }
        Assert.That(parts, Is.EqualTo(screen.Question.QuestionParts(0)));
        screen.Question.Drawn(clock);

        // Its answers, in the agent's order, a page at a time: what is chosen is only ever on the page in view.
        var labels = question.Prompts[0].Options.Select(option => option.Label).ToList();
        var seen = new List<string>();
        for (var page = 0; page < screen.Question.Pages; page++)
        {
            seen.AddRange(JudgePath.Options(Frame(workspace)).Select(key => labels[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)]));
            if (screen.Question.Pages > 1)
            {
                screen.Question.Drawn(clock);
                screen.Question.MoreAnswers(Later());
            }
        }
        Assert.That(seen, Is.EqualTo(labels), "every option the recording answers, none left out");
        var recordedOptions = Recorded(Demonstration.Recording().Nodes[0]);
        Assert.That(recordedOptions, Has.Member(DemonstrationAnswerKind.Answer));
        if (screen.Question.Pages > 1)
        {
            screen.Question.Choose(0);
            screen.Question.Drawn(clock);
            screen.Question.MoreAnswers(Later());
            Assert.That(draft.IsChosen(0, Demonstration.FirstOption), Is.False, "turning the page clears what was chosen on it");
            Assert.That(FileScreens.WhySendWaits(screen), Is.Not.Null);
            Assert.That(steering.SendAnswer(draft, workspace, FileScreens.WhySendWaits(screen)).Step, Is.EqualTo(SteeringStep.Explain),
                "nothing out of view is sent");
            while (screen.Question.Page != 0)
            {
                screen.Question.Drawn(clock);
                screen.Question.MoreAnswers(Later());
            }
        }
        screen.Question.Choose(0);
        screen.Question.Drawn(clock);
        frame = Frame(workspace);
        Assert.That(frame.Lines.Single(line => line.Chosen).Words, Does.StartWith(Demonstration.FirstOption));
        Assert.That(FileScreens.WhySendWaits(screen), Is.Null);
        // Stop stands on Activity (ADR 0026); Waiting offers the answer. Together the file offers what
        // the recording answers at its question.
        screen.Section = FileSection.Activity;
        var stopping = Frame(workspace);
        screen.Section = FileSection.Waiting;
        frame = Frame(workspace);
        Assert.That(JudgePath.Offered(frame).Union(JudgePath.Offered(stopping)), Is.EquivalentTo(recordedOptions),
            "the file offers what the recording answers at its question");

        var sending = steering.SendAnswer(draft, workspace, FileScreens.WhySendWaits(screen));
        if (sending.Step == SteeringStep.Confirm)
        {
            Assert.That(Frame(workspace).Footer[PromptSlot.Free]!.Kind, Is.EqualTo(PromptKind.Yes));
            sending = steering.Confirm(workspace);
        }
        Assert.That(sending.Step, Is.EqualTo(SteeringStep.Send));
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.None), "an answer goes once");
        var answered = (ExecutionAnswerQuestionCommand)sending.Command!;
        Assert.That(answered.Payload.Answers.Single().Selected, Is.EqualTo(new[] { Demonstration.FirstOption }));
        await submissions.SubmitAsync(c => session!.SubmitAsync(c), sending.Command!, workspace.Execution.ExecutionId);
        Assert.That(submissions.FeedbackFor(workspace.Execution.ExecutionId, session!.State, 5).First().Text, Does.Contain(Demonstration.FirstOption));

        // The approval: Waiting again, Approve the main action and Deny beside it; Stop stands on Activity.
        activity.Record((await Pumping.Until(session, Demonstration.AsksForApproval, "the directed work asks for approval")).Events);
        workspace = Directed(activity, submissions);
        screen.Section = FileSection.Activity;
        var stoppingAtApproval = Frame(workspace);
        screen.Section = FileScreens.Opening(workspace);
        frame = Frame(workspace);
        Assert.That(screen.Section, Is.EqualTo(FileSection.Waiting));
        Assert.That(frame.Lines[0].Tone, Is.EqualTo(LineTone.Waiting));
        Assert.That(JudgePath.Offered(frame).Union(JudgePath.Offered(stoppingAtApproval)), Is.EquivalentTo(Recorded(Demonstration.Answered())),
            "the file offers what the recording answers at its approval");

        // Deny's Yes stands from the first part; Cancel sends nothing.
        Assert.That(steering.Press(WorkspaceAction.Deny, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        var request = steering.Request(workspace)!;
        screen.ReadRequest(request, 7, 3, steering);
        Assert.That(Frame(workspace).Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, deny"), "refusing needs no reading to the end");
        steering.Cancel();
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.None), "cancelled, Yes sends nothing");
        Assert.That(Frame(workspace).Footer.Confirming, Is.False);

        // Approve shows the whole request again, a part at a time, and Yes only once the last has been drawn.
        Assert.That(steering.Press(WorkspaceAction.Approve, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        Assert.That(Frame(workspace).Footer[PromptSlot.Free], Is.Null, "no Yes before the layout has measured the request");
        screen.ReadRequest(request, 7, 3, steering);
        for (var part = 0; part < screen.RequestParts; part++)
        {
            frame = Frame(workspace);
            Assert.That(frame.Lines[0].Words, Is.EqualTo(request));
            Assert.That(frame.Footer[PromptSlot.Free], Is.Null, "no Yes on part " + (part + 1) + " before it is drawn");
            Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.Explain), "nothing goes before every part has shown");
            Assert.That(steering.Armed, Is.EqualTo(WorkspaceAction.Approve), "and it stays armed, so the rest can be read");
            screen.RequestDrawn(screen.RequestPart, steering, clock);
            if (part + 1 < screen.RequestParts) screen.NextRequestPart(steering, Later());
        }
        frame = Frame(workspace);
        Assert.That(frame.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, approve"));
        var approving = steering.Confirm(workspace);
        Assert.That(approving.Step, Is.EqualTo(SteeringStep.Send));
        Assert.That(approving.Command, Is.InstanceOf<ExecutionRespondToApprovalCommand>());
        Assert.That(steering.Confirm(workspace).Step, Is.EqualTo(SteeringStep.None), "Yes sends once");
        await submissions.SubmitAsync(c => session.SubmitAsync(c), approving.Command!, workspace.Execution!.ExecutionId);

        // The approved turn ends with a failing test: Checks reads it, from the recording's simulated sources.
        activity.Record((await Pumping.Until(session, s => Demonstration.DirectedExecution(s)?.Status == ExecutionStatus.Completed,
            "the approved turn ends")).Events);
        workspace = Directed(activity, submissions);
        var executionId = workspace.Execution!.ExecutionId;
        var approvedNode = Demonstration.AtApproval(DemonstrationAnswerKind.Approve).Node;
        var recording = Demonstration.Recording();
        var played = recording.Nodes[approvedNode].Events.Count;
        var understanding = recording.UnderstandingAt(executionId, approvedNode, played);
        var evaluation = recording.EvaluationAt(executionId, approvedNode, played);
        Assert.That(understanding != null || evaluation != null, Is.True, "the recording read the checks where the turn ended");
        SectionPresentation Checked(AnswerDepth depth) => CheckedPresenter.Present(executionId,
            understanding == null ? null : new IntelligenceRead<UnderstandingResponse>(understanding.Response, understanding.ReadAt, recorded: true), false, null,
            evaluation == null ? null : new IntelligenceRead<EvaluationResponse>(evaluation.Response, evaluation.ReadAt, recorded: true), false, null,
            clock, TimeZoneInfo.Utc, depth: depth);
        screen.Checked = new FileAnswer(Checked(AnswerDepth.Brief), Checked(AnswerDepth.Full));
        screen.Section = FileSection.Checks;
        frame = Frame(workspace);
        Assert.That(frame.Lines[0].Words, Does.StartWith("Tests failed").And.Contains("1 failed, 23 passed"));
        Assert.That(frame.Source, Does.StartWith("Simulated checks · recorded at "), "the checks say they are simulated and recorded");
        Assert.That(frame.Footer[PromptSlot.Rare]!.Id, Is.EqualTo(FileScreens.Refresh));

        // Tell it: the recorded instructions as rows, chosen first, sent by Tell it in exactly the words shown.
        var presets = player.InstructionsFor(executionId);
        Assert.That(presets, Is.Not.Empty);
        screen.Section = FileSection.Activity;
        screen.Presets = presets;
        frame = Frame(workspace);
        Assert.That(JudgePath.Instructions(frame), Is.EqualTo(presets.Select(preset => WorkspaceText.OneLine(preset.Text))));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Available, Is.False, "Tell it waits until one is chosen");
        Assert.That(JudgePath.Offered(frame), Is.EquivalentTo(Recorded(recording.Nodes[approvedNode])),
            "the file offers what the recording answers once the approved turn has ended");
        screen.ChoosePreset(0);
        Assert.That(Frame(workspace).Footer[PromptSlot.FarRight]!.Available, Is.True);
        Assert.That(steering.Press(WorkspaceAction.Instruct, workspace).Step, Is.EqualTo(SteeringStep.Type));
        var telling = steering.Typed(screen.PresetToSend, workspace);
        Assert.That(telling.Step, Is.EqualTo(SteeringStep.Send));
        await submissions.SubmitAsync(c => session.SubmitAsync(c), telling.Command!, executionId);
        Assert.That(submissions.FeedbackFor(executionId, session.State, 5).First().Text, Does.Contain(presets[0].Label));
        await Pumping.Until(session, s => s.State.Runtimes.Count == 0, "the recording plays its instruction and ends");
        Assert.That(Demonstration.DirectedExecution(session)!.LastTestRun!.Outcome, Is.EqualTo(TestOutcome.Passed));
        Assert.That(session.State.Commands.Values.Where(command => command.ExecutionId == executionId).Select(command => command.Status),
            Is.All.EqualTo(CommandStatus.Completed), "nothing a judge pressed reached an agent; only the recording's own commands completed");
    }

    [Test]
    public async Task ARecordedQuestionAskingForASecretCannotBeAnsweredFromTheFile()
    {
        var text = Demonstration.Text();
        Assert.That(text, Does.Contain("\"secret\":false"));
        Play(DemonstrationRecording.Parse(text.Replace("\"secret\":false", "\"secret\":true")));
        var activity = new ActivityLog();
        var submissions = new CommandSubmissions { Demonstration = () => true };
        await Pumping.Until(session!, Demonstration.AsksItsQuestion, "the directed work asks its question");
        var workspace = Directed(activity, submissions);
        Assert.That(workspace.Actions, Has.None.EqualTo(WorkspaceAction.Answer).And.None.EqualTo(WorkspaceAction.Instruct));
        var screen = new FileScreen { Section = FileScreens.Opening(workspace) };
        var draft = new QuestionDraft(workspace.Execution!.ExecutionId, workspace.QuestionToAnswer!);
        screen.ReadQuestion(draft, Measured(workspace.QuestionToAnswer!, 2), new RowBudget(3), new RowBudget(3));
        var steering = new WorkspaceSteering(factory);
        var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
        Assert.That(JudgePath.Options(frame), Is.Empty, "no answer to choose");
        Assert.That(frame.Lines.Select(line => line.Words), Has.Some.EqualTo(WorkspaceText.CannotAnswer(workspace.QuestionToAnswer!)));
        Assert.That(frame.Footer[PromptSlot.FarRight], Is.Null, "no Send answer");
        Assert.That(frame.Footer[PromptSlot.Rare]!.Id, Is.EqualTo(FileScreens.Stop), "Stop is the way on");
        screen.Section = FileSection.Activity;
        Assert.That(FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4)).Footer[PromptSlot.FarRight], Is.Null, "nor Tell it, which would invite typing it");
        Assert.That(steering.SendAnswer(draft, workspace).Step, Is.EqualTo(SteeringStep.Explain));
    }
}
