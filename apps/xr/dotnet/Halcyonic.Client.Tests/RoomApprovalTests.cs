using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The redesign's approval through a real file column, as AUDIT probed it (2026-11-10): crossed answers, an approval resolved, replaced or changed while its confirmation stands, a second approval, and a lost connection.</summary>
public class RoomApprovalTests
{
    private readonly CommandFactory factory = new(Samples.Client);
    private const string A = "rm -rf build && npm run migrate -- --target staging --verbose --confirm every table";
    private const string B = "git push --force origin main";

    private FileColumn Column(FileMenuHost host, Func<WorkspacePresentation?> present) => new(host, present, factory, () => null, _ => null, () => "");

    private static void Draw(FileMenuHost host, FileColumn column)
    {
        column.Drawn(column.Frame!, sidePanel: null);
        host.Wait(1);
    }

    private static RoomBar Bar(FileColumn column) => RoomBar.Of(KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
        new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true,
        requestRows: column.Steering.ToRead(column.Now!) is string read && column.Screen.Measured(column.Steering, read) ? column.Screen.RequestRows : (int?)null)!, VoiceStage.Idle, false);

    private static string Show(RoomBar bar) => bar.State + " | body: " + bar.Body.Text + " | says: " + bar.Sentence + " | " +
        string.Join(", ", bar.Column.Select(action => action.Words + (action.Main ? "*" : "") + (action.Locks ? "(lock)" : "")));

    private static RoomAction? Find(RoomBar bar, string words) => bar.Column.FirstOrDefault(action => action.Words.StartsWith(words, StringComparison.Ordinal));

    private (FileMenuHost Host, WaitingWork Work, FileColumn Column) Asked(int partRows = 8)
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.PendingApprovals.Add(WaitingWork.Approval(WaitingWork.ApprovalId, A, "2026-10-02T12:00:00Z")); execution.Status = ExecutionStatus.WaitingForHuman; },
            WorkstreamStatus.WaitingForHuman);
        var column = Column(host, () => work.Present());
        column.RequestPartRowsOf = () => partRows;
        column.Reflow();
        Draw(host, column);
        return (host, work, column);
    }

    private static void Press(FileColumn column, RoomAction action) => column.Act(action.Control.Action, action.Control.Key);

    /// <summary>
    /// Draws the request a part at a time until its Confirm stands, the decision is sent (R1: an approval is one press), or
    /// nothing is left to answer.
    /// </summary>
    private void ReadWhole(FileMenuHost host, FileColumn column)
    {
        var sent = host.Sent.Count;
        for (var turn = 0; turn < 12 && host.Sent.Count == sent && Answering(column) is RoomBar bar && Find(bar, "Confirm") == null; turn++)
        {
            Draw(host, column);
            if (host.Sent.Count != sent || Answering(column) is not RoomBar drawn || Find(drawn, "Confirm") != null) break;
            if (Find(drawn, "Next Part") is RoomAction next) Press(column, next);
        }
    }

    /// <summary>The redesign's bar for the file, or null where nothing is left that it draws.</summary>
    private static RoomBar? Answering(FileColumn column) => column.Frame != null && KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "",
        new ConnectionStatus(ConnectionPhase.Live), "Storefront", new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true,
        requestRows: column.Steering.ToRead(column.Now!) is string read && column.Screen.Measured(column.Steering, read) ? column.Screen.RequestRows : (int?)null) is KitFileBinding binding
        ? RoomBar.Of(binding, VoiceStage.Idle, false) : null;

    [Test]
    public void ApproveThenCancelThenDenySendsADenialAndNeverAnApproval()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (host, work, column) = Asked();
            TestContext.Out.WriteLine("asked: " + Show(Bar(column)));
            Press(column, Find(Bar(column), "Approve")!);
            TestContext.Out.WriteLine("approve pressed: " + Show(Bar(column)));
            // A Confirm that no control offers yet: refused, nothing sent.
            column.Act(FileScreens.Yes, null);
            Assert.That(host.Sent.Count, Is.EqualTo(0), "Yes before the request was drawn");
            // R1: Approve is the decision once its request is drawn whole; Cancel before that draw lets it lapse.
            Press(column, Find(Bar(column), "Cancel")!);
            ReadWhole(host, column);
            Draw(host, column);
            TestContext.Out.WriteLine("cancelled: " + Show(Bar(column)));
            Assert.That(host.Sent.Count, Is.EqualTo(0), "an Approve cancelled before its request was drawn sends nothing");
            column.Act(FileScreens.Yes, null);
            Assert.That(host.Sent.Count, Is.EqualTo(0), "a Yes after Cancel");
            Press(column, Find(Bar(column), "Deny")!);
            Assert.That(host.Sent.Count, Is.EqualTo(1), "Deny is one press");
            var sent = (ExecutionRespondToApprovalCommand)host.Sent[0];
            Assert.That((sent.Payload.Decision, sent.Payload.ApprovalId), Is.EqualTo((ApprovalDecision.Deny, WaitingWork.ApprovalId)));
            // Confirm again, and the drawn request again: nothing more.
            column.Act(FileScreens.Yes, null);
            Draw(host, column);
            Assert.That(host.Sent.Count, Is.EqualTo(1), "one decision only");
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>R1 (the lead's decision, 2026-11-12): Approve is the decision, sent once the request it answers is drawn whole, with no second press.</summary>
    [Test]
    public void ApproveIsOnePressOnceItsRequestIsDrawnWhole()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (host, work, column) = Asked();
            // The person sees the request, then presses Approve on it.
            Draw(host, column);
            Press(column, Find(Bar(column), "Approve")!);
            Assert.That(host.Sent.Count, Is.EqualTo(0), "nothing goes before its request is drawn");
            ReadWhole(host, column);
            Assert.That(host.Sent.Count, Is.EqualTo(1), "drawn whole, the press sent it");
            var sent = (ExecutionRespondToApprovalCommand)host.Sent[0];
            Assert.That((sent.Payload.Decision, sent.Payload.ApprovalId), Is.EqualTo((ApprovalDecision.Approve, WaitingWork.ApprovalId)));
            Draw(host, column);
            column.Act(FileScreens.Yes, null);
            Assert.That(host.Sent.Count, Is.EqualTo(1), "one decision only");
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>
    /// AUDIT (2026-11-12): with Approve in one press, an answer's button is what it answers. Approval A answered, approval B drawn in its
    /// place: B's Approve is a new control (its key B's id, so the kit settles it again), and a press still keyed to A sends nothing and arms
    /// nothing; B's own press approves B.
    /// </summary>
    [Test]
    public void ANewApprovalInTheSamePlaceIsANewButtonAndAPressMeantForTheLastAnswersNothing()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (host, work, column) = Asked();
            Draw(host, column);
            var approveA = Find(Bar(column), "Approve")!;
            Assert.That(approveA.Control.Key, Is.EqualTo(WaitingWork.ApprovalId), "the button names the approval it answers");
            Press(column, approveA);
            ReadWhole(host, column);
            Assume.That(host.Sent.Count, Is.EqualTo(1));
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", B, "2026-10-02T12:05:00Z")); });
            column.Reflow();
            Draw(host, column);
            var approveB = Find(Bar(column), "Approve")!;
            Assert.That(approveB.Control.Key, Is.EqualTo("approval-2"), "a new approval in the same place is a new control");
            Assert.That(approveB.Control.Key, Is.Not.EqualTo(approveA.Control.Key));
            // The quick second pinch, meant for A, lands on B's place.
            Press(column, approveA);
            ReadWhole(host, column);
            Assert.That(host.Sent.Count, Is.EqualTo(1), "a press meant for the last approval answers nothing");
            Assert.That(column.Steering.Armed, Is.Null, "and arms nothing");
            Press(column, approveB);
            ReadWhole(host, column);
            Assert.That(host.Sent.Count, Is.EqualTo(2));
            var sent = (ExecutionRespondToApprovalCommand)host.Sent[1];
            Assert.That((sent.Payload.Decision, sent.Payload.ApprovalId), Is.EqualTo((ApprovalDecision.Approve, "approval-2")));
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>Only a person's press decides: a check or preset that turns DecideOnPress off arms, and nothing it arms is sent unconfirmed.</summary>
    [Test]
    public void APresetsApproveOnlyArmsAndSendsNothing()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (host, work, column) = Asked();
            column.DecideOnPress = false;
            Draw(host, column);
            Press(column, Find(Bar(column), "Approve")!);
            ReadWhole(host, column);
            Assert.That(host.Sent.Count, Is.EqualTo(0));
            Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Approve), "armed, its Confirm standing for a person");
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void AnApprovalArmedAndUnreadIsNeverSentByAStaleYes()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            // R1 (2026-11-12): a denial goes at its press, so no denial's Confirm is ever left standing; the stale press that remains
            // is a Yes with Approve armed and its request not yet drawn whole.
            var (host, work, column) = Asked();
            Press(column, Find(Bar(column), "Approve")!);
            column.Act(FileScreens.Yes, null);
            TestContext.Out.WriteLine("approve armed and unread, Yes pressed: sent " + host.Sent.Count + " | " + Show(Bar(column)));
            Assert.That(host.Sent.Count, Is.EqualTo(0), "an unread approval is never sent by a denial's Confirm");
            Assert.That(Find(Bar(column), "Confirm"), Is.Null);
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void AnApprovalResolvedReplacedOrChangedWhileConfirmingIsNeverAnswered()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            // R1 (the lead's decision, 2026-11-12): Approve is the decision, sent once its request is drawn whole, so each change
            // lands between the press and that draw, where the answer would go.
            // Resolved meanwhile.
            var (host, work, column) = Asked();
            Press(column, Find(Bar(column), "Approve")!);
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
            ReadWhole(host, column);
            TestContext.Out.WriteLine("resolved meanwhile: sent " + host.Sent.Count + " armed " + column.Steering.Armed);
            Assert.That(host.Sent.Count, Is.EqualTo(0));

            // Replaced by another approval meanwhile.
            (host, work, column) = Asked();
            Press(column, Find(Bar(column), "Approve")!);
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", B, "2026-10-02T12:05:00Z")); });
            ReadWhole(host, column);
            column.Reflow();
            TestContext.Out.WriteLine("replaced meanwhile: sent " + host.Sent.Count + " | " + Show(Bar(column)));
            Assert.That(host.Sent.Count, Is.EqualTo(0));
            Assert.That(Bar(column).Body.Text, Does.Contain("git push"));
            Assert.That(Find(Bar(column), "Confirm"), Is.Null, "the new request starts unanswered");

            // The same approval's request reads differently meanwhile.
            (host, work, column) = Asked();
            Press(column, Find(Bar(column), "Approve")!);
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.PendingApprovals.Add(WaitingWork.Approval(WaitingWork.ApprovalId, B, "2026-10-02T12:00:00Z")); });
            ReadWhole(host, column);
            TestContext.Out.WriteLine("request changed meanwhile: sent " + host.Sent.Count + " armed " + column.Steering.Armed);
            Assert.That(host.Sent.Count, Is.EqualTo(0));
        }
        finally { RoomDesign.Enabled = was; }
    }

    /// <summary>A request longer than one part still asks its Confirm after its last part (R1): changes while that Confirm stands are never answered.</summary>
    [Test]
    public void ALongRequestsConfirmNeverAnswersAnApprovalResolvedReplacedOrChangedWhileItStands()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            // Resolved meanwhile.
            var (host, work, column) = Asked(partRows: 1);
            Press(column, Find(Bar(column), "Approve")!);
            ReadWhole(host, column);
            var confirm = Find(Bar(column), "Confirm")!;
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
            Press(column, confirm);
            TestContext.Out.WriteLine("resolved meanwhile: sent " + host.Sent.Count + " armed " + column.Steering.Armed);
            Assert.That(host.Sent.Count, Is.EqualTo(0));

            // Replaced by another approval meanwhile.
            (host, work, column) = Asked(partRows: 1);
            Press(column, Find(Bar(column), "Approve")!);
            ReadWhole(host, column);
            confirm = Find(Bar(column), "Confirm")!;
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", B, "2026-10-02T12:05:00Z")); });
            Press(column, confirm);
            column.Reflow();
            TestContext.Out.WriteLine("replaced meanwhile: sent " + host.Sent.Count + " | " + Show(Bar(column)));
            Assert.That(host.Sent.Count, Is.EqualTo(0));
            Assert.That(Bar(column).Body.Text, Does.Contain("git push"));
            Assert.That(Find(Bar(column), "Confirm"), Is.Null, "the new request starts unanswered");

            // The same approval's request reads differently meanwhile.
            (host, work, column) = Asked(partRows: 1);
            Press(column, Find(Bar(column), "Approve")!);
            ReadWhole(host, column);
            confirm = Find(Bar(column), "Confirm")!;
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.PendingApprovals.Add(WaitingWork.Approval(WaitingWork.ApprovalId, B, "2026-10-02T12:00:00Z")); });
            Press(column, confirm);
            TestContext.Out.WriteLine("request changed meanwhile: sent " + host.Sent.Count + " armed " + column.Steering.Armed);
            Assert.That(host.Sent.Count, Is.EqualTo(0));
        }
        finally { RoomDesign.Enabled = was; }
    }

    [TestCase("2026-10-02T12:05:00Z", TestName = "ASecondApprovalArrivingLater")]
    [TestCase("2026-10-02T11:00:00Z", TestName = "ASecondApprovalSortingEarlier")]
    public void ASecondApprovalWhileTheFirstIsConfirmedNeverTakesItsAnswer(string secondAt)
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (host, work, column) = Asked();
            // R1: the second arrives between Approve and the draw that sends the decision.
            Press(column, Find(Bar(column), "Approve")!);
            work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", B, secondAt)));
            column.Reflow();
            var bar = column.Frame != null && KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
                new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true) is KitFileBinding binding ? RoomBar.Of(binding, VoiceStage.Idle, false) : null;
            TestContext.Out.WriteLine("second at " + secondAt + ": armed " + column.Steering.Armed + " id " + column.Steering.ArmedApprovalId + " | " + (bar == null ? "no binding: the file's own view draws" : Show(bar)));
            ReadWhole(host, column);
            column.Act(FileScreens.Yes, null);
            TestContext.Out.WriteLine("  drawn and Yes pressed: sent " + string.Join(", ", host.Sent.OfType<ExecutionRespondToApprovalCommand>().Select(c => c.Payload.Decision + " " + c.Payload.ApprovalId)));
            Assert.That(host.Sent.OfType<ExecutionRespondToApprovalCommand>().Where(sent => sent.Payload.ApprovalId != WaitingWork.ApprovalId), Is.Empty,
                "the approval that arrived after the press is never answered by it");
            foreach (var sent in host.Sent.OfType<ExecutionRespondToApprovalCommand>())
            {
                Assert.That(sent.Payload.ApprovalId, Is.EqualTo(WaitingWork.ApprovalId), "only the approval that was read is answered");
                Assert.That(bar, Is.Not.Null);
                Assert.That(bar!.Body.Text, Does.Contain("npm run migrate"), "and the panel showed that request, not the other");
                Assert.That(bar.Body.Text, Does.Not.Contain("git push"));
            }
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void WhileDisconnectedNothingIsSentAndWithTheSwitchOffTheFileStaysOnItsSection()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (host, work, column) = Asked();
            var approve = Find(Bar(column), "Approve")!;
            host.Live = false;
            Press(column, approve);
            ReadWhole(host, column);
            column.Act(FileScreens.Yes, null);
            TestContext.Out.WriteLine("disconnected: sent " + host.Sent.Count + " armed " + column.Steering.Armed);
            Assert.That(host.Sent.Count, Is.EqualTo(0));
        }
        finally { RoomDesign.Enabled = was; }
        // Off: an approval arriving does not move the file off Activity (the judge path is unchanged).
        var off = new FileMenuHost();
        var offWork = new WaitingWork();
        offWork.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
        var offColumn = Column(off, () => offWork.Present());
        offWork.Change(execution => { execution.PendingApprovals.Add(WaitingWork.Approval(WaitingWork.ApprovalId, A, "2026-10-02T12:00:00Z")); execution.Status = ExecutionStatus.WaitingForHuman; }, WorkstreamStatus.WaitingForHuman);
        offColumn.Reflow();
        Assert.That(offColumn.Screen.Section, Is.EqualTo(FileSection.Activity));
    }
}
