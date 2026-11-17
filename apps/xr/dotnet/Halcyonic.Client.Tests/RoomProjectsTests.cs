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
        // FIELD's column: New Project leads, Settings, then Usage (the menu's place, the lead 2026-11-12), Close last where the
        // frame offers it; nothing else.
        Assert.That((bar.Column[0].Control.Action, bar.Column[0].Main), Is.EqualTo((ProjectsScreens.NewProject, true)));
        Assert.That(bar.Column.Where(action => action.Control.Action == MenuFrame.ChooseSection).Select(action => action.Control.Key),
            Is.EqualTo(new[] { MenuPlace.Settings.ToString(), MenuPlace.Usage.ToString() }));
        Assert.That(frame.Lines.Select(line => line.ProjectState?.TasksWords), Is.EqualTo(new[] { "1 Task", "1 Task" }));
    }

    [Test]
    public void TheProjectsPanelIsTheOneLanguagesTable()
    {
        var frame = Projects();
        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle)!;
        // FIELD's sheet, tile f1: HALORA over the title, the heads PROJECT and STATE over the rows, each row's note its state word
        // (one of the words a project's state is said in, never a count), no mark before a name.
        Assert.That(bar.Card!.Label, Is.EqualTo(WorkspaceText.Product));
        Assert.That((bar.Card.Heads?.Name, bar.Card.Heads?.Note), Is.EqualTo(((string?)RoomBar.ProjectHead, (string?)RoomBar.StateHead)));
        Assert.That(bar.Card.List.Select(row => row.Note), Is.EqualTo(frame.Lines.Select(line => line.ProjectState!.Word)));
        Assert.That(bar.Card.List.All(row => row.Kind == RoomKind.None), Is.True, "a project's row carries no mark before its name");
        // The Menu at the panel's top right is the bar's own press, of the frame drawn, never the lit action; the actions stand at
        // the panel's foot.
        Assert.That((bar.PanelMenu?.Control.Action, bar.PanelMenu?.Words, bar.PanelMenu?.Main), Is.EqualTo(((string?)RoomBar.OpenMenu, (string?)RoomBar.MenuWords, (bool?)false)));
        Assert.That(bar.PanelMenu!.Control.Frame, Is.SameAs(frame));
        Assert.That(bar.ActionsAtFoot, Is.True);
        Assert.That(frame.Footer.All.Any(each => each.Prompt.Id == RoomBar.OpenMenu) || frame.Lines.Any(line => line.Action == RoomBar.OpenMenu), Is.False,
            "the Menu is no action the frame offers");
    }

    [Test]
    public void AListsHeadsNameBothColumnsAndOutliveACopy()
    {
        Assert.Throws<ArgumentException>(() => new RoomListHeads("Project", " "));
        Assert.Throws<ArgumentException>(() => new RoomListHeads("", "State"));
        var card = new RoomCard(LineWords.Ours("Projects"), label: "Halora", heads: new RoomListHeads("Project", "State"));
        var copied = card.WithList(new RoomListRow[0]).WithStatus(null).WithPress(null);
        Assert.That((copied.Label, copied.Heads?.Name, copied.Heads?.Note), Is.EqualTo(("Halora", "Project", "State")));
        Assert.That(new RoomCard(LineWords.Ours("Projects"), label: "  ").Label, Is.Null, "a blank label is no label");
    }

    [Test]
    public void AnythingElseOnThePlaceStaysTheOlderViews()
    {
        // Folders to connect beside the projects: the Projects reader declines it (its card lists projects only), so the general
        // reader draws it, every line among its pages, in the frame's order: the projects (their summaries make them choices read
        // in the panel, the lead's 22:04Z), then the Folders heading over its folders, unpressed. Its card is titled by the
        // frame's subject too, so it is told from the Projects reader's by holding the folders.
        var frame = Projects(foldersOpen: true);
        var bar = RoomBar.OfFrame(frame, VoiceStage.Idle)!;
        var drawn = new System.Collections.Generic.List<string>();
        var unpressed = new System.Collections.Generic.List<string>();
        for (var page = 0; page < System.Math.Max(1, bar.GeneralPart.Pages); page++)
        {
            drawn.AddRange(bar.Facts?.Select(row => row.Words.Text) ?? Enumerable.Empty<string>());
            drawn.AddRange(bar.Card?.List.Select(row => row.Name.Text) ?? Enumerable.Empty<string>());
            unpressed.AddRange(bar.Card?.List.Where(row => row.Press == null).Select(row => row.Name.Text) ?? Enumerable.Empty<string>());
            drawn.AddRange(bar.Column.Select(action => action.Words));
            bar.Turn();
        }
        Assert.That(frame.Lines.Select(line => line.Words), Is.SubsetOf(drawn));
        Assert.That(unpressed.Distinct(), Is.EqualTo(frame.Lines.Where(line => line.Action == null).Select(line => line.Words)), "the folders, not the Projects reader's card (a card that fits one page stands on each)");
    }
}
