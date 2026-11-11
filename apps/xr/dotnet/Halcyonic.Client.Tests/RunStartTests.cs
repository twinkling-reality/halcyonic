using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// What a run's start was, as the runtime confirmed it (SCREENS's <see cref="RunStart"/>): the activity log keeps the
/// runtime's started event's plan and the Mac's reason a run is not kept to its folder; the result's card then says
/// Planning as its status and Not Kept to Folder as a slot, in FIELD's words, and nothing where the runtime said nothing.
/// </summary>
public class RunStartTests
{
    private const string NotConfined = "Halcyonic's sandbox is on macOS only, so this run is not confined.";

    private static RunStart? Started(string payload)
    {
        var log = new ActivityLog();
        var changed = log.Record(new[] { ActivityLogBoundTests.Journaled(3, "e1", "runtime.execution.started", payload) });
        Assert.That(changed, Does.Contain("e1"));
        return log.StartedFor("e1");
    }

    [Test]
    public void TheActivityLogKeepsWhatTheRuntimeConfirmedOfTheStartAndNothingItDidNotSay()
    {
        var plain = Started("{\"native_id\":\"n1\"}");
        Assert.That((plain!.Value.Plan, plain.Value.NotConfined), Is.EqualTo((false, false)), "absent never reads as a plan, nor as not kept");
        var plan = Started("{\"native_id\":\"n1\",\"plan\":true}");
        Assert.That(plan!.Value.Plan, Is.True);
        var loose = Started("{\"native_id\":\"n1\",\"confined\":false,\"unconfined_reason\":\"" + NotConfined + "\"}");
        Assert.That((loose!.Value.NotConfined, loose.Value.NotConfinedReason), Is.EqualTo((true, NotConfined)));
        Assert.That(new ActivityLog().StartedFor("e1"), Is.Null, "nothing before the runtime said it started");
    }

    [Test]
    public void TheResultCardSaysPlanningAndNotKeptToFolderInFieldsWords()
    {
        var planning = new RoomResult(false, false, false, null, null, null, start: new RunStart(plan: true, notConfinedReason: null));
        Assert.That(planning.Any, Is.True, "a plan is something to say of the task at rest");
        var card = RoomPanelCards.Result(planning, LineWords.TheirName("Kite Run"))!;
        Assert.That((card.Status!.Value.Title, card.Status.Value.Line, card.Status.Value.Tone), Is.EqualTo((RunStart.Planning, RunStart.PlanningLine, RoomTone.Working)));
        Assert.That(card.AnyDot, Is.False);

        var loose = new RoomResult(false, false, false, true, null, null, start: new RunStart(plan: false, notConfinedReason: NotConfined));
        var slots = RoomPanelCards.Result(loose, LineWords.TheirName("Kite Run"))!.Slots.Select(slot => (slot.Kind, slot.Value.Text)).ToList();
        Assert.That(slots, Does.Contain((RoomKind.Folder, RunStart.NotKeptToFolder)));
        Assert.That(slots.Select(slot => slot.Text), Has.None.Contains("onfined").And.None.Contains("andbox"), "never the mechanism's words on a screen");

        var kept = new RoomResult(false, false, false, true, null, null, start: new RunStart(plan: false, notConfinedReason: null));
        Assert.That(RoomPanelCards.Result(kept, LineWords.TheirName("Kite Run"))!.Slots.Select(slot => slot.Kind), Has.None.EqualTo(RoomKind.Folder));
    }
}
