#nullable enable
using System;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The Projects place, the launch screen, under the redesign: a card whose list is picked from (A17; the census).</summary>
public sealed class RoomProjectsTests
{
    private static WorkOverview Overview()
    {
        var state = new Portfolio()
            .Project("a", "Alpha").Project("b", "Beta")
            .Work("w1", "a", WorkstreamStatus.WaitingForHuman)
            .Work("w2", "a", WorkstreamStatus.Running)
            .Work("w3", "b", WorkstreamStatus.Completed)
            .Apply();
        var visibility = new StageVisibility();
        visibility.UseJournal(Samples.JournalId);
        return WorkOverview.Of(state, visibility, id => id == "w2");
    }

    private static MenuFrame Projects(bool foldersOpen = false) =>
        ProjectsScreens.Projects(new ProjectsScreens.State { Overview = Overview(), Live = true, FoldersOpen = foldersOpen, Now = DateTimeOffset.UnixEpoch, Zone = TimeZoneInfo.Utc, LineHeight = _ => 1d, AvailableHeight = 100d })
            .WithSections(new MenuBar(MenuPlace.Projects, "Nothing waiting").Sections());

    [Test]
    public void TheProjectsPlaceIsACardPickedFromWithItsStatesApart()
    {
        var frame = Projects();
        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle);
        Assert.That(bar, Is.Not.Null, "the launch screen is the redesign's");
        Assert.That(bar!.Card, Is.Not.Null);
        Assert.That(bar.Card!.AnyDot, Is.False, "no fact joined to another on a dot");
        Assert.That(bar.Card.List.Select(row => (row.Name.Text, row.Note, row.Tone, row.Name.AllTheirs)), Is.EqualTo(new[]
        {
            ("Alpha", (string?)"Needs You", RoomTone.Attention, true), ("Beta", (string?)"Paused", RoomTone.Quiet, true),
        }));
        // Each row is picked: its press is the frame's own project choice.
        Assert.That(bar.Card.List.Select(row => (row.Press?.Action, row.Press?.Key)), Is.EqualTo(new[]
        {
            ((string?)ProjectsScreens.ChooseProject, (string?)"a"), (ProjectsScreens.ChooseProject, "b"),
        }));
        Assert.That(bar.Card.List.All(row => ReferenceEquals(row.Press!.Frame, frame)), Is.True, "every press is of the frame drawn");
        // New Project leads; the menu's other places stand in the column; the task count is a fact of the state apart.
        Assert.That((bar.Column[0].Control.Action, bar.Column[0].Main), Is.EqualTo((ProjectsScreens.NewProject, true)));
        Assert.That(bar.Column.Count(action => action.Control.Action == MenuFrame.ChooseSection), Is.GreaterThan(0));
        Assert.That(frame.Lines.Select(line => line.ProjectState?.TasksWords), Is.EqualTo(new[] { "1 Task", "1 Task" }));
    }

    [Test]
    public void AnythingElseOnThePlaceStaysTheOlderViews()
    {
        // Folders to connect beside the projects: the older view draws it until a reader takes folders too.
        Assert.That(RoomBar.OfFrame(Projects(foldersOpen: true), VoiceStage.Idle)?.Card?.Title?.Text, Is.Not.EqualTo(RoomBar.ProjectsTitle));
    }
}
