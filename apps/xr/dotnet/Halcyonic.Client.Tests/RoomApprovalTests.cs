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

    private void ReadWhole(FileMenuHost host, FileColumn column)
    {
        for (var turn = 0; turn < 12 && Find(Bar(column), "Confirm") == null; turn++)
        {
            Draw(host, column);
            if (Find(Bar(column), "Confirm") != null) break;
            if (Find(Bar(column), "Next Part") is RoomAction next) Press(column, next);
        }
    }

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
            ReadWhole(host, column);
            var staleConfirm = Find(Bar(column), "Confirm")!;
            TestContext.Out.WriteLine("approve read: " + Show(Bar(column)));
            Press(column, Find(Bar(column), "Cancel")!);
            Draw(host, column);
            TestContext.Out.WriteLine("cancelled: " + Show(Bar(column)));
            // The stale Confirm of the approval, pressed after Cancel: nothing armed, nothing sent.
            Press(column, staleConfirm);
            Assert.That(host.Sent.Count, Is.EqualTo(0), "a stale Confirm after Cancel");
            Press(column, Find(Bar(column), "Deny")!);
            var denying = Bar(column);
            TestContext.Out.WriteLine("deny pressed: " + Show(denying));
            Assert.That(denying.Sentence, Is.EqualTo("Deny Request?"));
            Press(column, Find(denying, "Confirm")!);
            Assert.That(host.Sent.Count, Is.EqualTo(1));
            var sent = (ExecutionRespondToApprovalCommand)host.Sent[0];
            Assert.That((sent.Payload.Decision, sent.Payload.ApprovalId), Is.EqualTo((ApprovalDecision.Deny, WaitingWork.ApprovalId)));
            // Confirm again, and the approval's stale Confirm again: nothing more.
            column.Act(FileScreens.Yes, null);
            Press(column, staleConfirm);
            Assert.That(host.Sent.Count, Is.EqualTo(1), "one decision only");
        }
        finally { RoomDesign.Enabled = was; }
    }

    [Test]
    public void DenyThenCancelThenApproveNeedsTheRequestReadAgain()
    {
        var was = RoomDesign.Enabled; RoomDesign.Enabled = true;
        try
        {
            var (host, work, column) = Asked();
            Press(column, Find(Bar(column), "Deny")!);
            var staleDenyConfirm = Find(Bar(column), "Confirm")!;
            Press(column, Find(Bar(column), "Cancel")!);
            Draw(host, column);
            Press(column, Find(Bar(column), "Approve")!);
            // The denial's Confirm pressed now, with Approve armed and unread: it must not approve.
            Press(column, staleDenyConfirm);
            TestContext.Out.WriteLine("approve armed, the denial's Confirm pressed: sent " + host.Sent.Count + " | " + Show(Bar(column)));
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
            // Resolved meanwhile.
            var (host, work, column) = Asked();
            Press(column, Find(Bar(column), "Approve")!);
            ReadWhole(host, column);
            var confirm = Find(Bar(column), "Confirm")!;
            work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; }, WorkstreamStatus.Running);
            Press(column, confirm);
            TestContext.Out.WriteLine("resolved meanwhile: sent " + host.Sent.Count + " armed " + column.Steering.Armed);
            Assert.That(host.Sent.Count, Is.EqualTo(0));

            // Replaced by another approval meanwhile.
            (host, work, column) = Asked();
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
            (host, work, column) = Asked();
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
            Press(column, Find(Bar(column), "Approve")!);
            ReadWhole(host, column);
            work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", B, secondAt)));
            column.Reflow();
            var bar = column.Frame != null && KitFileBinding.Of(column.Frame!, column.Steering.Armed, column.Now!, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
                new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true) is KitFileBinding binding ? RoomBar.Of(binding, VoiceStage.Idle, false) : null;
            TestContext.Out.WriteLine("second at " + secondAt + ": armed " + column.Steering.Armed + " id " + column.Steering.ArmedApprovalId + " | " + (bar == null ? "no binding: the file's own view draws" : Show(bar)));
            column.Act(FileScreens.Yes, null);
            TestContext.Out.WriteLine("  Yes pressed: sent " + string.Join(", ", host.Sent.OfType<ExecutionRespondToApprovalCommand>().Select(c => c.Payload.Decision + " " + c.Payload.ApprovalId)));
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
            Press(column, Find(Bar(column), "Approve")!);
            ReadWhole(host, column);
            host.Live = false;
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
